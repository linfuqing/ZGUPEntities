using System;
using System.Runtime.InteropServices;
//using System.Runtime.InteropServices;
//using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using UnityEngine;

namespace ZG
{
    public interface IGameObjectEntityStatus
    {
        int value { get; set; }
    }

    /*[NativeContainer]
    public unsafe struct GameObjectEntityPool
    {
        private struct Data
        {
            public UnsafeHashMap<int, Prefab> prefabs;

            public UnsafeHashMap<int, int> ids;

            public int count;
        }

        [NativeDisableUnsafePtrRestriction]
        private SharedMultiHashMap<Entity, int> __instanceIDs;

        public readonly AllocatorManager.AllocatorHandle Allocator;

        public GameObjectEntityPool(in AllocatorManager.AllocatorHandle allocator)
        {
            Allocator = allocator;

            __data = AllocatorManager.Allocate<Data>(allocator);

            //__data->lookupJobManager = default;

            __data->prefabs = new UnsafeHashMap<int, Prefab>(1, allocator);

            __data->ids = new UnsafeHashMap<int, int>(1, allocator);

            __data->count = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

            CollectionHelper.SetStaticSafetyId<GameObjectEntityPool>(ref m_Safety, ref StaticSafetyID.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }

        public void Dispsoe()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

            //__data->lookupJobManager.CompleteReadWriteDependency();

            __data->prefabs.Dispose();

            __data->ids.Dispose();

            AllocatorManager.Free(Allocator, __data);

            __data = null;
        }

        public int CreateID(in Entity entity)
        {
            if (entity == Entity.Null)
                return 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            int id = System.Threading.Interlocked.Increment(ref __data->count);

            Prefab prefab;
            prefab.entity = entity;
            prefab.instanceIDs = default;

            __data->prefabs[id] = prefab;

            return id;
        }

        public void Bind(int instanceID, int id)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            __data->ids.Add(instanceID, id);
        }

        public int Retain(int instanceID, ref Entity entity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            Prefab prefab;
            if (__data->ids.TryGetValue(instanceID, out int id))
            {
                if (__data->prefabs.TryGetValue(id, out prefab))
                {
                    if (Entity.Null != entity && entity != prefab.entity)
                        return 0;

                    entity = prefab.entity;
                }
                else
                    prefab.instanceIDs = new UnsafeHashSet<int>(1, Allocator);
            }
            else
            {
                id = CreateID(entity);
                if (id == 0)
                    return 0;

                __data->ids[instanceID] = id;

                prefab.entity = entity;
                prefab.instanceIDs = new UnsafeHashSet<int>(1, Allocator);
            }

            if (!prefab.instanceIDs.Add(instanceID))
                return 0;

            __data->prefabs[id] = prefab;

            return id;
        }

        public Entity Release(int prefabID, int instanceID)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            if (!__data->ids.TryGetValue(prefabID, out int id))
                return Entity.Null;

            if (!__data->prefabs.TryGetValue(id, out var prefab))
                return Entity.Null;

            if (!prefab.instanceIDs.Remove(instanceID))
                return Entity.Null;

            if (!prefab.instanceIDs.IsEmpty)
            {
                __data->prefabs[id] = prefab;

                return Entity.Null;
            }

            __data->prefabs.Remove(id);

            return prefab.entity;
        }
    }*/

    public struct GameObjectEntityInstanceCount : IComponentData, IEnableableComponent, IGameObjectEntityStatus
    {
        public int value;

        int IGameObjectEntityStatus.value
        {
            get => value;

            set => this.value = value;
        }
    }

    public struct GameObjectEntityActiveCount : IComponentData, IEnableableComponent, IGameObjectEntityStatus
    {
        public int value;

        int IGameObjectEntityStatus.value
        {
            get => value;

            set => this.value = value;
        }
    }

    public struct EntityOrigin : IComponentData
    {
        public Entity entity;
    }

    public struct EntityParent : IBufferElementData, IEnableableComponent
    {
        public Entity entity;

        /*public static Entity GetRoot(in Entity entity, in BufferLookup<EntityParent> entityParents)
        {
            if(entityParents.HasBuffer(entity))
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
        }*/

