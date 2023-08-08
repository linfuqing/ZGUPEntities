using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ZG;

[assembly: RegisterGenericJobType(typeof(EntityDataContainerSerialize<EntityDataSerializationIndexContainerSystemCore.Serializer>))]

namespace ZG
{
    public interface IEntityDataSerializationIndexWrapper<T>// : IEntityDataIndexReadOnlyWrapper<T>
    {
        void Serialize(ref EntityDataWriter writer, in T data, in SharedHashMap<int, int>.Reader guidIndices/*int guidIndex*/);
    }

    public interface IEntityDataSerializationIndexContainerSystem
    {
        public SharedHashMap<int, int> guidIndices { get; }
    }

    public struct EntityDataSerializationIndexContainerSystemCore
    {
        [BurstCompile]
        private struct ClearGUIDs : IJob
        {
            public SharedHashMap<int, int>.Writer guidIndices;

            public SharedList<Hash128>.Writer guids;

            public void Execute()
            {
                guidIndices.Clear();
                guids.Clear();
            }
        }

        public struct Serializer : IEntityDataContainerSerializer
        {
            [ReadOnly]
            public SharedList<Hash128>.Reader guids;

            public void Serialize(ref NativeBuffer.Writer writer)
            {
                writer.Write(guids.AsArray());
            }
        }

        private EntityDataSerializationTypeHandle __typeHandle;

        public SharedHashMap<int, int> guidIndices
        {
            get;

            private set;
        }

        public SharedList<Hash128> guids
        {
            get;

            private set;
        }

        public EntityDataSerializationIndexContainerSystemCore(ref SystemState state)
        {
            __typeHandle = new EntityDataSerializationTypeHandle(ref state);

            guidIndices = new SharedHashMap<int, int>(Allocator.Persistent);

            guids = new SharedList<Hash128>(Allocator.Persistent);
        }

        public void Dispose()
        {
            guidIndices.Dispose();

            guids.Dispose();
        }

        public JobHandle Clear(in JobHandle inputDeps)
        {
            var guidIndices = this.guidIndices;
            var guids = this.guids;

            guidIndices.lookupJobManager.CompleteReadWriteDependency();
            guids.lookupJobManager.CompleteReadWriteDependency();

            ClearGUIDs clearGUIDs;
            clearGUIDs.guidIndices = guidIndices.writer;
            clearGUIDs.guids = guids.writer;
            return clearGUIDs.ScheduleByRef(inputDeps);
        }

        public JobHandle Update<TValue, TWrapper>(
               in EntityQuery group,
               in NativeArray<Hash128>.ReadOnly guids,
               in ComponentTypeHandle<TValue> type, 
               ref TWrapper wrapper,
               in JobHandle inputDeps)
            where TValue : unmanaged, IComponentData
            where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
        {
            var guidIndices = this.guidIndices;
            var guidIndicesWriter = guidIndices.writer;
            var guidResults = this.guids;
            var guidWriter = guidResults.writer;

            var jobHandle = EntityDataIndexComponentUtility<TValue, TWrapper>.Schedule(
                group,
                guids,
                type,
                ref guidIndicesWriter,
                ref guidWriter,
                ref wrapper,
                inputDeps);

            guidIndices.lookupJobManager.readWriteJobHandle = jobHandle;
            guidResults.lookupJobManager.readWriteJobHandle = jobHandle;

            return jobHandle;
        }

        public JobHandle Update<TValue, TWrapper>(
               in EntityQuery group,
               in NativeArray<Hash128>.ReadOnly guids,
               in BufferTypeHandle<TValue> type,
               ref TWrapper wrapper,
               in JobHandle inputDeps)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
        {
            var guidIndices = this.guidIndices;
            var guidIndicesWriter = guidIndices.writer;
            var guidResults = this.guids;
            var guidWriter = guidResults.writer;

            var jobHandle = EntityDataIndexBufferUtility<TValue, TWrapper>.Schedule(
                group,
                guids,
                type,
                ref guidIndicesWriter,
                ref guidWriter,
                ref wrapper,
                inputDeps);

            guidIndices.lookupJobManager.readWriteJobHandle = jobHandle;
            guidResults.lookupJobManager.readWriteJobHandle = jobHandle;

            return jobHandle;
        }

        public void Command(ref SystemState state)
        {
            var guids = this.guids;

            Serializer serializer;
            serializer.guids = guids.reader;
            __typeHandle.Update(ref serializer, ref state);

            guids.lookupJobManager.readWriteJobHandle = state.Dependency;
        }

