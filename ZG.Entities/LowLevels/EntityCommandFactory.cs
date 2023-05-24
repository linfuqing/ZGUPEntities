using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG.Unsafe;
using System.Collections.Generic;

namespace ZG
{
    /*public struct EntityCommandRef
    {
        public static readonly EntityCommandRef Null = new EntityCommandRef(-1, default);

        private readonly int __index;
        private readonly UnsafeListEx<Entity> __list;

        public EntityCommandRef(int index, in UnsafeListEx<Entity> list)
        {
            __index = index;
            __list = list;
        }

        public static unsafe implicit operator Entity(in EntityCommandRef value)
        {
            if (value.__list.isCreated)
                return value.__list[value.__index];

            return Entity.Null;
        }
    }*/

    public struct EntityCommandFactory
    {
        private struct Data
        {
            public int version;
            public int count;
            public JobHandle prefabJobHandle;
            public JobHandle instanceJobHandle;
            public UnsafeList<Entity> entitiesToDestroy;
        }

        private struct Instance
        {
            public int refIndex;
            public int versionIndex;
        }

        [BurstCompile]
        private struct Collect<T> : IJob where T : unmanaged, IEquatable<T>
        {
            public int numKeys;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<T> keys;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            public UnsafeParallelMultiHashMap<T, Entity> entities;

            public SharedHashMap<Entity, Entity>.Writer prefabs;

            public SharedHashMap<Entity, Entity>.Writer instances;

            public void Execute()
            {
                int entityIndex = 0;

                T key;
                Entity source, destination;

                for (int i = 0; i < numKeys; ++i)
                {
                    key = keys[i];
                    if (entities.TryGetFirstValue(key, out source, out var iterator))
                    {
                        do
                        {
                            destination = entityArray[entityIndex++];

                            instances[source] = destination;

                            prefabs[destination] = source;

                        } while (entities.TryGetNextValue(out source, ref iterator));
                    }
                }

                entities.Clear();
            }
        }

        [BurstCompile]
        private struct Apply : IJob
        {
            [ReadOnly]
            public EntityComponentAssigner.ReadOnly source;
            public EntityComponentAssigner.Writer destination;

            public void Execute()
            {
                source.AppendTo(destination);
                //UnityEngine.Assertions.Assert.IsFalse(result);
            }
        }

        [BurstCompile]
        private struct Clear : IJob
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            public SharedHashMap<Entity, Entity>.Writer instances;

            public SharedHashMap<Entity, Entity>.Writer prefabs;

            public UnsafeParallelMultiHashMap<Entity, TypeIndex> systemStateComponentTypes;
            public UnsafeParallelMultiHashMap<Entity, TypeIndex> instanceComponentTypes;

#if UNITY_EDITOR
            public UnsafeParallelHashMap<Entity, FixedString128Bytes> names;
#endif

            public void Execute()
            {
                Entity entity, prefab;
                int length = entityArray.Length;
                for (int i = 0; i < length; ++i)
                {
                    entity = entityArray[i];

                    if (prefabs.TryGetValue(entity, out prefab))
                    {
                        prefabs.Remove(entity);

                        instances.Remove(prefab);
                        systemStateComponentTypes.Remove(prefab);
                        instanceComponentTypes.Remove(prefab);

#if UNITY_EDITOR
                        names.Remove(prefab);
#endif
                    }
                }
            }
        }

        /*[BurstCompile]
        private struct CollectInstances : IJob
        {
            public int version;

            public int numKeys;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> keys;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            public NativeArray<Entity> entities;

            public UnsafeParallelMultiHashMap<Entity, Instance> instances;

            public UnsafeHashMap<Entity, Entity> entityMap;

            public void Execute()
            {
                Instance instance;
                Entity key, source, destination;
                int entityIndex = 0;

                source.Version = version;
                for (int i = 0; i < numKeys; ++i)
                {
                    key = keys[i];
                    if (instances.TryGetFirstValue(key, out instance, out var iterator))
                    {
                        do
                        {
                            destination = entityArray[entityIndex++];

                            entities[instance.refIndex] = destination;

                            source.Index = instance.versionIndex;

                            entityMap.TryAdd(source, destination);

                        } while (instances.TryGetNextValue(out instance, ref iterator));
                    }
                }

                instances.Clear();
            }
        }*/