        public static Entity Get<T>(
            in Entity entity,
            in BufferLookup<EntityParent> entityParents,
            in ComponentLookup<T> values) where T : unmanaged, IComponentData
        {
            if (values.HasComponent(entity))
                return entity;

            if (entityParents.HasBuffer(entity))
            {
                Entity parent;
                foreach (var entityParent in entityParents[entity])
                {
                    parent = Get(entityParent.entity, entityParents, values);
                    if (parent != Entity.Null)
                        return parent;
                }
            }

            return Entity.Null;
        }

        public static Entity Get<T>(
            in Entity entity,
            in BufferLookup<EntityParent> entityParents,
            in BufferLookup<T> values) where T : unmanaged, IBufferElementData
        {
            if (values.HasBuffer(entity))
                return entity;

            if (entityParents.HasBuffer(entity))
            {
                Entity parent;
                foreach (var entityParent in entityParents[entity])
                {
                    parent = Get(entityParent.entity, entityParents, values);
                    if (parent != Entity.Null)
                        return parent;
                }
            }

            return Entity.Null;
        }

        public static Entity Get<T>(
            in DynamicBuffer<EntityParent> entityParents, 
            in BufferLookup<T> values) where T : unmanaged, IBufferElementData
        {
            foreach (var entityParent in entityParents)
            {
                if (values.HasBuffer(entityParent.entity))
                    return entityParent.entity;

            }

            return Entity.Null;
        }

        public static Entity Get<T>(
            in DynamicBuffer<EntityParent> entityParents,
            in ComponentLookup<T> values) where T : unmanaged, IComponentData
        {
            foreach (var entityParent in entityParents)
            {
                if (values.HasComponent(entityParent.entity))
                    return entityParent.entity;

            }

            return Entity.Null;
        }
    }

