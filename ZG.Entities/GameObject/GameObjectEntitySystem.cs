using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using UnityEngine;

namespace ZG
{
    public struct GameObjectEntityManager
    {
        public struct Entry
        {
            public Entity entity;
            public UnsafeHashSet<int> instanceIDs;
        }

        private NativeHashMap<int, Entry> __entries;

        private NativeHashMap<int, int> __ids;

        public GameObjectEntityManager(Allocator allocator)
        {
            __entries = new NativeHashMap<int, Entry>(1, allocator);

            __ids = new NativeHashMap<int, int>(1, allocator);
        }

        public void Dispsoe()
        {
            foreach (var entry in __entries)
                entry.Value.instanceIDs.Dispose();

            __entries.Dispose();

            __ids.Dispose();
        }

        public Entity Retain(int prefabID, int instanceID, in Entity entity = default)
        {
            if (__TryGetEntry(prefabID, out int id, out var entry))
            {
                if (Entity.Null != entity && entity != entry.entity)
                    return Entity.Null;
            }
            else if (entity == Entity.Null)
                return Entity.Null;
            else
                entry.instanceIDs = new UnsafeHashSet<int>(1, Allocator.Persistent);

            if (!entry.instanceIDs.Add(instanceID))
                return Entity.Null;

            __entries[id] = entry;

            return entry.entity;
        }

        public Entity Release(int prefabID, int instanceID)
        {
            if (!__TryGetEntry(prefabID, out int id, out var entry))
                return Entity.Null;

            if (!entry.instanceIDs.Remove(instanceID))
                return Entity.Null;

            if (entry.instanceIDs.IsEmpty)
            {
                __entries.Remove(id);

                return Entity.Null;
            }

            __entries[id] = entry;

            return entry.entity;
        }

        private bool __TryGetEntry(int prefabID, out int id, out Entry entry)
        {
            if (!__ids.TryGetValue(prefabID, out id))
            {
                entry = default;

                return false;
            }

            return __entries.TryGetValue(id, out entry);
        }
    }

    public struct EntityOrigin : IComponentData
    {
        public Entity entity;
    }

    public struct EntityParent : IComponentData
    {
        public Entity entity;

        public static Entity GetRoot(in Entity entity, in ComponentLookup<EntityParent> entityParents)
        {
            if(entityParents.HasComponent(entity))
                return GetRoot(entityParents[entity].entity, entityParents);

            return entity;
        }

        public static Entity GetRoot<T>(
            in Entity entity, 
            in ComponentLookup<EntityParent> entityParents, 
            in ComponentLookup<T> values) where T : unmanaged, IComponentData
        {
            if (entityParents.HasComponent(entity))
                return GetRoot(entityParents[entity].entity, entityParents, values);

            return values.HasComponent(entity) ? entity : Entity.Null;
        }

        public static Entity GetRoot<T>(
            in Entity entity,
            in ComponentLookup<EntityParent> entityParents,
            in BufferLookup<T> values) where T : unmanaged, IBufferElementData
        {
            if (entityParents.HasComponent(entity))
                return GetRoot(entityParents[entity].entity, entityParents, values);

            return values.HasBuffer(entity) ? entity : Entity.Null;
        }

        public static Entity Get<T>(
            in Entity entity,
            in ComponentLookup<EntityParent> entityParents,
            in ComponentLookup<T> values) where T : unmanaged, IComponentData
        {
            if (values.HasComponent(entity))
                return entity;

            if (entityParents.HasComponent(entity))
                return Get(entityParents[entity].entity, entityParents, values);

            return Entity.Null;
        }

    }

    public struct EntityStatus : ICleanupComponentData
    {
        public int activeCount;
    }

