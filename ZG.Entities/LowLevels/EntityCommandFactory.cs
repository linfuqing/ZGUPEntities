using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using static ZG.TimeManager<T>;

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
            //public UnsafeList<Entity> entitiesToDestroy;
        }

        private struct Instance
        {
            public int refIndex;
            public int versionIndex;
        }

        [BurstCompile]
        private struct Collect<T> : IJob where T : unmanaged, IEquatable<T>
        {
            public int version;

            public int numKeys;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<T> keys;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            public UnsafeParallelMultiHashMap<T, int> indices;

            public SharedHashMap<Entity, Entity>.Writer prefabs;

            public SharedHashMap<Entity, Entity>.Writer entities;

            public void Execute()
            {
                int entityIndex = 0;

                T key;
                Entity source, destination;
                source.Version = version;

                for (int i = 0; i < numKeys; ++i)
                {
                    key = keys[i];
                    if (indices.TryGetFirstValue(key, out source.Index, out var iterator))
                    {
                        do
                        {
                            destination = entityArray[entityIndex++];

                            entities[source] = destination;

                            prefabs[destination] = source;

                        } while (indices.TryGetNextValue(out source.Index, ref iterator));
                    }
                }

                indices.Clear();
            }
        }

        [BurstCompile]
        private struct Clear : IJob
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            public SharedHashMap<Entity, Entity>.Writer entities;

            public SharedHashMap<Entity, Entity>.Writer prefabs;

            public UnsafeParallelHashMap<Entity, ComponentTypeSet> systemStateComponentTypes;
            public UnsafeParallelHashMap<Entity, ComponentTypeSet> instanceComponentTypes;

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

                        entities.Remove(prefab);
                        systemStateComponentTypes.Remove(prefab);
                        instanceComponentTypes.Remove(prefab);

#if UNITY_EDITOR
                        names.Remove(prefab);
