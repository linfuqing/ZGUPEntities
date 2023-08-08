using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;
using ZG.Unsafe;

[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<EntityDataDeserializationIndexContainerSystemCore.Deserializer>))]

namespace ZG
{
    public interface IEntityDataDeserializationIndexWrapper<T>
    {
        T Deserialize(in Entity entity, in NativeArray<int>.ReadOnly guidIndices, ref EntityDataReader reader);
    }

    public interface IEntityDataIndexDeserializer<T>
    {
        T Deserialize(int index, in NativeArray<int>.ReadOnly guidIndices);
    }

    public interface IEntityDataDeserializationIndexContainerSystem
    {
        public SharedList<int> guidIndices { get; }
    }

    public struct EntityDataDeserializationIndexContainerSystemCore
    {
        public struct Deserializer : IEntityDataContainerDeserializer
        {
#if DEBUG
            public NativeText.ReadOnly typeName;
#endif

            [ReadOnly]
            public NativeArray<Hash128>.ReadOnly guids;
            public NativeList<int> guidIndices;

            public void Deserialize(in UnsafeBlock block)
            {
                var guids = block.AsArray<Hash128>();

                int length = guids.Length;
                guidIndices.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    guidIndices[i] = this.guids.IndexOf(guids[i]);
#if DEBUG
                    if (guidIndices[i] == -1)
                        UnityEngine.Debug.LogError($"{guids[i]} In Container {typeName} Deserialize Fail.");
#endif
                }
            }
        }

        private EntityDataDeserializationContainerSystemCore __core;

        public SharedList<int> guidIndices
        {
            get;

            private set;
        }

        public EntityDataDeserializationIndexContainerSystemCore(ref SystemState state)
        {
            __core = new EntityDataDeserializationContainerSystemCore(ref state);

            guidIndices = new SharedList<int>(Allocator.Persistent);
        }

        public void Dispose()
        {
            __core.Dispose();

            guidIndices.Dispose();
        }