    [BurstCompile, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), UpdateAfter(typeof(GameObjectEntityInitSystem))]
    public partial struct GameObjectEntityStatusSystem : ISystem
    {
        private struct Collect
        {
            public bool isDisabled;

            [ReadOnly]
            public NativeArray<EntityStatus> entityStates;
            [ReadOnly]
            public NativeArray<Entity> inputs;
            public NativeList<Entity> outputs;

            public void Execute(int index)
            {
                if (isDisabled == entityStates[index].activeCount < 1)
                    return;

                outputs.Add(inputs[index]);
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<EntityStatus> entityStatusType;

            [ReadOnly]
            public ComponentTypeHandle<Disabled> disabledType;

            public NativeList<Entity> entitiesToEnable;
            public NativeList<Entity> entitiesToDisable;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.isDisabled = chunk.Has(ref disabledType);
                collect.entityStates = chunk.GetNativeArray(ref entityStatusType);
                collect.inputs = chunk.GetNativeArray(entityType);
                collect.outputs = collect.isDisabled ? entitiesToEnable : entitiesToDisable;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);
            }
        }

        private EntityQuery __group;

        private EntityTypeHandle __entityType;

        private ComponentTypeHandle<EntityStatus> __entityStatusType;

        private ComponentTypeHandle<Disabled> __disabledType;

        private NativeList<Entity> __entitiesToEnable;
        private NativeList<Entity> __entitiesToDisable;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<EntityObjects, EntityStatus>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            __group.SetChangedVersionFilter(ComponentType.ReadWrite<EntityStatus>());

            __entityType = state.GetEntityTypeHandle();
            __entityStatusType = state.GetComponentTypeHandle<EntityStatus>(true);
            __disabledType = state.GetComponentTypeHandle<Disabled>(true);

            __entitiesToEnable = new NativeList<Entity>(Allocator.Persistent);
            __entitiesToDisable = new NativeList<Entity>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __entitiesToEnable.Dispose();
            __entitiesToDisable.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            CollectEx collect;
            collect.entityType = __entityType.UpdateAsRef(ref state);
            collect.entityStatusType = __entityStatusType.UpdateAsRef(ref state);
            collect.disabledType = __disabledType.UpdateAsRef(ref state);
            collect.entitiesToEnable = __entitiesToEnable;
            collect.entitiesToDisable = __entitiesToDisable;

            collect.RunByRef(__group);

            var entityManager = state.EntityManager;
            if (__entitiesToEnable.Length > 0)
            {
                entityManager.RemoveComponent<Disabled>(__entitiesToEnable.AsArray());

                __entitiesToEnable.Clear();
            }

            if(__entitiesToDisable.Length > 0)
            {
                entityManager.AddComponent<Disabled>(__entitiesToDisable.AsArray());

                __entitiesToDisable.Clear();
            }
        }
    }

    [BurstCompile, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), UpdateAfter(typeof(GameObjectEntityStatusSystem))]
    public partial struct GameObjectEntityHierarchySystem : ISystem
    {
        private struct Collect
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<EntityOrigin> orgins;

            public NativeHashMap<Entity, Entity> instanceEntities;

            public void Execute(int index)
            {
                instanceEntities.TryAdd(orgins[index].entity, entityArray[index]);
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<EntityOrigin> orginType;

            public NativeHashMap<Entity, Entity> instanceEntities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.entityArray = chunk.GetNativeArray(entityType);
                collect.orgins = chunk.GetNativeArray(ref orginType);
                collect.instanceEntities = instanceEntities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);
            }
        }

        [BurstCompile]
        private struct SetParents : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeHashMap<Entity, Entity> instanceEntities;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<EntityParent> entityParents;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];
                if (!entityParents.HasComponent(entity))
                    return;

                var entityParent = entityParents[entity];
                if (!instanceEntities.TryGetValue(entityParent.entity, out Entity parentEntity))
                    return;

                entityParent.entity = parentEntity;

                entityParents[entity] = entityParent;
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityQuery __group;

        private NativeHashMap<Entity, Entity> __instanceEntities;

        public void OnCreate(ref SystemState state)
        {
            __group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<EntityOrigin>()
                    }, 

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __instanceEntities = new NativeHashMap<Entity, Entity>(1, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            __instanceEntities.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //var instanceEntities = new UnsafeParallelHashMap<Entity, Entity>(__group.CalculateEntityCount(), Allocator.TempJob);

            __instanceEntities.Clear();

            state.CompleteDependency();

            CollectEx collect;
            collect.entityType = state.GetEntityTypeHandle();
            collect.orginType = state.GetComponentTypeHandle<EntityOrigin>(true);
            collect.instanceEntities = __instanceEntities;
            collect.Run(__group);

            state.EntityManager.RemoveComponent<EntityOrigin>(__group);

            SetParents setParents;
            setParents.entityArray = __instanceEntities.GetValueArray(Allocator.TempJob);
            setParents.instanceEntities = __instanceEntities;
            setParents.entityParents = state.GetComponentLookup<EntityParent>();

            state.Dependency = setParents.Schedule(setParents.entityArray.Length, InnerloopBatchCount, state.Dependency);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(EntityObjectSystemGroup))]
    public partial struct GameObjectEntityDestroySystem : ISystem
    {
        private EntityQuery __group;
        private EntityCommandFactory __factory;

        public void OnCreate(ref SystemState state)
        {
            __group = state.GetEntityQuery(
                ComponentType.ReadOnly<EntityStatus>(),
                ComponentType.Exclude<EntityObjects>());

            __factory = state.World.GetFactory();
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityArray = __group.ToEntityArray(state.WorldUpdateAllocator);
            state.EntityManager.RemoveComponent<EntityStatus>(__group);

            state.Dependency = __factory.ClearEntity(entityArray, state.Dependency);
        }
    }

    [UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), UpdateBefore(typeof(EntityCommandFactorySystem))]
    public partial class GameObjectEntityDeserializedSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            try
            {
                GameObjectEntity.DisposeAllDestoriedEntities();
                GameObjectEntity.CreateAllDeserializedEntities();
            }
            catch(Exception e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }
    }

    [UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), UpdateAfter(typeof(EntityCommandFactorySystem))]
    public partial class GameObjectEntityInitSystem : SystemBase
    {
        public int innerloopBatchCount = 1;

        public static GameObjectEntityManager manager
        {
            get;

            private set;
        }

        private EntityQuery __group;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<GameObjectEntityHandle>(),
                    ComponentType.ReadWrite<EntityStatus>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

            manager = new GameObjectEntityManager(Allocator.Persistent);
        }

        protected override void OnUpdate()
        {
            //TODO: 
            CompleteDependency();

            GameObjectEntity gameObjectEntity;

            NativeList<Entity> entitiesToDestroy = default;
            //using (var entityArray = __group.ToEntityArray(Allocator.Temp))
            using (var chunks = __group.ToArchetypeChunkArray(Allocator.Temp))
            //using (var handles = __group.ToComponentDataArray<GameObjectEntityHandle>(Allocator.Temp))
            {
                NativeArray<Entity> entityArray;
                NativeArray<EntityStatus> entityStates;
                DynamicBuffer<GameObjectEntityHandle> gcHandles;
                BufferAccessor<GameObjectEntityHandle> gcHandleBufferAccessor;
                BufferTypeHandle<GameObjectEntityHandle> gcHandleType = GetBufferTypeHandle<GameObjectEntityHandle>();
                ComponentTypeHandle<EntityStatus> entityStatusType = GetComponentTypeHandle<EntityStatus>();
                EntityTypeHandle entityType = GetEntityTypeHandle();
                Entity entity;
                EntityStatus entityStatus;
                GCHandle gcHandle;
                ArchetypeChunk chunk;
                int numEntities, numChunks = chunks.Length, numGCHandles, i, j, k;
                for (i = 0; i < numChunks; ++i)
                {
                    chunk = chunks[i];
                    numEntities = chunk.Count;
                    entityArray = chunk.GetNativeArray(entityType);
                    entityStates = chunk.GetNativeArray(ref entityStatusType);
                    gcHandleBufferAccessor = chunk.GetBufferAccessor(ref gcHandleType);
                    for (j = 0; j < numEntities; ++j)
                    {
                        entity = entityArray[j];

                        entityStatus = entityStates[j];

                        gcHandles = gcHandleBufferAccessor[j];

                        numGCHandles = gcHandles.Length;
                        for (k = 0; k < numGCHandles; ++k)
                        {
                            gcHandle = gcHandles[k].value;

                            gameObjectEntity = (GameObjectEntity)gcHandle.Target;
                            if (gameObjectEntity == null)
                            {
                                gcHandles.RemoveAt(k--);

                                --numGCHandles;
                            }
                            else
                            {
                                try
                                {
                                    gameObjectEntity._Create(entity);
                                }
                                catch (Exception e)
                                {
                                    Debug.LogException(e.InnerException ?? e, gameObjectEntity);
                                }

                                if (gameObjectEntity.isActiveAndEnabled)
                                    ++entityStatus.activeCount;
                            }
                            gcHandle.Free();
                        }

                        if (numGCHandles < 1)
                        {
                            if (!entitiesToDestroy.IsCreated)
                                entitiesToDestroy = new NativeList<Entity>(Allocator.Temp);

                            entitiesToDestroy.Add(entity);
                        }
                        else
                            entityStates[j] = entityStatus;
                    }
                }
            }

            var entityManager = EntityManager;
            entityManager.RemoveComponent<GameObjectEntityHandle>(__group);

            if (entitiesToDestroy.IsCreated)
            {
                entityManager.DestroyEntity(entitiesToDestroy.AsArray());

                entitiesToDestroy.Dispose();
            }
        }
    }

}