#endif
                    }
                }
            }
        }

        [BurstCompile]
        private struct Destroy : IJobParallelFor
        {
            public UnsafeListEx<Entity> results;

            public SharedHashMap<Entity, Entity>.Writer entities;

            public SharedHashMap<Entity, Entity>.Writer prefabs;

            public UnsafeParallelHashMap<Entity, ComponentTypeSet> systemStateComponentTypes;
            public UnsafeParallelHashMap<Entity, ComponentTypeSet> instanceComponentTypes;

#if UNITY_EDITOR
            public UnsafeParallelHashMap<Entity, FixedString128Bytes> names;
#endif

            public void Execute(int index)
            {
                Entity prefab = results[index];
                if (entities.TryGetValue(prefab, out Entity entity))
                {
                    entities.Remove(prefab);

                    prefabs.Remove(entity);

                    results[index] = entity;
                }
                else
                {
                    UnityEngine.Debug.LogError($"Missing Prefab {prefab}");

                    results[index] = Entity.Null;
                }

                systemStateComponentTypes.Remove(prefab);
                instanceComponentTypes.Remove(prefab);

#if UNITY_EDITOR
                names.Remove(prefab);
#endif
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

        public readonly Allocator allocator;

        private unsafe Data* __data;
        private UnsafeListEx<Entity> __entitiesToDestroy;
        private UnsafeParallelHashMap<Entity, ComponentTypeSet> __systemStateComponentTypes;
        private UnsafeParallelHashMap<Entity, ComponentTypeSet> __instanceComponentTypes;
        private UnsafeParallelMultiHashMap<EntityArchetype, int> __createEntityCommander;
        private UnsafeParallelMultiHashMap<Entity, int> __instantiateCommander;

#if UNITY_EDITOR
        private UnsafeParallelHashMap<Entity, FixedString128Bytes> __names;
#endif

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

        public SharedHashMap<Entity, Entity> entities
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

        public unsafe EntityCommandFactory(Allocator allocator)
        {
            BurstUtility.InitializeJob<Collect<EntityArchetype>>();
            BurstUtility.InitializeJob<Collect<Entity>>();
            BurstUtility.InitializeJob<Clear>();
            BurstUtility.InitializeJobParallelFor<Destroy>();

            this.allocator = allocator;

            int size = UnsafeUtility.SizeOf<Data>();
            __data = (Data*)UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<Data>(), allocator);
            UnsafeUtility.MemClear(__data, size);
            //__data->entitiesToDestroy = new UnsafeList<Entity>(0, allocator);

            __entitiesToDestroy = new UnsafeListEx<Entity>(allocator);

            __systemStateComponentTypes = new UnsafeParallelHashMap<Entity, ComponentTypeSet>(1, allocator);
            __instanceComponentTypes = new UnsafeParallelHashMap<Entity, ComponentTypeSet>(1, allocator);

            __createEntityCommander = new UnsafeParallelMultiHashMap<EntityArchetype, int>(1, allocator);
            __instantiateCommander = new UnsafeParallelMultiHashMap<Entity, int>(1, allocator);

            prefabs = new SharedHashMap<Entity, Entity>(allocator);
            entities = new SharedHashMap<Entity, Entity>(allocator);

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
            //__data->entitiesToDestroy.Dispose();

            UnsafeUtility.Free(__data, allocator);
            __data = null;

            __entitiesToDestroy.Dispose();

            __systemStateComponentTypes.Dispose();
            __instanceComponentTypes.Dispose();

            __createEntityCommander.Dispose();
            __instantiateCommander.Dispose();

            var prefabs = this.prefabs;
            prefabs.lookupJobManager.CompleteReadWriteDependency();
            prefabs.Dispose();

            var entities = this.entities;
            entities.lookupJobManager.CompleteReadWriteDependency();
            entities.Dispose();

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

            __entitiesToDestroy.Add(entity);
        }

        public Entity CreateEntity(in EntityArchetype entityArchetype, ComponentTypeSet stateComponentTypes = default, ComponentTypeSet instanceComponentTypes = default)
        {
            Entity entity = __GetEntity();

            __CompletePrefabJob();

            __createEntityCommander.Add(entityArchetype, entity.Index);

            if(stateComponentTypes.Length > 0)
                __systemStateComponentTypes.Add(entity, stateComponentTypes);

            if (instanceComponentTypes.Length > 0)
                __instanceComponentTypes.Add(entity, instanceComponentTypes);

            return entity;
        }

        public Entity Instantiate(in Entity prefab, ComponentTypeSet componentTypes = default)
        {
            Entity entity = __GetEntity();

            __CompleteInstanceJob();

            //int refIndex = __entities.length;

            //__entities.Add(entity);
            __instantiateCommander.Add(prefab, entity.Index);

            if (componentTypes.Length > 0)
                __instanceComponentTypes.Add(entity, componentTypes);

            return entity;
            /*Instance instance;
            instance.refIndex = refIndex;
            instance.versionIndex = entity.Index;
            __instantiateCommander.Add(prefab, instance);

            return new EntityCommandRef(refIndex, __entities);*/
        }

        public void AddComponents(in Entity prefab, in ComponentTypeSet componentTypes)
        {
            if (componentTypes.Length < 1)
                return;

            if (__instanceComponentTypes.TryGetValue(prefab, out var originComponentTypes))
            {
                var results = new FixedList128Bytes<ComponentType>();

                int numComponentTypes = originComponentTypes.Length, i;
                for (i = 0; i < numComponentTypes; ++i)
                    results.Add(originComponentTypes.GetComponentType(i));

                numComponentTypes = componentTypes.Length;
                for (i = 0; i < numComponentTypes; ++i)
                    results.Add(componentTypes.GetComponentType(i));

                __instanceComponentTypes[prefab] = new ComponentTypeSet(results);
            }
            else
                __instanceComponentTypes[prefab] = componentTypes;
        }

        public JobHandle ClearEntity(in NativeArray<Entity> entityArray, in JobHandle inputDeps)
        {
            Clear clear;
            clear.entityArray = entityArray;
            clear.entities = entities.writer;
            clear.prefabs = prefabs.writer;
            clear.systemStateComponentTypes = __systemStateComponentTypes;
            clear.instanceComponentTypes = __instanceComponentTypes;

#if UNITY_EDITOR
            clear.names = __names;
#endif

            ref var entityJobManager = ref entities.lookupJobManager;
            ref var prefabJobManager = ref prefabs.lookupJobManager;

            var jobHandle = JobHandle.CombineDependencies(inputDeps, entityJobManager.readWriteJobHandle, prefabJobManager.readWriteJobHandle);
            jobHandle = clear.Schedule(jobHandle);

            entityJobManager.readWriteJobHandle = jobHandle;
            prefabJobManager.readWriteJobHandle = jobHandle;

            return jobHandle;
        }

        public void Playback(ref SystemState systemState)
        {
            var entities = this.entities;
            var entitiesReader = entities.reader;
            var entitiesWriter = entities.writer;

            var prefabs = this.prefabs;
            var prefabsReader = prefabs.reader;
            var prefabsWriter = prefabs.writer;

            if (!__entitiesToDestroy.isEmpty)
            {
                prefabs.lookupJobManager.CompleteReadWriteDependency();
                entities.lookupJobManager.CompleteReadWriteDependency();

                Destroy destroy;
                destroy.results = __entitiesToDestroy;
                destroy.prefabs = prefabsWriter;
                destroy.entities = entitiesWriter;
                destroy.systemStateComponentTypes = __systemStateComponentTypes;
                destroy.instanceComponentTypes = __instanceComponentTypes;
#if UNITY_EDITOR
                destroy.names = __names;
#endif
                destroy.Run(__entitiesToDestroy.length);

                systemState.EntityManager.DestroyEntity(__entitiesToDestroy.AsArray()/*Unsafe.CollectionUtility.ToNativeArray(ref entitiesToDestroy)*/);

                __entitiesToDestroy.Clear();
            }

            EntityManager entityManager = systemState.EntityManager;

            int count = __createEntityCommander.Count();
            if (count > 0)
            {
                using (var entityArray = new NativeArray<Entity>(count, Allocator.TempJob))
                {
                    prefabs.lookupJobManager.CompleteReadWriteDependency();
                    entities.lookupJobManager.CompleteReadWriteDependency();

                    var keys = __createEntityCommander.GetKeyArray(Allocator.TempJob);

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
                    collect.version = version;
                    collect.numKeys = numKeys;
                    collect.keys = keys;
                    collect.entityArray = entityArray;
                    collect.indices = __createEntityCommander;
                    collect.prefabs = prefabsWriter;
                    collect.entities = entitiesWriter;
                    //systemState.Dependency = collect.Schedule(systemState.Dependency);
                    collect.Run();
                }
            }
            else
                entities.lookupJobManager.CompleteReadOnlyDependency();

            Entity prefab, instance;
            KeyValue<Entity, ComponentTypeSet> pair;
            var enumerator = __instanceComponentTypes.GetEnumerator();
            while (enumerator.MoveNext())
            {
                pair = enumerator.Current;

                prefab = pair.Key;
                if (!entitiesReader.TryGetValue(prefab, out instance))
                    continue;

                entityManager.AddComponent(instance, pair.Value);
            }

            prefabAssigner.Playback(ref systemState, entitiesReader, prefabs);

            prefabJobHandle = systemState.Dependency;// = prefabs.Dispose(systemState.Dependency);

            count = __instantiateCommander.Count();
            if (count > 0)
            {
                using (var entityArray = new NativeArray<Entity>(count, Allocator.TempJob))
                {
                    prefabs.lookupJobManager.CompleteReadWriteDependency();
                    entities.lookupJobManager.CompleteReadWriteDependency();

                    var keys = __instantiateCommander.GetKeyArray(Allocator.TempJob);
                    int numKeys = keys.ConvertToUniqueArray();

                    //systemState.CompleteDependency();
                    __CompletePrefabJob();

                    int offset = 0, length, i, j;
                    Entity key, value;
                    ComponentTypeSet componentTypes;
                    NativeArray<Entity> subEntities;
                    for (i = 0; i < numKeys; ++i)
                    {
                        key = keys[i];

                        if (entitiesReader.TryGetValue(key, out value))
                        {
                            length = __instantiateCommander.CountValuesForKey(key);

                            subEntities = entityArray.GetSubArray(offset, length);
                            entityManager.Instantiate(value, subEntities);

                            if (__systemStateComponentTypes.TryGetValue(key, out componentTypes))
                                entityManager.AddComponent(subEntities, componentTypes);

                            offset += length;
                        }
                        else
                            keys[i--] = keys[--numKeys];
                    }

                    subEntities = entityArray.GetSubArray(0, offset);

                    //var instances = new NativeHashMap<Entity, Entity>(count, Allocator.TempJob);

                    Collect<Entity> collect;
                    collect.version = version;
                    collect.numKeys = numKeys;
                    collect.keys = keys;
                    collect.entityArray = subEntities;
                    //collect.entities = __entities.AsArray();
                    collect.indices = __instantiateCommander;
                    collect.prefabs = prefabsWriter;
                    collect.entities = entitiesWriter;
                    //systemState.Dependency = collect.Schedule(systemState.Dependency);
                    collect.Run();

                    foreach (Entity entity in subEntities)
                    {
                        if (!prefabsReader.TryGetValue(entity, out prefab) ||
                            !__instanceComponentTypes.TryGetValue(prefab, out componentTypes))
                            continue;

                        //__instanceComponentTypes.Remove(prefab);

                        entityManager.AddComponent(entity, componentTypes);
                    }
                }
                //entityArray.Dispose();
            }
            else
                entities.lookupJobManager.CompleteReadOnlyDependency();

            instanceAssigner.Playback(ref systemState, entitiesReader, prefabs);

            instanceJobHandle = systemState.Dependency;// = instances.Dispose(systemState.Dependency);

            __instanceComponentTypes.Clear();

            __UpdateVersion();

#if UNITY_EDITOR
            __PlaybackNames(ref systemState);
#endif
        }

#if UNITY_EDITOR
        [BurstDiscard]
        private void __PlaybackNames(ref SystemState systemState)
        {
            var entityManager = systemState.EntityManager;

            using (var names = __names.GetKeyValueArrays(Allocator.Temp))
            {
                var reader = entities.reader;
                int length = names.Length;
                for(int i = 0; i < length; ++i)
                    entityManager.SetName(reader[names.Keys[i]], names.Values[i].ToString());
            }

            __names.Clear();
        }
#endif

        private unsafe void __UpdateVersion()
        {
            ++__data->version;

            __data->count = 0;
        }

        private unsafe Entity __GetEntity()
        {
            Entity entity;
            entity.Index = -(++__data->count);
            entity.Version = __data->version;

            return entity;
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

        /*private unsafe ref UnsafeList<Entity> __GetEntitiesToDestroy()
        {
            //return ref UnsafeUtility.AsRef<Data>(__data).entitiesToDestroy;
        }*/
    }
}