        private unsafe Data* __data;
        private UnsafeParallelMultiHashMap<Entity, TypeIndex> __systemStateComponentTypes;
        private UnsafeParallelMultiHashMap<Entity, TypeIndex> __instanceComponentTypes;
        private UnsafeParallelMultiHashMap<EntityArchetype, Entity> __createEntityCommander;
        private UnsafeParallelMultiHashMap<Entity, Entity> __instantiateCommander;
        private EntityComponentAssigner __prefabAssignerBuffer;
        private EntityComponentAssigner __instanceAssignerBuffer;

#if UNITY_EDITOR
        private UnsafeParallelHashMap<Entity, FixedString128Bytes> __names;
#endif

        public readonly AllocatorManager.AllocatorHandle Allocator;

        public unsafe bool isCreated => __data != null;

        public unsafe int count => __data->count;

        public unsafe int version => __data->version;

        public unsafe JobHandle prefabJobHandle
        {
            get => __data->prefabJobHandle;

            private set => __data->prefabJobHandle = value;
        }

        public unsafe JobHandle instanceJobHandle
        {
            get => __data->instanceJobHandle;

            private set => __data->instanceJobHandle = value;
        }

        public SharedHashMap<Entity, Entity> prefabs
        {
            get;
        }

        public SharedHashMap<Entity, Entity> instances
        {
            get;
        }

        public EntityComponentAssigner prefabAssigner
        {
            get;

            private set;
        }

        public EntityComponentAssigner instanceAssigner
        {
            get;

            private set;
        }

        public unsafe EntityCommandFactory(in AllocatorManager.AllocatorHandle allocator)
        {
            Allocator = allocator;

            __data = AllocatorManager.Allocate<Data>(allocator);
            UnsafeUtility.MemClear(__data, UnsafeUtility.SizeOf<Data>());
            __data->entitiesToDestroy = new UnsafeList<Entity>(0, allocator);

            __systemStateComponentTypes = new UnsafeParallelMultiHashMap<Entity, TypeIndex>(1, allocator);
            __instanceComponentTypes = new UnsafeParallelMultiHashMap<Entity, TypeIndex>(1, allocator);

            __createEntityCommander = new UnsafeParallelMultiHashMap<EntityArchetype, Entity>(1, allocator);
            __instantiateCommander = new UnsafeParallelMultiHashMap<Entity, Entity>(1, allocator);

            __prefabAssignerBuffer = new EntityComponentAssigner(allocator);
            __instanceAssignerBuffer = new EntityComponentAssigner(allocator);

            prefabs = new SharedHashMap<Entity, Entity>(allocator);
            instances = new SharedHashMap<Entity, Entity>(allocator);

            prefabAssigner = new EntityComponentAssigner(allocator);
            instanceAssigner = new EntityComponentAssigner(allocator);

#if UNITY_EDITOR
            __names = new UnsafeParallelHashMap<Entity, FixedString128Bytes>(1, allocator);
#endif
        }

        public unsafe void Dispose()
        {
            __data->prefabJobHandle.Complete();
            __data->instanceJobHandle.Complete();
            __data->entitiesToDestroy.Dispose();

            AllocatorManager.Free(Allocator, __data);
            __data = null;

            __systemStateComponentTypes.Dispose();
            __instanceComponentTypes.Dispose();

            __createEntityCommander.Dispose();
            __instantiateCommander.Dispose();

            __prefabAssignerBuffer.Dispose();
            __instanceAssignerBuffer.Dispose();

            var prefabs = this.prefabs;
            prefabs.lookupJobManager.CompleteReadWriteDependency();
            prefabs.Dispose();

            var instances = this.instances;
            instances.lookupJobManager.CompleteReadWriteDependency();
            instances.Dispose();

            prefabAssigner.Dispose();
            instanceAssigner.Dispose();

#if UNITY_EDITOR
            __names.Dispose();
#endif
        }

#if UNITY_EDITOR
        public void SetName(in Entity entity, string name)
        {
            __names[entity] = name;
        }
#endif