        public void Update(
            in NativeArray<Hash128>.ReadOnly guids, 
            ref SystemState state
            )
        {
            ref var guidIndicesJobManager = ref guidIndices.lookupJobManager;

            state.Dependency = JobHandle.CombineDependencies(guidIndicesJobManager.readWriteJobHandle, state.Dependency);

            Deserializer deserializer;
#if DEBUG
            deserializer.typeName = TypeManager.GetSystemName(state.GetSystemTypeIndex());//TypeManager.GetTypeNameFixed();
#endif
            deserializer.guids = guids;
            deserializer.guidIndices = guidIndices.writer;
            __core.Update(ref deserializer, ref state);

            guidIndicesJobManager.readWriteJobHandle = state.Dependency;
        }
    }

    public struct EntityDataDeserializationIndexComponentDataSystemCore<TValue, TWrapper>
        where TValue : unmanaged, IComponentData
        where TWrapper : unmanaged, IEntityDataDeserializationIndexWrapper<TValue>
    {
        public struct Deserializer : IEntityDataDeserializer
        {
            [ReadOnly]
            public SharedList<int>.Reader guidIndices;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            public NativeArray<TValue> instances;

            public TWrapper wrapper;

            public bool Fallback(int index)
            {
                return false;
            }

            public void Deserialize(int index, ref EntityDataReader reader)
            {
                instances[index] = wrapper.Deserialize(entityArray[index], guidIndices.AsArray().AsReadOnly(), ref reader);

                /*reader.Read<TValue>();

                if (wrapper.TryGet(instance, out int temp))
                    wrapper.Set(ref instance, indices[temp]);
                else
                    wrapper.Invail(ref instance);*/
            }
        }

        public struct DeserializerFactory : IEntityDataFactory<Deserializer>
        {
            [ReadOnly]
            public SharedList<int>.Reader guidIndices;

            [ReadOnly]
            public EntityTypeHandle entityType;

            public ComponentTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                Deserializer deserializer;
                deserializer.guidIndices = guidIndices;
                deserializer.entityArray = chunk.GetNativeArray(entityType);
                deserializer.instances = chunk.GetNativeArray(ref instanceType);
                deserializer.wrapper = wrapper;

                return deserializer;
            }
        }

        private EntityDataDeserializationSystemCore __value;
        private EntityTypeHandle __entityType;
        private ComponentTypeHandle<TValue> __instanceType;
        private SharedList<int> __guidIndices;

        public EntityDataDeserializationSystemCore value => __value;

        public SharedList<int> guidIndices => __guidIndices;

        public static EntityDataDeserializationIndexComponentDataSystemCore<TValue, TWrapper> Create<T>(ref SystemState state) where T : unmanaged, ISystem, IEntityDataDeserializationIndexContainerSystem
        {
            EntityDataDeserializationIndexComponentDataSystemCore<TValue, TWrapper> result;
            result.__value = EntityDataDeserializationSystemCore.Create<TValue>(ref state);
            result.__entityType = state.GetEntityTypeHandle();
            result.__instanceType = state.GetComponentTypeHandle<TValue>();

            result.__guidIndices = state.WorldUnmanaged.GetExistingSystemUnmanaged<T>().guidIndices;

            return result;
        }

        public void Dispose()
        {
            __value.Dispose();
        }

        public void Update(ref TWrapper wrapper, ref SystemState state, bool isParallel)
        {
            ref var guidIndicesJobManager = ref __guidIndices.lookupJobManager;
            state.Dependency = JobHandle.CombineDependencies(guidIndicesJobManager.readOnlyJobHandle, state.Dependency);

            DeserializerFactory deserializerFactory;
            deserializerFactory.guidIndices = __guidIndices.reader;
            deserializerFactory.entityType = __entityType.UpdateAsRef(ref state);
            deserializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);
            deserializerFactory.wrapper = wrapper;

            __value.Update<Deserializer, DeserializerFactory>(ref deserializerFactory, ref state, isParallel);

            guidIndicesJobManager.AddReadOnlyDependency(state.Dependency);
        }
    }

    public struct EntityDataDeserializationIndexBufferSystemCore<TValue, TWrapper>
        where TValue : unmanaged, IBufferElementData
        where TWrapper : unmanaged, IEntityDataDeserializationIndexWrapper<TValue>
    {
        public struct Deserializer : IEntityDataDeserializer
        {
            [ReadOnly]
            public SharedList<int>.Reader guidIndices;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            public BufferAccessor<TValue> instances;

            public TWrapper wrapper;

            public bool Fallback(int index)
            {
                return false;
            }

            public void Deserialize(int index, ref EntityDataReader reader)
            {
                Entity entity = entityArray[index];
                var guidIndices = this.guidIndices.AsArray().AsReadOnly();

                int length = reader.Read<int>();
                /*var sources = reader.ReadArray<TValue>(length);
                var destinations = instances[index];
                destinations.CopyFrom(sources);*/
                var instances = this.instances[index];
                instances.ResizeUninitialized(length);

                //TValue instance;
                //int temp;
                for (int i = 0; i < length; ++i)
                {
                    instances[i] = wrapper.Deserialize(entity, guidIndices, ref reader);
                    /*instance = destinations[i];

                    if (wrapper.TryGet(instance, out temp))
                        wrapper.Set(ref instance, indices[temp]);
                    else
                        wrapper.Invail(ref instance);

                    destinations[i] = instance;*/
                }
            }
        }

        public struct DeserializerFactory : IEntityDataFactory<Deserializer>
        {
            [ReadOnly]
            public SharedList<int>.Reader guidIndices;

            public EntityTypeHandle entityType;

            public BufferTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                Deserializer deserializer;
                deserializer.guidIndices = guidIndices;
                deserializer.entityArray = chunk.GetNativeArray(entityType);
                deserializer.instances = chunk.GetBufferAccessor(ref instanceType);
                deserializer.wrapper = wrapper;

                return deserializer;
            }
        }

        private EntityDataDeserializationSystemCore __core;
        private EntityTypeHandle __entityType;
        private BufferTypeHandle<TValue> __instanceType;
        private SharedList<int> __guidIndices;

        public static EntityDataDeserializationIndexBufferSystemCore<TValue, TWrapper> Create<T>(ref SystemState state) where T : unmanaged, ISystem, IEntityDataDeserializationIndexContainerSystem
        {
            EntityDataDeserializationIndexBufferSystemCore<TValue, TWrapper> result;
            result.__core = EntityDataDeserializationSystemCore.Create<TValue>(ref state);
            result.__entityType = state.GetEntityTypeHandle();
            result.__instanceType = state.GetBufferTypeHandle<TValue>();
            result.__guidIndices = state.WorldUnmanaged.GetExistingSystemUnmanaged<T>().guidIndices;

            return result;
        }

        public void Dispose()
        {
            __core.Dispose();
        }

        public void Update(ref TWrapper wrapper, ref SystemState state, bool isParallel)
        {
            ref var guidIndicesJobManager = ref __guidIndices.lookupJobManager;
            state.Dependency = JobHandle.CombineDependencies(guidIndicesJobManager.readOnlyJobHandle, state.Dependency);

            DeserializerFactory deserializerFactory;
            deserializerFactory.guidIndices = __guidIndices.reader;
            deserializerFactory.entityType = __entityType.UpdateAsRef(ref state);
            deserializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);
            deserializerFactory.wrapper = wrapper;

            __core.Update<Deserializer, DeserializerFactory>(ref deserializerFactory, ref state, isParallel);

            guidIndicesJobManager.AddReadOnlyDependency(state.Dependency);
        }
    }
}