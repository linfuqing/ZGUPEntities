using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace ZG
{
    public struct EntityStructChangeCommander
    {
        private struct ComponentEntity : IEquatable<ComponentEntity>
        {
            public Entity value;
            public TypeIndex componentTypeIndex;

            public bool Equals(ComponentEntity other)
            {
                return value == other.value && componentTypeIndex == other.componentTypeIndex;
            }

            public override int GetHashCode()
            {
                return value.GetHashCode() ^ componentTypeIndex;
            }
        }

        public struct Writer
        {
            private NativeParallelHashMap<ComponentEntity, bool> __componentStates;

            internal Writer(ref EntityStructChangeCommander instance)
            {
                __componentStates = instance.__componentStates;
            }

            public void Clear()
            {
                __componentStates.Clear();
            }

            public bool AddComponent<T>(in Entity entity)
            {
                ComponentEntity componentEntity;
                componentEntity.value = entity;
                componentEntity.componentTypeIndex = TypeManager.GetTypeIndex<T>();
                return __componentStates.TryAdd(componentEntity, true);
            }

            public bool RemoveComponent<T>(in Entity entity)
            {
                ComponentEntity componentEntity;
                componentEntity.value = entity;
                componentEntity.componentTypeIndex = TypeManager.GetTypeIndex<T>();

                return __componentStates.TryAdd(componentEntity, false);
            }
        }

        public struct ParallelWriter
        {
            private NativeParallelHashMap<ComponentEntity, bool>.ParallelWriter __componentStates;

            internal ParallelWriter(ref EntityStructChangeCommander instance)
            {
                __componentStates = instance.__componentStates.AsParallelWriter();
            }

            public bool AddComponent<T>(in Entity entity)
            {
                ComponentEntity componentEntity;
                componentEntity.value = entity;
                componentEntity.componentTypeIndex = TypeManager.GetTypeIndex<T>();
                return __componentStates.TryAdd(componentEntity, true);
            }

            public bool RemoveComponent<T>(in Entity entity)
            {
                ComponentEntity componentEntity;
                componentEntity.value = entity;
                componentEntity.componentTypeIndex = TypeManager.GetTypeIndex<T>();

                return __componentStates.TryAdd(componentEntity, false);
            }
        }

        public struct ReadOnly
        {
            [ReadOnly]
            private NativeParallelHashMap<ComponentEntity, bool> __componentStates;

            internal ReadOnly(in EntityStructChangeCommander instance)
            {
                __componentStates = instance.__componentStates;
            }

            public void AppendTo(ref EntityStructChangeCommander commander)
            {
                KeyValue<ComponentEntity, bool> keyValue;
                var enumerator = __componentStates.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    keyValue = enumerator.Current;

                    commander.__componentStates[keyValue.Key] = keyValue.Value;
                }
            }
        }

        [BurstCompile]
        private struct Init : IJob
        {
            public NativeParallelHashMap<ComponentEntity, bool> componentStates;
            public NativeParallelMultiHashMap<int, Entity> addComponentCommanders;
            public NativeParallelMultiHashMap<int, Entity> removeComponentCommanders;

            public void Execute()
            {
                if (componentStates.IsEmpty)
                    return;

                var enumerator = componentStates.GetEnumerator();
                KeyValue<ComponentEntity, bool> keyValue;
                ComponentEntity componentEntity;
                while (enumerator.MoveNext())
                {
                    keyValue = enumerator.Current;
                    componentEntity = keyValue.Key;

                    if (keyValue.Value)
                        addComponentCommanders.Add(componentEntity.componentTypeIndex, componentEntity.value);
                    else
                        removeComponentCommanders.Add(componentEntity.componentTypeIndex, componentEntity.value);
                }

                //componentStates.Clear();
            }
        }

        private struct AddComponentCommander
        {
            /*[BurstCompile]
            private struct AddComponents : IJob
            {
                public ExclusiveEntityTransaction exclusiveEntityTransaction;
                public NativeParallelMultiHashMap<ComponentType, Entity> addComponentCommanders;

                public void Execute()
                {
                    var componentTypes = addComponentCommanders.GetKeyArray(Allocator.Temp);
                    {
                        int length = componentTypes.ConvertToUniqueArray();
                        ComponentType componentType;
                        Entity entity;
                        NativeParallelMultiHashMap<ComponentType, Entity>.Enumerator enumerator;
                        for (int i = 0; i < length; ++i)
                        {
                            componentType = componentTypes[i];
                            enumerator = addComponentCommanders.GetValuesForKey(componentType);
                            while (enumerator.MoveNext())
                            {
                                entity = enumerator.Current;
                                if (exclusiveEntityTransaction.HasComponent(entity, componentType))
                                    continue;

                                exclusiveEntityTransaction.AddComponent(entity, componentType);
                            }
                        }
                    }

                    addComponentCommanders.Clear();
                }
            }*/

            public NativeParallelMultiHashMap<int, Entity> addComponentCommanders;

            /*public JobHandle Execute(ExclusiveEntityTransaction exclusiveEntityTransaction, in JobHandle inputDeps)
            {
                AddComponents addComponents;
                addComponents.exclusiveEntityTransaction = exclusiveEntityTransaction;
                addComponents.addComponentCommanders = addComponentCommanders;

                return addComponents.Schedule(inputDeps);
            }*/

            public void Playback(ref SystemState systemState)
            {
                /*if (addComponentCommanders.IsEmpty)
                    return;*/

                EntityManager entityManager = systemState.EntityManager;

                using (var componentTypeIndices = addComponentCommanders.GetKeyArray(Allocator.Temp))
                {
                    ComponentType componentType;
                    componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;

                    int length = componentTypeIndices.Length, count = componentTypeIndices.ConvertToUniqueArray();
                    Entity entity;
                    NativeParallelMultiHashMap<int, Entity>.Enumerator enumerator;
                    {
                        var entities = new NativeArray<Entity>(length, Allocator.Temp);
                        int entityCount;
                        for (int i = 0; i < count; ++i)
                        {
                            componentType.TypeIndex = componentTypeIndices[i];

                            entityCount = 0;

                            enumerator = addComponentCommanders.GetValuesForKey(componentType.TypeIndex);
                            while (enumerator.MoveNext())
                            {
                                entity = enumerator.Current;
                                if (!entityManager.Exists(entity) || entityManager.HasComponent(entity, componentType))
                                    continue;

                                entities[entityCount++] = entity;
                            }

                            if (entityCount > 0)
                            {
                                //systemState.CompleteDependency();

                                entityManager.AddComponentBurstCompatible(entities.GetSubArray(0, entityCount), componentType);
                            }
                        }

                        entities.Dispose();
                    }
                }

                //addComponentCommanders.Clear();
            }
        }

        private struct RemoveComponentCommander
        {
            /*[BurstCompile]
            private struct RemoveComponents : IJob
            {
                public ExclusiveEntityTransaction exclusiveEntityTransaction;
                public NativeParallelMultiHashMap<ComponentType, Entity> removeComponentCommanders;

                public void Execute()
                {
                    var componentTypes = removeComponentCommanders.GetKeyArray(Allocator.Temp);
                    {
                        int length = componentTypes.ConvertToUniqueArray();
                        ComponentType componentType;
                        Entity entity;
                        NativeParallelMultiHashMap<ComponentType, Entity>.Enumerator enumerator;
                        for (int i = 0; i < length; ++i)
                        {
                            componentType = componentTypes[i];
                            enumerator = removeComponentCommanders.GetValuesForKey(componentType);
                            while (enumerator.MoveNext())
                            {
                                entity = enumerator.Current;
                                if (exclusiveEntityTransaction.HasComponent(entity, componentType))
                                    continue;

                                exclusiveEntityTransaction.RemoveComponent(entity, componentType);
                            }
                        }
                    }

                    removeComponentCommanders.Clear();
                }
            }*/

            public NativeParallelMultiHashMap<int, Entity> removeComponentCommanders;

            /*public JobHandle Execute(ExclusiveEntityTransaction exclusiveEntityTransaction, in JobHandle inputDeps)
            {
                RemoveComponents removeComponents;
                removeComponents.exclusiveEntityTransaction = exclusiveEntityTransaction;
                removeComponents.removeComponentCommanders = removeComponentCommanders;

                return removeComponents.Schedule(inputDeps);
            }*/

            public void Playback(ref SystemState systemState)
            {
                /*if (removeComponentCommanders.IsEmpty)
                    return;*/

                using (var componentTypeIndices = removeComponentCommanders.GetKeyArray(Allocator.Temp))
                {
                    EntityManager entityManager = systemState.EntityManager;

                    ComponentType componentType;
                    componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;

                    int length = componentTypeIndices.Length, count = componentTypeIndices.ConvertToUniqueArray();
                    Entity entity;
                    NativeParallelMultiHashMap<int, Entity>.Enumerator enumerator;
                    {
                        var entities = new NativeArray<Entity>(length, Allocator.Temp);

                        int entityCount;
                        for (int i = 0; i < count; ++i)
                        {
                            componentType.TypeIndex = componentTypeIndices[i];

                            entityCount = 0;
                            enumerator = removeComponentCommanders.GetValuesForKey(componentType.TypeIndex);
                            while (enumerator.MoveNext())
                            {
                                entity = enumerator.Current;
                                if (!entityManager.HasComponent(entity, componentType))
                                    continue;

                                entities[entityCount++] = entity;
                            }

                            if (entityCount > 0)
                            {
                                //systemState.CompleteDependency();

                                entityManager.RemoveComponent(entities.GetSubArray(0, entityCount), componentType);
                            }
                        }
                        entities.Dispose();
                    }
                }

                //removeComponentCommanders.Clear();
            }
        }

        private NativeParallelHashMap<ComponentEntity, bool> __componentStates;

#if ENABLE_PROFILER
        private ProfilerMarker __addComponentProfilerMarker;
        private ProfilerMarker __removeComponentProfilerMarker;
#endif

        public Writer writer => new Writer(ref this);

        public EntityStructChangeCommander(Allocator allocator)
        {
            __componentStates = new NativeParallelHashMap<ComponentEntity, bool>(1, allocator);

#if ENABLE_PROFILER
            __addComponentProfilerMarker = new ProfilerMarker("AddComponents");
            __removeComponentProfilerMarker = new ProfilerMarker("RemoveComponents");
#endif
        }

        public ReadOnly AsReadOnly()
        {
            return new ReadOnly(this);
        }

        public ParallelWriter AsParallelWriter(int capacity)
        {
            __componentStates.Capacity = math.max(__componentStates.Capacity, __componentStates.Count() + capacity);

            return new ParallelWriter(ref this);
        }

        public void Dispose()
        {
            __componentStates.Dispose();
        }

        public bool IsAddOrRemoveComponent(in Entity entity, in TypeIndex componentTypeIndex, out bool status)
        {
            ComponentEntity componentEntity;
            componentEntity.value = entity;
            componentEntity.componentTypeIndex = componentTypeIndex;

            return __componentStates.TryGetValue(componentEntity, out status);
        }

        public bool HasComponent<T>(in Entity entity)
        {
            ComponentEntity componentEntity;
            componentEntity.value = entity;
            componentEntity.componentTypeIndex = TypeManager.GetTypeIndex<T>();
            return __componentStates.TryGetValue(componentEntity, out var status) && status;
        }

        public bool AddComponent<T>(in Entity entity)
        {
            ComponentEntity componentEntity;
            componentEntity.value = entity;
            componentEntity.componentTypeIndex = TypeManager.GetTypeIndex<T>();
            if (__componentStates.TryGetValue(componentEntity, out var status))
            {
                if (status)
                    return false;
                else
                    __componentStates.Remove(componentEntity);
            }
            else
                __componentStates[componentEntity] = true;

            return true;
        }

        public bool RemoveComponent<T>(in Entity entity)
        {
            ComponentEntity componentEntity;
            componentEntity.value = entity;
            componentEntity.componentTypeIndex = TypeManager.GetTypeIndex<T>();
            if (__componentStates.TryGetValue(componentEntity, out var status))
            {
                if (status)
                    __componentStates.Remove(componentEntity);
                else
                    return false;
            }
            else
                __componentStates[componentEntity] = false;

            return true;
        }

        public void Clear()
        {
            __componentStates.Clear();
        }

        public void Apply(ref SystemState systemState)
        {
            NativeParallelMultiHashMap<int, Entity> addComponentCommanders;
            NativeParallelMultiHashMap<int, Entity> removeComponentCommanders;

            if (__componentStates.IsEmpty)
            {
                addComponentCommanders = default;
                removeComponentCommanders = default;
            }
            else
            {
                addComponentCommanders = new NativeParallelMultiHashMap<int, Entity>(1, Allocator.Temp);
                removeComponentCommanders = new NativeParallelMultiHashMap<int, Entity>(1, Allocator.Temp);

                Init init;
                init.componentStates = __componentStates;
                init.addComponentCommanders = addComponentCommanders;
                init.removeComponentCommanders = removeComponentCommanders;
                init.Execute();
            }

            if (addComponentCommanders.IsCreated)
            {
#if ENABLE_PROFILER
                using (__addComponentProfilerMarker.Auto())
#endif
                {
                    if (!addComponentCommanders.IsEmpty)
                    {
                        AddComponentCommander addComponentCommander;
                        addComponentCommander.addComponentCommanders = addComponentCommanders;
                        addComponentCommander.Playback(ref systemState);
                    }

                    addComponentCommanders.Dispose();
                }
            }

            if (removeComponentCommanders.IsCreated)
            {
#if ENABLE_PROFILER
                using (__removeComponentProfilerMarker.Auto())
#endif
                {
                    if (!removeComponentCommanders.IsEmpty)
                    {
                        RemoveComponentCommander removeComponentCommander;
                        removeComponentCommander.removeComponentCommanders = removeComponentCommanders;
                        removeComponentCommander.Playback(ref systemState);
                    }

                    removeComponentCommanders.Dispose();
                }
            }
        }

        public void Playback(ref SystemState systemState)
        {
            Apply(ref systemState);

            __componentStates.Clear();
        }
    }

    public struct EntitySharedComponentCommander
    {
        private struct SetSharedComponentCommander
        {
            public EntitySharedComponentAssigner assigner;

#if ENABLE_PROFILER
            public ProfilerMarker setSharedComponentProfilerMarker;
#endif
            public void Playback(ref SystemState systemState)
            {
#if ENABLE_PROFILER
                using (setSharedComponentProfilerMarker.Auto())
#endif
                    assigner.Playback(systemState.EntityManager);
            }
        }

        private EntitySharedComponentAssigner __assigner;
        private EntityStructChangeCommander __structChangeCommander;

#if ENABLE_PROFILER
        private ProfilerMarker __setSharedComponentProfilerMarker;
#endif

        public EntitySharedComponentCommander(Allocator allocator)
        {
            __assigner = new EntitySharedComponentAssigner(allocator);
            __structChangeCommander = new EntityStructChangeCommander(allocator);

#if ENABLE_PROFILER
            __setSharedComponentProfilerMarker = new ProfilerMarker("SetSharedComponents");
#endif
        }

        public void Dispose()
        {
            __assigner.Dispose();
            __structChangeCommander.Dispose();
        }

        public bool IsAddOrRemoveComponent(in Entity entity, int componentTypeIndex, out bool status) => __structChangeCommander.IsAddOrRemoveComponent(entity, componentTypeIndex, out status);

        public bool AddComponent<T>(in Entity entity) => __structChangeCommander.AddComponent<T>(entity);

        public bool RemoveComponent<T>(in Entity entity)
        {
            if (__structChangeCommander.RemoveComponent<T>(entity))
            {
                __assigner.RemoveComponent<T>(entity);

                return true;
            }

            return false;
        }

        public bool TryGetSharedComponentData<T>(in Entity entity, ref T value) where T : struct, ISharedComponentData => __assigner.TryGetSharedComponentData<T>(entity, ref value);

        public void SetSharedComponentData<T>(in Entity entity, in T value) where T : struct, ISharedComponentData
        {
            __assigner.SetSharedComponentData(entity, value);
        }

        public void AddSharedComponentData<T>(in Entity entity, in T value) where T : struct, ISharedComponentData
        {
            AddComponent<T>(entity);

            __assigner.SetSharedComponentData(entity, value);
        }

        public void Playback(ref SystemState systemState)
        {
            __structChangeCommander.Playback(ref systemState);

#if ENABLE_PROFILER
            using (__setSharedComponentProfilerMarker.Auto())
#endif
                __assigner.Playback(systemState.EntityManager);
        }
    }
}