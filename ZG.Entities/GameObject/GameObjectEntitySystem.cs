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

    }

    [BurstCompile, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), UpdateAfter(typeof(GameObjectEntityInitSystem))]
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

        private EntityQuery __group;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<GameObjectEntityHandle>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        }

        protected override void OnUpdate()
        {
            //TODO: 
            CompleteDependency();

            GameObjectEntity gameObjectEntity;

            NativeList<Entity> entitiesToDisabled = default;
            NativeList<Entity> entitiesToDestroy = default;
            using (var entityArray = __group.ToEntityArray(Allocator.Temp))
            using (var handles = __group.ToComponentDataArray<GameObjectEntityHandle>(Allocator.Temp))
            {
                GCHandle handle;
                Entity entity;
                int length = entityArray.Length;
                for (int i = 0; i < length; ++i)
                {
                    entity = entityArray[i];
                    handle = handles[i].value;

                    gameObjectEntity = (GameObjectEntity)handle.Target;
                    if (gameObjectEntity == null)
                    {
                        if (!entitiesToDestroy.IsCreated)
                            entitiesToDestroy = new NativeList<Entity>(Allocator.Temp);

                        entitiesToDestroy.Add(entityArray[i]);
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

                        if (!gameObjectEntity.isActiveAndEnabled)
                        {
                            if (!entitiesToDisabled.IsCreated)
                                entitiesToDisabled = new NativeList<Entity>(Allocator.Temp);

                            entitiesToDisabled.Add(entityArray[i]);
                        }
                    }
                    handle.Free();
                }
            }

            var entityManager = EntityManager;
            entityManager.RemoveComponent<GameObjectEntityHandle>(__group);

            if (entitiesToDisabled.IsCreated)
            {
                entityManager.AddComponent<Disabled>(entitiesToDisabled.AsArray());

                entitiesToDisabled.Dispose();
            }

            if (entitiesToDestroy.IsCreated)
            {
                entityManager.DestroyEntity(entitiesToDestroy.AsArray());

                entitiesToDestroy.Dispose();
            }
        }
    }

}