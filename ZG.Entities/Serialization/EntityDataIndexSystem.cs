using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

namespace ZG
{
    public interface IEntityDataIndexReadOnlyWrapper<T>
    {
        bool TryGet(in T data, out int index);
    }

    public interface IEntityDataIndexReadWriteWrapper<T> : IEntityDataIndexReadOnlyWrapper<T>
    {
        void Invail(ref T data);

        void Set(ref T data, int index);
    }

    public abstract partial class EntityDataIndexContainerSerializationSystemBase : EntityDataSerializationContainerSystem<EntityDataIndexContainerSerializationSystemBase.Serializer>, IReadOnlyLookupJobManager
    {
        public struct Serializer : IEntityDataContainerSerializer
        {
            [ReadOnly]
            public NativeArray<Hash128> guids;

            public void Serialize(ref NativeBuffer.Writer writer)
            {
                writer.Write(guids);
            }
        }

        public abstract NativeParallelHashMap<int, int> indices
        {
            get;
        }

        #region LookupJob
        private LookupJobManager __lookupJobManager;

        public JobHandle readOnlyJobHandle
        {
            get => __lookupJobManager.readOnlyJobHandle;
        }

        public void CompleteReadOnlyDependency() => __lookupJobManager.CompleteReadOnlyDependency();

        public void AddReadOnlyDependency(in JobHandle inputDeps) => __lookupJobManager.AddReadOnlyDependency(inputDeps);
        #endregion

        protected override void OnUpdate()
        {
            __lookupJobManager.CompleteReadWriteDependency();

            JobHandle inputDeps = Dependency, jobHandle = indices.Clear(1, inputDeps);
            jobHandle = JobHandle.CombineDependencies(jobHandle, _GetResultGuids().Clear(inputDeps));

            jobHandle = _Update(jobHandle);

            __lookupJobManager.readWriteJobHandle = jobHandle;

            Dependency = jobHandle;

            base.OnUpdate();
        }

        protected override Serializer _Get()
        {
            Serializer serializer;
            serializer.guids = _GetResultGuids().AsDeferredJobArray();
            return serializer;
        }

        protected abstract NativeList<Hash128> _GetResultGuids();

        protected abstract JobHandle _Update(in JobHandle inputDeps);

        protected JobHandle _ScheduleComponent<TValue, TWrapper>(
               in EntityQuery group,
               in NativeArray<Hash128> guids,
               ref TWrapper wrapper,
               in JobHandle inputDeps)
            where TValue : unmanaged, IComponentData
            where TWrapper : unmanaged, IEntityDataIndexReadOnlyWrapper<TValue>
        {
            var indices = this.indices;
            var reusultGuids = _GetResultGuids();
            return EntityDataIndexComponentUtility<TValue, TWrapper>.Schedule(
                group,
                guids,
                GetComponentTypeHandle<TValue>(true),
                ref indices,
                ref reusultGuids,
                ref wrapper,
                inputDeps);
        }

        protected JobHandle _ScheduleBuffer<TValue, TWrapper>(
               in EntityQuery group,
               in NativeArray<Hash128> guids,
               ref TWrapper wrapper,
               in JobHandle inputDeps)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : unmanaged, IEntityDataIndexReadOnlyWrapper<TValue>
        {
            var indices = this.indices;
            var reusultGuids = _GetResultGuids();
            return EntityDataIndexBufferUtility<TValue, TWrapper>.Schedule(
                group,
                guids,
                GetBufferTypeHandle<TValue>(true),
                ref indices,
                ref reusultGuids,
                ref wrapper,
                inputDeps);
        }
    }

    public abstract partial class EntityDataIndexContainerSerializationSystem : EntityDataIndexContainerSerializationSystemBase
    {
        private NativeList<Hash128> __guids;
        private NativeParallelHashMap<int, int> __indices;

        public override NativeParallelHashMap<int, int> indices => __indices;

        protected override void OnCreate()
        {
            base.OnCreate();

            __guids = new NativeList<Hash128>(Allocator.Persistent);
            __indices = new NativeParallelHashMap<int, int>(1, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            __guids.Dispose();
            __indices.Dispose();

            base.OnDestroy();
        }

        protected override NativeList<Hash128> _GetResultGuids() => __guids;
    }