        public void DestroyEntity(in Entity entity)
        {
            /*__CompletePrefabJob();

            __CompleteInstanceJob();

            prefabs.lookupJobManager.CompleteReadWriteDependency();

            if (prefabs.writer.Remove(entity))
            {
                __prefabComponentTypes.Remove(entity);

                return true;
            }

            return false;*/

            __GetEntitiesToDestroy().Add(entity);
        }

        public unsafe Entity CreateEntity()
        {
            Entity entity;
            entity.Index = -(++__data->count);
            entity.Version = __data->version;

            return entity;
        }

        public bool InitEntity(in Entity entity, in EntityArchetype entityArchetype)
        {
            //__CompletePrefabJob();

            if(__createEntityCommander.TryGetFirstValue(entityArchetype, out Entity prefab, out var iterator))
            {
                do
                {
                    if (prefab == entity)
                        return false;

                } while (__createEntityCommander.TryGetNextValue(out prefab, ref iterator));
            }

            __createEntityCommander.Add(entityArchetype, entity);

            return true;
        }

        public void Instantiate(in Entity entity, in Entity prefab)
        {
            //__CompleteInstanceJob();

            //int refIndex = __entities.length;

            //__entities.Add(entity);
            __instantiateCommander.Add(prefab, entity);
            /*Instance instance;
            instance.refIndex = refIndex;
            instance.versionIndex = entity.Index;
            __instantiateCommander.Add(prefab, instance);

            return new EntityCommandRef(refIndex, __entities);*/
        }

        public bool HasComponent<T>(in Entity entity)
        {
            var destination = TypeManager.GetTypeIndex<T>();
            if (__systemStateComponentTypes.TryGetFirstValue(entity, out var source, out var iterator))
            {
                do
                {
                    if (source == destination)
                        return true;
                } while (__instanceComponentTypes.TryGetNextValue(out source, ref iterator));
            }

            if (__instanceComponentTypes.TryGetFirstValue(entity, out source, out iterator))
            {
                do
                {
                    if (source == destination)
                        return true;

                } while (__instanceComponentTypes.TryGetNextValue(out source, ref iterator));
            }

            return false;
        }

        public void SetComponentEnabled<T>(in Entity prefab, in bool value) where T : struct, IEnableableComponent
        {
            instanceAssigner.SetComponentEnabled<T>(prefab, value);
        }