    [BurstCompile,
        CreateAfter(typeof(EntityCommandFactorySystem)),
        UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true),
        UpdateAfter(typeof(EntityCommandFactorySystem))]
    public partial struct GameObjectEntityFactorySystem : ISystem
    {
        private struct CollectInstances
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<GameObjectEntityInstanceCount> instanceCounts;

            public EntityCommander.ParallelWriter entityManager;
            //public NativeList<Entity> entitiesToDestroy;

            public void Execute(int index)
            {
                if (instanceCounts[index].value < 1)
                    entityManager.DestroyEntity(entityArray[index]);
            }
        }

        [BurstCompile]
        private struct CollectInstancesEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;

            public ComponentTypeHandle<GameObjectEntityInstanceCount> instanceCountType;

            //public NativeList<Entity> entitiesToDestroy;
            public EntityCommander.ParallelWriter entityManager;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectInstances collect;
                collect.entityArray = chunk.GetNativeArray(entityType);
                collect.instanceCounts = chunk.GetNativeArray(ref instanceCountType);
                collect.entityManager = entityManager;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    chunk.SetComponentEnabled(ref instanceCountType, i, false);

                    collect.Execute(i);
                }
            }
        }

        private struct CollectActives
        {
            public bool isDisabled;

            [ReadOnly]
            public NativeArray<GameObjectEntityActiveCount> activeCounts;
            [ReadOnly]
            public NativeArray<Entity> entityArray;
            public EntityCommander.ParallelWriter entityManager;
            //public NativeList<Entity> entitiesToChange;

            public void Execute(int index)
            {
                if (activeCounts[index].value > 0 == isDisabled)
                {
                    if(isDisabled)
                        entityManager.RemoveComponent<Disabled>(entityArray[index]);
                    else
                        entityManager.AddComponent<Disabled>(entityArray[index]);
                }
            }
        }

        [BurstCompile]
        private struct CollectActivesEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<Disabled> disabledType;

            public ComponentTypeHandle<GameObjectEntityActiveCount> activeCountType;

            public EntityCommander.ParallelWriter entityManager;

            //public NativeList<Entity> entitiesToEnable;
            //public NativeList<Entity> entitiesToDisable;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectActives collect;
                collect.isDisabled = chunk.Has(ref disabledType);
                collect.activeCounts = chunk.GetNativeArray(ref activeCountType);
                collect.entityArray = chunk.GetNativeArray(entityType);
                collect.entityManager = entityManager;//collect.isDisabled ? entitiesToEnable : entitiesToDisable;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    chunk.SetComponentEnabled(ref activeCountType, i, false);

                    collect.Execute(i);
                }
            }
        }

        private EntityQuery __instanceGroup;
        private EntityQuery __activeGroup;

        private EntityTypeHandle __entityType;

        private ComponentTypeHandle<GameObjectEntityInstanceCount> __instanceCountType;

        private ComponentTypeHandle<GameObjectEntityActiveCount> __activeCountType;

        //private ComponentTypeHandle<EntityOrigin> __originType;

        private ComponentTypeHandle<Disabled> __disabledType;

        private EntityCommander __commander;

        /*private NativeList<Entity> __entitiesToEnable;
        private NativeList<Entity> __entitiesToDisable;*/
        //private NativeList<Entity> __entitiesToDestroy;

        public EntityCommandFactory factory
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __instanceGroup = builder
                    .WithAllRW<GameObjectEntityInstanceCount>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            //__instanceGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameObjectEntityInstanceCount>());

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __activeGroup = builder
                    .WithAllRW<GameObjectEntityActiveCount>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            //__activeGroup.SetChangedVersionFilter(ComponentType.ReadWrite<GameObjectEntityActiveCount>());

            __entityType = state.GetEntityTypeHandle();
            __instanceCountType = state.GetComponentTypeHandle<GameObjectEntityInstanceCount>();
            __activeCountType = state.GetComponentTypeHandle<GameObjectEntityActiveCount>();
            __disabledType = state.GetComponentTypeHandle<Disabled>(true);

            var world = state.WorldUnmanaged;
            __commander = world.GetExistingSystemUnmanaged<EntityCommanderSystem>().value;
            factory = world.GetExistingSystemUnmanaged<EntityCommandFactorySystem>().factory;

            //__entitiesToDestroy = new NativeList<Entity>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            //__entitiesToDestroy.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //__commander.Playback(ref state);

            var entityManager = __commander.AsParallelWriter(
                0, 
                __activeGroup.CalculateEntityCountWithoutFiltering(), 
                __instanceGroup.CalculateEntityCountWithoutFiltering());

            var jobHandle = JobHandle.CombineDependencies(__commander.jobHandle, state.Dependency);

            CollectInstancesEx collectInstances;
            collectInstances.entityType = __entityType.UpdateAsRef(ref state);
            collectInstances.instanceCountType = __instanceCountType.UpdateAsRef(ref state);
            collectInstances.entityManager = entityManager;

            jobHandle = collectInstances.ScheduleParallelByRef(__instanceGroup, jobHandle);

            CollectActivesEx collectActive;
            collectActive.entityType = __entityType.UpdateAsRef(ref state);
            collectActive.disabledType = __disabledType.UpdateAsRef(ref state);
            collectActive.activeCountType = __activeCountType.UpdateAsRef(ref state);
            collectActive.entityManager = entityManager;

            jobHandle = collectActive.ScheduleParallelByRef(__activeGroup, jobHandle);

            jobHandle = factory.ClearEntity(__commander.destroiedEntities, jobHandle);

            __commander.jobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile, 
        CreateAfter(typeof(EntityCommandFactorySystem)), 
        UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), 
        UpdateAfter(typeof(GameObjectEntityFactorySystem))]
    public partial struct GameObjectEntityHierarchySystem : ISystem
    {
        private struct Reset
        {
            [ReadOnly]
            public SharedHashMap<Entity, Entity>.Reader instances;

            public BufferAccessor<EntityParent> parents;

            public bool Execute(int index)
            {
                bool result = true;
                Entity entity;
                var parents = this.parents[index];
                int numParents = parents.Length;
                for(int i = 0; i < numParents; ++i)
                {
                    ref var parent = ref parents.ElementAt(i);

                    if (instances.TryGetValue(parent.entity, out entity))
                    {
                        parent.entity = entity;

                        UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0);
                    }
                    else
                        result &= parent.entity.Index >= 0;
                }

                return result;
            }
        }

        [BurstCompile]
        private struct ResetEx : IJobChunk
        {
            [ReadOnly]
            public SharedHashMap<Entity, Entity>.Reader instances;

            public BufferTypeHandle<EntityParent> parentType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Reset reset;
                reset.instances = instances;
                reset.parents = chunk.GetBufferAccessor(ref parentType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if (reset.Execute(i))
                        chunk.SetComponentEnabled(ref parentType, i, false);
                }
            }
        }

        private EntityQuery __group;

        private BufferTypeHandle<EntityParent> __parentType;

        private SharedHashMap<Entity, Entity> __instances;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAllRW<EntityParent>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __group.SetChangedVersionFilter(ComponentType.ReadWrite<EntityParent>());

            __parentType = state.GetBufferTypeHandle<EntityParent>();

            __instances = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityCommandFactorySystem>().factory.instances;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //var instanceEntities = new UnsafeParallelHashMap<Entity, Entity>(__group.CalculateEntityCount(), Allocator.TempJob);

            ResetEx reset;
            reset.instances = __instances.reader;
            reset.parentType = __parentType.UpdateAsRef(ref state);

            ref var lookupJobManager = ref __instances.lookupJobManager;

            var jobHandle = JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, state.Dependency);

            jobHandle = reset.ScheduleParallelByRef(__group, jobHandle);

            lookupJobManager.AddReadOnlyDependency(jobHandle);

            state.Dependency = jobHandle;
        }
    }

    [UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), UpdateBefore(typeof(EntityCommandFactorySystem))]
    public partial class GameObjectEntityDeserializedSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            try
            {
                GameObjectEntity.DestroiedEntity.DisposeAllDestoriedEntities();
                GameObjectEntity.CreateAllDeserializedEntities();

                //Debug.Log($"Deserialized End {UnityEngine.Time.frameCount}");
            }
            catch(Exception e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }
    }

    [BurstCompile, RequireMatchingQueriesForUpdate, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true), 
        UpdateAfter(typeof(GameObjectEntityFactorySystem))]
    public partial struct GameObjectEntityInitSystem : ISystem
    {
        public struct FunctionWrapper : IFunctionWrapper
        {
            public Entity entity;
            public GCHandle gcHandle;

            public void Invoke()
            {
                var gameObjectEntity = gcHandle.IsAllocated ? (GameObjectEntity)gcHandle.Target : null;
                if (gameObjectEntity != null)
                {
                    gameObjectEntity._Create(entity);
                    
                    gcHandle.Free();
                }
            }
        }
        
        private struct Callback
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;
            //[ReadOnly]
            //public NativeArray<EntityOrigin> entityOrigins;
            [ReadOnly]
            public BufferAccessor<GameObjectEntityHandle> gcHandles;

            public NativeList<FunctionWrapper> functionWrappers;

            public void Execute(int index)
            {
                FunctionWrapper functionWrapper;
                functionWrapper.entity = entityArray[index];

                //Debug.Log($"Apply {entity} To {entityOrigins[index].entity}");
                foreach (var gcHandle in gcHandles[index])
                {
                    functionWrapper.gcHandle = gcHandle.value;
                    
                    functionWrappers.Add(functionWrapper);
                }
            }
        }

        [BurstCompile]
        private struct CallbackEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;
            //[ReadOnly]
            //public ComponentTypeHandle<EntityOrigin> entityOriginType;
            [ReadOnly]
            public BufferTypeHandle<GameObjectEntityHandle> gcHandleType;

            public NativeList<FunctionWrapper> functionWrappers;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Callback callback;
                callback.entityArray = chunk.GetNativeArray(entityType);
                //callback.entityOrigins = chunk.GetNativeArray(ref entityOriginType);
                callback.gcHandles = chunk.GetBufferAccessor(ref gcHandleType);
                callback.functionWrappers = functionWrappers;
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    callback.Execute(i);
            }
        }

        private EntityQuery __group;

        private EntityTypeHandle __entityType;
        private BufferTypeHandle<GameObjectEntityHandle> __gcHandleType;
        
        private NativeList<FunctionWrapper> __functionWrappers;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using(var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<GameObjectEntityHandle>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
            
            __entityType = state.GetEntityTypeHandle();
            __gcHandleType = state.GetBufferTypeHandle<GameObjectEntityHandle>(true);

            __functionWrappers = new NativeList<FunctionWrapper>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __functionWrappers.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //TODO: 
            state.CompleteDependency();

            CallbackEx callback;
            callback.entityType = __entityType.UpdateAsRef(ref state);
            callback.gcHandleType = __gcHandleType.UpdateAsRef(ref state);
            callback.functionWrappers = __functionWrappers;
            callback.RunByRef(__group);

            state.EntityManager.RemoveComponent<GameObjectEntityHandle>(__group);

            int numFunctionWrappers = __functionWrappers.Length;
            for(int i = 0; i < numFunctionWrappers; ++i)
                __functionWrappers.ElementAt(i).Run();

            __functionWrappers.Clear();
        }
    }

}