        public void Update<TValue, TWrapper>(
               in EntityQuery group,
               in NativeArray<Hash128>.ReadOnly guids,
               in ComponentTypeHandle<TValue> type,
               ref TWrapper wrapper,
               ref SystemState state)
            where TValue : unmanaged, IComponentData
            where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
        {
            var jobHandle = Clear(state.Dependency);

            jobHandle = Update(group, guids, type, ref wrapper, jobHandle);

            state.Dependency = jobHandle;

            Command(ref state);
            /*var guidResults = this.guids;

            Serializer serializer;
            serializer.guids = guidResults.reader;
            EntityDataSerializationUtility.Update(__typeHandle, ref serializer, ref state);

            guidResults.lookupJobManager.readWriteJobHandle = state.Dependency;*/
        }

        public void Update<TValue, TWrapper>(
               in EntityQuery group,
               in NativeArray<Hash128>.ReadOnly guids,
               in BufferTypeHandle<TValue> type,
               ref TWrapper wrapper,
               ref SystemState state)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
        {
            var jobHandle = Clear(state.Dependency);

            jobHandle = Update(group, guids, type, ref wrapper, jobHandle);

            state.Dependency = jobHandle;

            Command(ref state);
        }
    }

    public struct EntityDataSerializationIndexComponentDataSystemCore<TValue, TWrapper>
            where TValue : unmanaged, IComponentData
            where TWrapper : struct, IEntityDataSerializationIndexWrapper<TValue>
    {
        public struct Serializer : IEntityDataSerializer
        {
            [ReadOnly]
            public SharedHashMap<int, int>.Reader guidIndices;
            [ReadOnly]
            public NativeArray<TValue> instances;

            public TWrapper wrapper;

            public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
            {
                var instance = instances[index];

                /*if (wrapper.TryGet(instance, out int guidIndex))
                    wrapper.Set(ref instance, guidIndices[guidIndex]);
                else
                    wrapper.Invail(ref instance);

                writer.Write(instance);*/

                wrapper.Serialize(ref writer, instance, guidIndices);// wrapper.TryGet(instance, out int guidIndex) ? guidIndices[guidIndex] : -1);
            }
        }

        public struct SerializerFactory : IEntityDataFactory<Serializer>
        {
            [ReadOnly]
            public SharedHashMap<int, int>.Reader guidIndices;
            [ReadOnly]
            public ComponentTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
            {
                Serializer serializer;
                serializer.guidIndices = guidIndices;
                serializer.instances = chunk.GetNativeArray(ref instanceType);
                serializer.wrapper = wrapper;

                return serializer;
            }
        }

        private ComponentTypeHandle<TValue> __instanceType;
        private SharedHashMap<int, int> __guidIndices;
        private EntityDataSerializationSystemCore __core;

        public static EntityDataSerializationIndexComponentDataSystemCore<TValue, TWrapper> Create<T>(ref SystemState state) where T : unmanaged, ISystem, IEntityDataSerializationIndexContainerSystem
        {
            EntityDataSerializationIndexComponentDataSystemCore<TValue, TWrapper> result;
            result.__instanceType = state.GetComponentTypeHandle<TValue>(true);
            result.__guidIndices = state.WorldUnmanaged.GetExistingSystemUnmanaged<T>().guidIndices;
            result.__core = EntityDataSerializationSystemCore.Create<TValue>(ref state);

            return result;
        }

        public void Dispose()
        {
            __core.Dispose();
        }

        public void Update(ref TWrapper wrapper, ref SystemState state)
        {
            ref var guidIndicesJobManager = ref __guidIndices.lookupJobManager;
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, guidIndicesJobManager.readOnlyJobHandle);

            SerializerFactory serializerFactory;
            serializerFactory.guidIndices = __guidIndices.reader;
            serializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);
            serializerFactory.wrapper = wrapper;