    public abstract partial class EntityDataIndexComponentContainerSerializationSystem<TValue, TWrapper> : EntityDataIndexContainerSerializationSystem
        where TValue : unmanaged, IComponentData 
        where TWrapper : unmanaged, IEntityDataIndexReadOnlyWrapper<TValue>
    {
        private EntityQuery __group;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<TValue>(),
                        ComponentType.ReadOnly<EntityDataIdentity>(),
                        ComponentType.ReadOnly<EntityDataSerializable>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
        }

        protected override JobHandle _Update(in JobHandle inputDeps)
        {
            return _ScheduleComponent<TValue, TWrapper>(__group, _GetGuids(), ref _GetWrapper(), inputDeps);
        }

        protected abstract NativeArray<Hash128> _GetGuids();

        protected abstract ref TWrapper _GetWrapper();
    }

    public abstract partial class EntityDataIndexBufferContainerSerializationSystem<TValue, TWrapper> : EntityDataIndexContainerSerializationSystem
        where TValue : unmanaged, IBufferElementData
        where TWrapper : unmanaged, IEntityDataIndexReadOnlyWrapper<TValue>
    {
        private EntityQuery __group;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<TValue>(),
                        ComponentType.ReadOnly<EntityDataIdentity>(),
                        ComponentType.ReadOnly<EntityDataSerializable>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
        }

        protected override JobHandle _Update(in JobHandle inputDeps)
        {
            return _ScheduleBuffer<TValue, TWrapper>(__group, _GetGuids(), ref _GetWrapper(), inputDeps);
        }

        protected abstract NativeArray<Hash128> _GetGuids();