        public void SetBuffer<TValue, TCollection>(in Entity prefab, in TCollection values) 
            where TValue : struct, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            instanceAssigner.SetBuffer<TValue, TCollection>(true, prefab, values);
        }

        public void SetBuffer<T>(in Entity prefab, T[] values) where T : unmanaged, IBufferElementData
        {
            instanceAssigner.SetBuffer(true, prefab, values);
        }

        public void SetBuffer<T>(in Entity prefab, in NativeArray<T> values) where T : struct, IBufferElementData
        {
            instanceAssigner.SetBuffer(true, prefab, values);
        }

        public void SetComponentData<T>(in Entity prefab, in T value) where T : struct, IComponentData
        {
            instanceAssigner.SetComponentData(prefab, value);
        }

        public void AddStateComponent(in Entity prefab, in ComponentType componentType)
        {
            __systemStateComponentTypes.Add(prefab, componentType.TypeIndex);
        }

        public void AddComponent(in Entity prefab, in ComponentType componentType)
        {
            __instanceComponentTypes.Add(prefab, componentType.TypeIndex);
        }

        public void AddComponent<T>(in Entity prefab)
        {
            __instanceComponentTypes.Add(prefab, TypeManager.GetTypeIndex<T>());
        }

        public void AddComponentData<T>(in Entity prefab, in T value) where T : struct, IComponentData
        {
            AddComponent<T>(prefab);

            instanceAssigner.SetComponentData(prefab, value);
        }

        public void AddBuffer<T>(in Entity prefab, T[] values) where T : unmanaged, IBufferElementData
        {
            AddComponent<T>(prefab);

            instanceAssigner.SetBuffer(true, prefab, values);
        }

        public void AddBuffer<TValue, TCollection>(in Entity prefab, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            AddComponent<TValue>(prefab);

            instanceAssigner.SetBuffer<TValue, TCollection>(true, prefab, values);
        }

        public void AppendBuffer<T>(in Entity prefab, T[] values) where T : unmanaged, IBufferElementData
        {
            AddComponent<T>(prefab);

            instanceAssigner.SetBuffer(false, prefab, values);
        }

        public void AppendBuffer<TValue, TCollection>(in Entity prefab, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            AddComponent<TValue>(prefab);

            instanceAssigner.SetBuffer<TValue, TCollection>(false, prefab, values);
        }

        public void RemoveComponent<T>(in Entity entity) where T : struct
        {
            var destination = TypeManager.GetTypeIndex<T>();
            if (__systemStateComponentTypes.TryGetFirstValue(entity, out var source, out var iterator))
            {
                do
                {
                    if (source == destination)
                    {
                        __instanceComponentTypes.Remove(iterator);

                        break;
                    }
                } while (__instanceComponentTypes.TryGetNextValue(out source, ref iterator));
            }

            if (__instanceComponentTypes.TryGetFirstValue(entity, out source, out iterator))
            {
                do
                {
                    if (source == destination)
                    {
                        __instanceComponentTypes.Remove(iterator);

                        break;
                    }
                } while (__instanceComponentTypes.TryGetNextValue(out source, ref iterator));
            }

            instanceAssigner.RemoveComponent<T>(entity);
            prefabAssigner.RemoveComponent<T>(entity);
        }

        public JobHandle ClearEntity(in NativeArray<Entity> entityArray, in JobHandle inputDeps)
        {
            Clear clear;
            clear.entityArray = entityArray;
            clear.instances = instances.writer;
            clear.prefabs = prefabs.writer;
            clear.systemStateComponentTypes = __systemStateComponentTypes;
            clear.instanceComponentTypes = __instanceComponentTypes;

#if UNITY_EDITOR
            clear.names = __names;
#endif

            ref var instanceJobManager = ref instances.lookupJobManager;
            ref var prefabJobManager = ref prefabs.lookupJobManager;

            var jobHandle = JobHandle.CombineDependencies(inputDeps, instanceJobManager.readWriteJobHandle, prefabJobManager.readWriteJobHandle);
            jobHandle = clear.ScheduleByRef(jobHandle);

            instanceJobManager.readWriteJobHandle = jobHandle;
            prefabJobManager.readWriteJobHandle = jobHandle;

            return jobHandle;
        }

        public void Playback(ref SystemState systemState)
        {
            var instances = this.instances;
            var instancesReader = instances.reader;
            var instancesWriter = instances.writer;

            var prefabs = this.prefabs;
            var prefabsReader = prefabs.reader;
            var prefabsWriter = prefabs.writer;

            EntityManager entityManager = systemState.EntityManager;

            Entity prefab, instance;
            ref var entitiesToDestroy = ref __GetEntitiesToDestroy();
            if (!entitiesToDestroy.IsEmpty)
            {
                prefabs.lookupJobManager.CompleteReadWriteDependency();
                instances.lookupJobManager.CompleteReadWriteDependency();

                for(int i = 0; i < entitiesToDestroy.Length; ++i)
                {
                    prefab = entitiesToDestroy[i];
                    if (instancesWriter.TryGetValue(prefab, out instance))
                    {
                        instancesWriter.Remove(prefab);

                        prefabsWriter.Remove(instance);

                        entitiesToDestroy[i] = instance;
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"Missing Prefab {prefab}");

                        entitiesToDestroy[i] = Entity.Null;
                    }

                    __systemStateComponentTypes.Remove(prefab);
                    __instanceComponentTypes.Remove(prefab);

#if UNITY_EDITOR
                    __names.Remove(prefab);
#endif
                }

                entityManager.DestroyEntity(entitiesToDestroy.AsArray()/*Unsafe.CollectionUtility.ToNativeArray(ref entitiesToDestroy)*/);

                entitiesToDestroy.Clear();
            }

            int count = __createEntityCommander.Count();
            if (count > 0)
            {
                using (var entityArray = new NativeArray<Entity>(count, Unity.Collections.Allocator.TempJob))
                {
                    prefabs.lookupJobManager.CompleteReadWriteDependency();
                    instances.lookupJobManager.CompleteReadWriteDependency();

                    var keys = __createEntityCommander.GetKeyArray(Unity.Collections.Allocator.TempJob);

                    //systemState.CompleteDependency();

                    int numKeys = keys.ConvertToUniqueArray(), offset = 0, length;
                    EntityArchetype key;
                    for (int i = 0; i < numKeys; ++i)
                    {
                        key = keys[i];

                        length = __createEntityCommander.CountValuesForKey(key);

                        entityManager.CreateEntity(key, entityArray.GetSubArray(offset, length));

                        offset += length;
                    }

                    Collect<EntityArchetype> collect;
                    collect.numKeys = numKeys;
                    collect.keys = keys;
                    collect.entityArray = entityArray;
                    collect.entities = __createEntityCommander;
                    collect.prefabs = prefabsWriter;
                    collect.instances = instancesWriter;
                    //systemState.Dependency = collect.Schedule(systemState.Dependency);
                    collect.Run();
                }
            }
            else
                instances.lookupJobManager.CompleteReadOnlyDependency();

            __prefabAssignerBuffer.Clear();

            var prefabAssigner = this.prefabAssigner;
            if (!__instanceComponentTypes.IsEmpty)
            {
                var keys = __instanceComponentTypes.GetKeyArray(Unity.Collections.Allocator.Temp);

                int stepCount = 0, numKeys = keys.ConvertToUniqueArray();
                for (int i = 0; i < numKeys; ++i)
                {
                    prefab = keys[i];

                    if (!instancesWriter.TryGetValue(prefab, out instance))
                    {
                        keys[stepCount++] = prefab;

                        continue;
                    }

                    if (__AddComponents(prefab, instance, default, __instanceComponentTypes, ref entityManager))
                        __instanceComponentTypes.Remove(prefab);
                }

                if (stepCount > 0)
                    prefabAssigner.AsReadOnly().AppendTo(__prefabAssignerBuffer.writer, keys.GetSubArray(0, stepCount));

                keys.Dispose();
            }

            prefabAssigner.Playback(ref systemState, instancesReader, prefabs);

            Apply apply;
            apply.source = __prefabAssignerBuffer.AsReadOnly();
            apply.destination = prefabAssigner.writer;

            var prefabJobHandle = apply.ScheduleByRef(systemState.Dependency);

            this.prefabJobHandle = prefabJobHandle;

            prefabAssigner.jobHandle = prefabJobHandle;// = prefabs.Dispose(systemState.Dependency);

            count = __instantiateCommander.Count();
            if (count > 0)
            {
                using (var entityArray = new NativeArray<Entity>(count, Unity.Collections.Allocator.TempJob))
                {
                    prefabs.lookupJobManager.CompleteReadWriteDependency();
                    instances.lookupJobManager.CompleteReadWriteDependency();

                    var keys = __instantiateCommander.GetKeyArray(Unity.Collections.Allocator.TempJob);
                    int numKeys = keys.ConvertToUniqueArray();

                    //systemState.CompleteDependency();
                    __CompletePrefabJob();

                    int offset = 0, length, i;
                    Entity key, value;
                    NativeArray<Entity> subEntities;
                    for (i = 0; i < numKeys; ++i)
                    {
                        key = keys[i];

                        if (instancesReader.TryGetValue(key, out value))
                        {
                            length = __instantiateCommander.CountValuesForKey(key);

                            subEntities = entityArray.GetSubArray(offset, length);
                            entityManager.Instantiate(value, subEntities);

                            __AddComponents(key, Entity.Null, subEntities, __systemStateComponentTypes, ref entityManager);

                            offset += length;
                        }
                        else
                            keys[i--] = keys[--numKeys];
                    }

                    subEntities = entityArray.GetSubArray(0, offset);

                    //var instances = new NativeHashMap<Entity, Entity>(count, Allocator.TempJob);

                    Collect<Entity> collect;
                    collect.numKeys = numKeys;
                    collect.keys = keys;
                    collect.entityArray = subEntities;
                    //collect.entities = __entities.AsArray();
                    collect.entities = __instantiateCommander;
                    collect.prefabs = prefabsWriter;
                    collect.instances = instancesWriter;
                    //systemState.Dependency = collect.Schedule(systemState.Dependency);
                    collect.Run();

                    foreach (Entity entity in subEntities)
                    {
                        if (!prefabsReader.TryGetValue(entity, out prefab))
                            continue;

                        if(__AddComponents(prefab, entity, default, __instanceComponentTypes, ref entityManager))
                            __instanceComponentTypes.Remove(prefab);
                    }
                }
                //entityArray.Dispose();
            }
            else
                instances.lookupJobManager.CompleteReadOnlyDependency();

            __instanceAssignerBuffer.Clear();

            var instanceAssigner = this.instanceAssigner;
            if (!__instanceComponentTypes.IsEmpty)
            {
                var keys = __instanceComponentTypes.GetKeyArray(Unity.Collections.Allocator.Temp);

                instanceAssigner.AsReadOnly().AppendTo(__instanceAssignerBuffer.writer, keys);
            }

            instanceAssigner.Playback(ref systemState, instancesReader, prefabs);

            apply.source = __instanceAssignerBuffer.AsReadOnly();
            apply.destination = instanceAssigner.writer;

            var instanceJobHandle = apply.ScheduleByRef(systemState.Dependency);// = instances.Dispose(systemState.Dependency);

            this.instanceJobHandle = instanceJobHandle;
            instanceAssigner.jobHandle = instanceJobHandle;

            systemState.Dependency = JobHandle.CombineDependencies(prefabJobHandle, instanceJobHandle);