            __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);

            guidIndicesJobManager.AddReadOnlyDependency(state.Dependency);
        }
    }

    public struct EntityDataSerializationIndexBufferSystemCore<TValue, TWrapper>
            where TValue : unmanaged, IBufferElementData
            where TWrapper : struct, IEntityDataSerializationIndexWrapper<TValue>
    {
        public struct Serializer : IEntityDataSerializer
        {
            [ReadOnly]
            public SharedHashMap<int, int>.Reader guidIndices;
            [ReadOnly]
            public BufferAccessor<TValue> instances;

            public TWrapper wrapper;

            public void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
            {
                var instances = this.instances[index];//.ToNativeArray(Allocator.Temp);
                int length = instances.Length;
                writer.Write(length);

                TValue instance;
                //int temp;
                for (int i = 0; i < length; ++i)
                {
                    instance = instances[i];

                    /*if (wrapper.TryGet(instance, out temp))
                        wrapper.Set(ref instance, guidIndices[temp]);
                    else
                        wrapper.Invail(ref instance);

                    instances[i] = instance;*/

                    wrapper.Serialize(ref writer, instance, guidIndices);// wrapper.TryGet(instance, out int guidIndex) ? guidIndices[guidIndex] : -1);
                }

                //writer.Write(length);
                //writer.Write(instances);

                //instances.Dispose();
            }
        }

        public struct SerializerFactory : IEntityDataFactory<Serializer>
        {
            [ReadOnly]
            public SharedHashMap<int, int>.Reader guidIndices;
            [ReadOnly]
            public BufferTypeHandle<TValue> instanceType;

            public TWrapper wrapper;

            public Serializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
            {
                Serializer serializer;
                serializer.guidIndices = guidIndices;
                serializer.instances = chunk.GetBufferAccessor(ref instanceType);
                serializer.wrapper = wrapper;

                return serializer;
            }
        }

        private BufferTypeHandle<TValue> __instanceType;
        private SharedHashMap<int, int> __guidIndices;
        private EntityDataSerializationSystemCore __core;

        public static EntityDataSerializationIndexBufferSystemCore<TValue, TWrapper> Create<T>(ref SystemState state) where T : unmanaged, ISystem, IEntityDataSerializationIndexContainerSystem
        {
            EntityDataSerializationIndexBufferSystemCore<TValue, TWrapper> result;
            result.__instanceType = state.GetBufferTypeHandle<TValue>(true);
            result.__guidIndices = state.WorldUnmanaged.GetExistingSystemUnmanaged<T>().guidIndices;
            result.__core = EntityDataSerializationSystemCore.Create<TValue>(ref state);

            return result;
        }

        public void Dispose()
        {
            __core.Dispose();
        }

        public void Update(ref TWrapper wrapper, ref SystemState state)
        {
            ref var guidIndicesJobManager = ref __guidIndices.lookupJobManager;
            state.Dependency = JobHandle.CombineDependencies(state.Dependency, guidIndicesJobManager.readOnlyJobHandle);

            SerializerFactory serializerFactory;
            serializerFactory.guidIndices = __guidIndices.reader;
            serializerFactory.instanceType = __instanceType.UpdateAsRef(ref state);
            serializerFactory.wrapper = wrapper;

            __core.Update<Serializer, SerializerFactory>(ref serializerFactory, ref state);

            guidIndicesJobManager.AddReadOnlyDependency(state.Dependency);
        }
    }

    public struct EntityDataSerializationIndexContainerComponentDataSystemCore<T> where T : unmanaged, IComponentData
    {
        private EntityQuery __group;
        private ComponentTypeHandle<T> __type;
        private EntityDataSerializationIndexContainerSystemCore __core;

        public SharedHashMap<int, int> guidIndices => __core.guidIndices;

        public SharedList<Hash128> guids => __core.guids;

        public EntityDataSerializationIndexContainerComponentDataSystemCore(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<T, EntityDataIdentity, EntityDataSerializable>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __type = state.GetComponentTypeHandle<T>(true);

            __core = new EntityDataSerializationIndexContainerSystemCore(ref state);
        }

        public void Dispose()
        {
            __core.Dispose();
        }

        public void Update<U>(in NativeArray<Hash128>.ReadOnly guids, ref U wrapper, ref SystemState state) where U : struct, IEntityDataIndexReadOnlyWrapper<T>
        {
            __core.Update(__group, guids, __type.UpdateAsRef(ref state), ref wrapper, ref state);
        }
    }

    public struct EntityDataSerializationIndexContainerBufferSystemCore<T> where T : unmanaged, IBufferElementData
    {
        private EntityQuery __group;
        private BufferTypeHandle<T> __type;
        private EntityDataSerializationIndexContainerSystemCore __core;

        public SharedHashMap<int, int> guidIndices => __core.guidIndices;

        public SharedList<Hash128> guids => __core.guids;

        public EntityDataSerializationIndexContainerBufferSystemCore(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<T, EntityDataIdentity, EntityDataSerializable>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __type = state.GetBufferTypeHandle<T>(true);

            __core = new EntityDataSerializationIndexContainerSystemCore(ref state);
        }

        public void Dispose()
        {
            __core.Dispose();
        }

        public void Update<U>(in NativeArray<Hash128>.ReadOnly guids, ref U wrapper, ref SystemState state) where U : struct, IEntityDataIndexReadOnlyWrapper<T>
        {
            __core.Update(__group, guids, __type.UpdateAsRef(ref state), ref wrapper, ref state);
        }
    }
}