        protected abstract ref TWrapper _GetWrapper();
    }

    public abstract partial class EntityDataIndexComponentSerializationSystem<TValue, TWrapper> : EntityDataSerializationComponentSystem<
        TValue,
        EntityDataIndexComponentSerializationSystem<TValue, TWrapper>.Serializer,
        EntityDataIndexComponentSerializationSystem<TValue, TWrapper>.SerializerFactory>
        where TValue : unmanaged, IComponentData
        where TWrapper : unmanaged, IEntityDataIndexReadWriteWrapper<TValue>
    {
        public struct Serializer : IEntityDataSerializer
        {
            [ReadOnly]
            public NativeParallelHashMap<int, int> indices;
            [ReadOnly]
            public NativeArray<TValue> instances;

            public TWrapper wrapper;

            public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
            {
                var instance = instances[index];

                if (wrapper.TryGet(instance, out int temp))
                    wrapper.Set(ref instance, indices[temp]);
                else
                    wrapper.Invail(ref instance);

                writer.Write(instance);
            }
        }

        public struct SerializerFactory : IEntityDataFactory<Serializer>
        {
            [ReadOnly]
            public NativeParallelHashMap<int, int> indices;
            [ReadOnly]
            public ComponentTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
            {
                Serializer serializer;
                serializer.indices = indices;
                serializer.instances = chunk.GetNativeArray(ref instanceType);
                serializer.wrapper = wrapper;

                return serializer;
            }
        }

        private EntityDataIndexContainerSerializationSystem __containerSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            __containerSystem = _GetOrCreateContainerSystem();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            __containerSystem.AddReadOnlyDependency(Dependency);
        }

        protected override SerializerFactory _Get(ref JobHandle jobHandle)
        {
            jobHandle = JobHandle.CombineDependencies(jobHandle, __containerSystem.readOnlyJobHandle);

            SerializerFactory serializerFactory;
            serializerFactory.indices = __containerSystem.indices;
            serializerFactory.instanceType = GetComponentTypeHandle<TValue>(true);
            serializerFactory.wrapper = _GetWrapper();

            return serializerFactory;
        }

        protected abstract TWrapper _GetWrapper();

        protected abstract EntityDataIndexContainerSerializationSystem _GetOrCreateContainerSystem();
    }

    public abstract partial class EntityDataIndexBufferSerializationSystem<TValue, TWrapper> : EntityDataSerializationComponentSystem<
        TValue,
        EntityDataIndexBufferSerializationSystem<TValue, TWrapper>.Serializer,
        EntityDataIndexBufferSerializationSystem<TValue, TWrapper>.SerializerFactory>
        where TValue : unmanaged, IBufferElementData
        where TWrapper : unmanaged, IEntityDataIndexReadWriteWrapper<TValue>
    {
        public struct Serializer : IEntityDataSerializer
        {
            [ReadOnly]
            public NativeParallelHashMap<int, int> indices;
            [ReadOnly]
            public BufferAccessor<TValue> instances;

            public TWrapper wrapper;

            public void Serialize(int index, in NativeParallelHashMap<Hash128, int> entityIndices, ref EntityDataWriter writer)
            {
                var instances = this.instances[index].ToNativeArray(Allocator.Temp);

                TValue instance;
                int length = instances.Length, temp;
                for (int i = 0; i < length; ++i)
                {
                    instance = instances[i];

                    if (wrapper.TryGet(instance, out temp))
                        wrapper.Set(ref instance, indices[temp]);
                    else
                        wrapper.Invail(ref instance);

                    instances[i] = instance;
                }

                writer.Write(length);
                writer.Write(instances);

                instances.Dispose();
            }
        }

        public struct SerializerFactory : IEntityDataFactory<Serializer>
        {
            [ReadOnly]
            public NativeParallelHashMap<int, int> indices;
            [ReadOnly]
            public BufferTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
            {
                Serializer serializer;
                serializer.indices = indices;
                serializer.instances = chunk.GetBufferAccessor(ref instanceType);
                serializer.wrapper = wrapper;

                return serializer;
            }
        }

        private EntityDataIndexContainerSerializationSystem __containerSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            __containerSystem = _GetOrCreateContainerSystem();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            __containerSystem.AddReadOnlyDependency(Dependency);
        }

        protected override SerializerFactory _Get(ref JobHandle jobHandle)
        {
            jobHandle = JobHandle.CombineDependencies(jobHandle, __containerSystem.readOnlyJobHandle);

            SerializerFactory serializerFactory;
            serializerFactory.indices = __containerSystem.indices;
            serializerFactory.instanceType = GetBufferTypeHandle<TValue>(true);
            serializerFactory.wrapper = _GetWrapper();

            return serializerFactory;
        }

        protected abstract TWrapper _GetWrapper();

        protected abstract EntityDataIndexContainerSerializationSystem _GetOrCreateContainerSystem();
    }

    public abstract partial class EntityDataIndexContainerDeserializationSystem : EntityDataDeserializationContainerSystem<EntityDataIndexContainerDeserializationSystem.Deserializer>, IReadOnlyLookupJobManager
    {
        public struct Deserializer : IEntityDataContainerDeserializer
        {
#if DEBUG
            public FixedString128Bytes typeName;
#endif

            [ReadOnly]
            public NativeArray<Hash128> guids;
            public NativeList<int> indices;

            public void Deserialize(in UnsafeBlock block)
            {
                var guids = block.AsArray<Hash128>();

                int length = guids.Length;
                indices.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    indices[i] = this.guids.IndexOf<Hash128, Hash128>(guids[i]);
#if DEBUG
                    if (indices[i] == -1)
                        UnityEngine.Debug.LogError($"{guids[i]} In Container {typeName} Deserialize Fail.");
#endif
                }
            }
        }

        private LookupJobManager __lookupJobManager;

        private NativeList<int> __indices;

        public NativeArray<int> indices => __indices.AsDeferredJobArray();

        #region LookupJob
        public JobHandle readOnlyJobHandle
        {
            get => __lookupJobManager.readOnlyJobHandle;
        }

        public void CompleteReadOnlyDependency() => __lookupJobManager.CompleteReadOnlyDependency();

        public void AddReadOnlyDependency(in JobHandle inputDeps) => __lookupJobManager.AddReadOnlyDependency(inputDeps);
        #endregion

        protected override void OnCreate()
        {
            base.OnCreate();

            __indices = new NativeList<int>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            __indices.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            __lookupJobManager.CompleteReadWriteDependency();

            base.OnUpdate();

            __lookupJobManager.readWriteJobHandle = Dependency;
        }

        protected override Deserializer _Create(ref JobHandle inputDeps)
        {
            Deserializer deserializer;
#if DEBUG
            deserializer.typeName = GetType().Name;
#endif
            deserializer.guids = _GetGuids();
            deserializer.indices = __indices;
            return deserializer;
        }

        protected abstract NativeArray<Hash128> _GetGuids();
    }

    public abstract partial class EntityDataIndexComponentDeserializationSystem<TValue, TWrapper> : EntityDataDeserializationComponentSystem<
        TValue,
        EntityDataIndexComponentDeserializationSystem<TValue, TWrapper>.Deserializer,
        EntityDataIndexComponentDeserializationSystem<TValue, TWrapper>.DeserializerFactory>
        where TValue : unmanaged, IComponentData
        where TWrapper : unmanaged, IEntityDataIndexReadWriteWrapper<TValue>
    {
        public struct Deserializer : IEntityDataDeserializer
        {
            [ReadOnly]
            public NativeArray<int> indices;

            public NativeArray<TValue> instances;

            public TWrapper wrapper;

            public void Deserialize(int index, ref EntityDataReader reader)
            {
                TValue instance = reader.Read<TValue>();

                if (wrapper.TryGet(instance, out int temp))
                    wrapper.Set(ref instance, indices[temp]);
                else
                    wrapper.Invail(ref instance);

                instances[index] = instance;
            }
        }

        public struct DeserializerFactory : IEntityDataFactory<Deserializer>
        {
            [ReadOnly]
            public NativeArray<int> indices;

            public ComponentTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                Deserializer deserializer;
                deserializer.indices = indices;
                deserializer.instances = chunk.GetNativeArray(ref instanceType);
                deserializer.wrapper = wrapper;

                return deserializer;
            }
        }

        private EntityDataIndexContainerDeserializationSystem __containerSystem;

        //public override bool isSingle => true;

        protected override void OnCreate()
        {
            base.OnCreate();

            __containerSystem = _GetOrCreateContainerSystem();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            __containerSystem.AddReadOnlyDependency(Dependency);
        }

        protected override DeserializerFactory _Get(ref JobHandle jobHandle)
        {
            jobHandle = JobHandle.CombineDependencies(jobHandle, __containerSystem.readOnlyJobHandle);

            DeserializerFactory deserializerFactory;
            deserializerFactory.indices = __containerSystem.indices;
            deserializerFactory.instanceType = GetComponentTypeHandle<TValue>();
            deserializerFactory.wrapper = _GetWrapper();

            return deserializerFactory;
        }

        protected abstract TWrapper _GetWrapper();

        protected abstract EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem();
    }

    public abstract partial class EntityDataIndexBufferDeserializationSystem<TValue, TWrapper> : EntityDataDeserializationComponentSystem<
        TValue,
        EntityDataIndexBufferDeserializationSystem<TValue, TWrapper>.Deserializer,
        EntityDataIndexBufferDeserializationSystem<TValue, TWrapper>.DeserializerFactory>
        where TValue : unmanaged, IBufferElementData
        where TWrapper : unmanaged, IEntityDataIndexReadWriteWrapper<TValue>
    {
        public struct Deserializer : IEntityDataDeserializer
        {
            [ReadOnly]
            public NativeArray<int> indices;

            public BufferAccessor<TValue> instances;

            public TWrapper wrapper;

            public void Deserialize(int index, ref EntityDataReader reader)
            {
                int length = reader.Read<int>();
                var sources = reader.ReadArray<TValue>(length);
                var destinations = instances[index];
                destinations.CopyFrom(sources);

                TValue instance;
                int temp;
                for (int i = 0; i < length; ++i)
                {
                    instance = destinations[i];

                    if (wrapper.TryGet(instance, out temp))
                        wrapper.Set(ref instance, indices[temp]);
                    else
                        wrapper.Invail(ref instance);

                    destinations[i] = instance;
                }
            }
        }

        public struct DeserializerFactory : IEntityDataFactory<Deserializer>
        {
            [ReadOnly]
            public NativeArray<int> indices;

            public BufferTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
            {
                Deserializer deserializer;
                deserializer.indices = indices;
                deserializer.instances = chunk.GetBufferAccessor(ref instanceType);
                deserializer.wrapper = wrapper;

                return deserializer;
            }
        }

        private EntityDataIndexContainerDeserializationSystem __containerSystem;

        //public override bool isSingle => true;

        protected override void OnCreate()
        {
            base.OnCreate();

            __containerSystem = _GetOrCreateContainerSystem();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            __containerSystem.AddReadOnlyDependency(Dependency);
        }

        protected override DeserializerFactory _Get(ref JobHandle jobHandle)
        {
            jobHandle = JobHandle.CombineDependencies(jobHandle, __containerSystem.readOnlyJobHandle);

            DeserializerFactory deserializerFactory;
            deserializerFactory.indices = __containerSystem.indices;
            deserializerFactory.instanceType = GetBufferTypeHandle<TValue>();
            deserializerFactory.wrapper = _GetWrapper();

            return deserializerFactory;
        }

        protected abstract TWrapper _GetWrapper();

        protected abstract EntityDataIndexContainerDeserializationSystem _GetOrCreateContainerSystem();
    }
}