/*#if UNITY_EDITOR
            JobHandle = systemState.Dependency;
#endif*/

            //systemState.Dependency.Complete();

            //__instanceComponentTypes.Clear();

            __UpdateVersion();

#if UNITY_EDITOR
            __PlaybackNames(ref systemState);
#endif
        }

#if UNITY_EDITOR
        //public static JobHandle JobHandle;

        [BurstDiscard]
        private void __PlaybackNames(ref SystemState systemState)
        {
            var entityManager = systemState.EntityManager;

            using (var names = __names.GetKeyValueArrays(Unity.Collections.Allocator.Temp))
            {
                var reader = instances.reader;
                int length = names.Length;
                for(int i = 0; i < length; ++i)
                    entityManager.SetName(reader[names.Keys[i]], names.Values[i].ToString());
            }

            __names.Clear();
        }
#endif

        private unsafe ref UnsafeList<Entity> __GetEntitiesToDestroy() => ref __data->entitiesToDestroy;

        private unsafe void __UpdateVersion()
        {
            ++__data->version;

            __data->count = 0;
        }

        private unsafe void __CompletePrefabJob()
        {
            __data->prefabJobHandle.Complete();
            __data->prefabJobHandle = default;
        }

        private unsafe void __CompleteInstanceJob()
        {
            __data->instanceJobHandle.Complete();
            __data->instanceJobHandle = default;
        }

        private static bool __AddComponents(
            in Entity source,
            in Entity destination, 
            in NativeArray<Entity> entityArray,
            in UnsafeParallelMultiHashMap<Entity, TypeIndex> entityComponentTypes, 
            ref EntityManager entityManager)
        {
            var componentTypes = new FixedList128Bytes<ComponentType>();
            ComponentType componentType;
            if (entityComponentTypes.TryGetFirstValue(source, out componentType.TypeIndex, out var iterator))
            {
                componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;
                do
                {
                    componentTypes.Add(componentType);
                    if (componentTypes.Length == componentTypes.Capacity)
                    {
                        if(entityArray.IsCreated)
                            entityManager.AddComponent(entityArray, new ComponentTypeSet(componentTypes));
                        else
                            entityManager.AddComponent(destination, new ComponentTypeSet(componentTypes));

                        componentTypes.Clear();
                    }
                }
                while (entityComponentTypes.TryGetNextValue(out componentType.TypeIndex, ref iterator));

                if (componentTypes.Length > 0)
                {
                    if (entityArray.IsCreated)
                        entityManager.AddComponent(entityArray, new ComponentTypeSet(componentTypes));
                    else
                        entityManager.AddComponent(destination, new ComponentTypeSet(componentTypes));
                }

                return true;
            }

            return false;
        }

        /*private unsafe ref UnsafeList<Entity> __GetEntitiesToDestroy()
        {
            //return ref UnsafeUtility.AsRef<Data>(__data).entitiesToDestroy;
        }*/
    }
}