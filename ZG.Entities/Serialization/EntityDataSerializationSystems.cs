using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities.LowLevel.Unsafe;
using ZG;

[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationSystemCore.ComponentDataSerializer, EntityDataSerializationSystemCore.ComponentDataSerializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentSerialize<EntityDataSerializationSystemCore.BufferSerializer, EntityDataSerializationSystemCore.BufferSerializerFactory>))]

namespace ZG
{
    public struct EntityDataWriter
    {
        private UnsafeBuffer.Writer __value;
        public int position => __value.position;

        public EntityDataWriter(ref UnsafeBuffer buffer)
        {
            __value = new UnsafeBuffer.Writer(ref buffer);
        }

        public unsafe void Write(void* value, int length) => __value.Write(value, length);

        public void Write<T>(in T value) where T : struct => __value.Write(value);

        public void Write<T>(in NativeSlice<T> values) where T : struct => __value.Write(values);

        public void Write<T>(in NativeArray<T> values) where T : struct => __value.Write(values);

        public UnsafeBlock<T> WriteBlock<T>(in T value) where T : struct => __value.WriteBlock(value);
    }

    public struct EntityDataSerializationTypeHandle
    {
        public int value;

        public EntityQuery streamGroup;

        public ref EntityDataSerializationStream stream => ref streamGroup.GetSingletonRW<EntityDataSerializationStream>().ValueRW;
    }

    public struct EntityDataSerializable : IComponentData
    {
    }

    public struct EntityDataSerializationStream : IComponentData
    {
        public NativeBuffer.Writer writer;

        public LookupJobManager lookupJobManager;
    }

    public struct EntityDataSerializationBufferManager : IComponentData
    {
        public struct Buffer
        {
            internal int _index;
            internal UnsafeBuffer _value;

            public EntityDataWriter AsWriter() => new EntityDataWriter(ref _value);
        }

        private NativeArray<int> __count;

        private NativeList<UnsafeBuffer> __buffers;

        public int count => __count[0];

        public EntityDataSerializationBufferManager(in AllocatorManager.AllocatorHandle allocator)
        {
            __count = new NativeArray<int>(1, allocator.ToAllocator, NativeArrayOptions.UninitializedMemory);

            __buffers = new NativeList<UnsafeBuffer>(allocator);
        }

        public void Dispose()
        {
            __count.Dispose();

            foreach (var buffer in __buffers.AsArray())
            {
                if (buffer.isCreated)
                    buffer.Dispose();
            }

            __buffers.Dispose();
        }

        public JobHandle Resize(in EntityQuery group, in JobHandle inputDeps)
        {
            __count[0] = 0;

            return group.CalculateEntityCountAsync(__count, inputDeps);
        }

        public int ResizeIfNeed()
        {
            int count = __count[0];
            if (count > __buffers.Length)
                __buffers.Resize(count, NativeArrayOptions.ClearMemory);

            return count;
        }

        public void Reset(int index)
        {
            var buffer = __buffers[index];
            if (buffer.isCreated)
                buffer.Reset();
            else
                buffer = new UnsafeBuffer(UnsafeUtility.SizeOf<int>(), 1, Allocator.Persistent);

            __buffers[index] = buffer;
        }

        public void Write(int index, ref NativeBuffer.Writer writer)
        {
            writer.WriteBuffer(__buffers[index]);
        }

        public Buffer BeginWrite(int index)
        {
            Buffer result;
            result._index = index;
            result._value = __buffers[index];

            return result;
        }

        public void EndWrite(in Buffer buffer)
        {
            __buffers[buffer._index] = buffer._value;
        }
    }

    /*[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class EntityDataSerializeAttribute : Attribute
    {
        public Type type;
        public Type systemType;

        public EntityDataSerializeAttribute(Type type, Type systemType)
        {
            /*bool isVailType = false;
            Type genericType = null;
            var interfaceTypes = type.GetInterfaces();
            foreach (var interfaceType in interfaceTypes)
            {
                if (interfaceType == typeof(IEntityDataContainerSerializer))
                    isVailType = systemType.IsGenericTypeOf(typeof(EntityDataSerializationContainerSystem<>), out genericType);
                else if (interfaceType == typeof(IComponentData))
                    isVailType = systemType.IsGenericTypeOf(typeof(ComponentDataSerializationSystem<>), out genericType);
                else if(interfaceType == typeof(IBufferElementData))
                    isVailType = systemType.IsGenericTypeOf(typeof(BufferSerializationSystem<>), out genericType);

                if (isVailType)
                {
                    isVailType = genericType.GetGenericArguments()[0] == type;
                    if (isVailType)
                        break;
                }
            }

            if (!isVailType)
                throw new InvalidCastException($"Invail Entity Data Serialize : {type}, {systemType}!");/

            this.type = type;
            this.systemType = systemType;
        }

        public EntityDataSerializeAttribute(Type type)
        {
            systemType = null;

            var interfaceTypes = type.GetInterfaces();
            foreach (var interfaceType in interfaceTypes)
            {
                if (interfaceType == typeof(IComponentData))
                {
                    systemType = typeof(ComponentDataSerializationSystem<>).MakeGenericType(type);

                    break;
                }
                else if (interfaceType == typeof(IBufferElementData))
                {
                    systemType = typeof(BufferSerializationSystem<>).MakeGenericType(type);

                    break;
                }
            }

            if (systemType == null)
                throw new InvalidCastException();

            this.type = type;
        }
    }*/

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class EntityDataSerializationSystemAttribute : Attribute
    {
        public Type type;

        public EntityDataSerializationSystemAttribute(Type type)
        {
            this.type = type;
        }
    }

    [DisableAutoCreation]
    public partial class EntityDataSerializationManagedSystem : SystemBase
    {
        public virtual int version => 0;

        public virtual string GetTypeName(Type type)
        {
            return type.AssemblyQualifiedName;
        }

        protected override void OnUpdate()
        {
            throw new NotImplementedException();
        }
    }

    [BurstCompile, UpdateInGroup(typeof(ManagedSystemGroup))]
    public partial struct EntityDataSerializationSystemGroup : ISystem
    {
        private int __bufferHeaderLength;

        private NativeBuffer __buffer;
        private NativeHashMap<int, int> __typeHandles;

        private SystemGroup __systemGroup;

        public NativeBuffer.Writer writer => __buffer.writer;

        public NativeHashMap<int, int>.ReadOnly typeHandles => __typeHandles.AsReadOnly();

        /*public static Dictionary<Type, Type> CollectSystemTypes()
        {
            var result = new Dictionary<Type, Type>();

            IEnumerable<EntityDataSerializeAttribute> attributes;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                attributes = assembly.GetCustomAttributes<EntityDataSerializeAttribute>();
                if (attributes != null)
                {
                    foreach (var attribute in attributes)
                        result.Add(attribute.type, attribute.systemType);
                }
            }

            return result;
        }*/

        public byte[] ToBytes()
        {
            SystemAPI.GetSingletonRW<EntityDataSerializationStream>().ValueRW.lookupJobManager.CompleteReadOnlyDependency();

            return __buffer.ToBytes();
        }

        public void OnCreate(ref SystemState state)
        {
            var world = state.World;
            __systemGroup = world.GetOrCreateSystemGroup(typeof(EntityDataSerializationSystemGroup));

            __buffer = new NativeBuffer(Allocator.Persistent, 1);

            var writer = __buffer.writer;

            var managedSystem = world.GetOrCreateSystemManaged<EntityDataSerializationManagedSystem>();

            writer.Write(managedSystem.version);

            var systems = TypeManager.GetSystems();
            {
                int capacity = systems.Count;
                var types = new List<Type>(capacity);
                __typeHandles = new NativeHashMap<int, int>(capacity, Allocator.Persistent);

                //var worldUnmanaged = world.Unmanaged;
                EntityDataSerializationSystemAttribute attribute;
                foreach (var system in systems)
                {
                    attribute = system.GetCustomAttribute<EntityDataSerializationSystemAttribute>();
                    if (attribute == null || attribute.type == null)
                        continue;

                    types.Add(attribute.type);

                    __typeHandles[TypeManager.GetSystemTypeIndex(system)] = types.Count;
                }

                writer.Write(types.Count);
                foreach (var type in types)
                    writer.Write(managedSystem.GetTypeName(type));
            }

            __bufferHeaderLength = __buffer.position;

            EntityDataSerializationStream stream;
            stream.writer = writer;
            stream.lookupJobManager = default;

            state.EntityManager.AddComponentData(state.SystemHandle, stream);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __buffer.Dispose();

            __typeHandles.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.GetComponentDataRW<EntityDataSerializationStream>(state.SystemHandle).ValueRW.lookupJobManager.CompleteReadWriteDependency();

            __buffer.length = __bufferHeaderLength;

            var world = state.WorldUnmanaged;
            __systemGroup.Update(ref world);
        }
    }

    [BurstCompile, CreateAfter(typeof(EntityDataSerializationSystemGroup)), UpdateInGroup(typeof(EntityDataSerializationSystemGroup), OrderFirst = true)]
    public partial struct EntityDataSerializationInitializationSystem : ISystem
    {
        [BurstCompile]
        private struct InitBuffer : IJob
        {
            public NativeBuffer.Writer writer;

            public EntityDataSerializationBufferManager bufferManager;

            public SharedHashMap<Hash128, int>.Writer entityIndices;

            public void Execute()
            {
                int entityCount = bufferManager.ResizeIfNeed();

                writer.Write(entityCount);

                entityIndices.Clear();
                entityIndices.capacity = math.max(entityIndices.capacity, entityCount);
            }
        }

        [BurstCompile]
        private struct InitEntities : IJobChunk
        {
            [ReadOnly]
            public NativeArray<int> chunkBaseEntityIndices;

            [ReadOnly]
            public NativeArray<Hash128>.ReadOnly types;

            [ReadOnly]
            public ComponentTypeHandle<EntityDataIdentity> identityType;

            public NativeBuffer.Writer writer;

            public EntityDataSerializationBufferManager bufferManager;

            public SharedHashMap<Hash128, int>.Writer entityIndices;

            public SharedHashMap<int, int>.Writer typeIndices;

            public SharedList<Hash128>.Writer typeGuids;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var identities = chunk.GetNativeArray(ref identityType);

                bool result;
                int index, firstEntityIndex = chunkBaseEntityIndices[unfilteredChunkIndex];
                EntityDataIdentity identity;
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    identity = identities[i];

                    /*if (identity.guid.ToString() == "4b959ef3998812f82e760687c088b17e")
                        UnityEngine.Debug.Log($"ddd {identity.type}");*/

                    if (!typeIndices.TryGetValue(identity.type, out index))
                    {
                        index = typeGuids.length;
                        typeGuids.Add(types[identity.type]);

                        typeIndices[identity.type] = index;
                    }

                    writer.Write(index);
                    writer.Write(identity.guid);

                    index = firstEntityIndex + i;
                    result = entityIndices.TryAdd(identity.guid, index);
                    UnityEngine.Assertions.Assert.IsTrue(result, $"{identity}");

                    bufferManager.Reset(index);
                }
            }
        }

        private ComponentTypeHandle<EntityDataIdentity> __identityType;

        private EntityQuery __streamGroup;
        private EntityQuery __commonGroup;

        public EntityQuery group
        {
            get;

            private set;
        }

        public EntityDataSerializationBufferManager bufferManager
        {
            get;

            private set;
        }

        public SharedList<Hash128> typeGUIDs
        {
            get;

            private set;
        }

        public SharedHashMap<int, int> typeGUIDIndices
        {
            get;

            private set;
        }

        public SharedHashMap<Hash128, int> entityIndices
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                group = builder
                        .WithAll<EntityDataIdentity, EntityDataSerializable>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __streamGroup = builder
                        .WithAllRW<EntityDataSerializationStream>()
                        .WithOptions(EntityQueryOptions.IncludeSystems)
                        .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __commonGroup = builder
                        .WithAll<EntityDataCommon>()
                        .WithOptions(EntityQueryOptions.IncludeSystems)
                        .Build(ref state);

            __identityType = state.GetComponentTypeHandle<EntityDataIdentity>(true);

            bufferManager = new EntityDataSerializationBufferManager(Allocator.Persistent);

            typeGUIDs = new SharedList<Hash128>(Allocator.Persistent);

            typeGUIDIndices = new SharedHashMap<int, int>(Allocator.Persistent);

            entityIndices = new SharedHashMap<Hash128, int>(Allocator.Persistent);
        }

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            entityIndices.Dispose();

            typeGUIDIndices.Dispose();

            typeGUIDs.Dispose();

            bufferManager.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var stream = ref __streamGroup.GetSingletonRW<EntityDataSerializationStream>().ValueRW;
            var types = __commonGroup.GetSingleton<EntityDataCommon>().typesGUIDs;

            var group = this.group;

            var inputDeps = state.Dependency;

            var bufferManager = this.bufferManager;
            var jobHandle = bufferManager.Resize(group, inputDeps);

            var entityIndices = this.entityIndices;

            InitBuffer initBuffer;
            initBuffer.writer = stream.writer;
            initBuffer.bufferManager = bufferManager;
            initBuffer.entityIndices = entityIndices.writer;

            jobHandle = initBuffer.ScheduleByRef(JobHandle.CombineDependencies(
                jobHandle, 
                entityIndices.lookupJobManager.readWriteJobHandle, 
                stream.lookupJobManager.readWriteJobHandle));

            var typeGUIDs = this.typeGUIDs;
            typeGUIDs.lookupJobManager.CompleteReadWriteDependency();
            var typeGUIDsWriter = typeGUIDs.writer;
            typeGUIDsWriter.Clear();

            var typeGUIDIndices = this.typeGUIDIndices;
            typeGUIDIndices.lookupJobManager.CompleteReadWriteDependency();
            var typeGUIDIndicesWriter = typeGUIDIndices.writer;
            typeGUIDIndicesWriter.Clear();
            typeGUIDIndicesWriter.capacity = math.max(typeGUIDIndicesWriter.capacity, types.Length);

            var chunkBaseEntityIndices =
                group.CalculateBaseEntityIndexArrayAsync(state.WorldUpdateAllocator, inputDeps,
                    out var baseIndexJob);

            InitEntities initEntities;
            initEntities.chunkBaseEntityIndices = chunkBaseEntityIndices;
            initEntities.types = types;
            initEntities.identityType = __identityType.UpdateAsRef(ref state);
            initEntities.writer = stream.writer;
            initEntities.bufferManager = bufferManager;
            initEntities.entityIndices = entityIndices.writer;
            initEntities.typeIndices = typeGUIDIndicesWriter;
            initEntities.typeGuids = typeGUIDsWriter;
            jobHandle = initEntities.ScheduleByRef(group, JobHandle.CombineDependencies(baseIndexJob, jobHandle));

            typeGUIDs.lookupJobManager.readWriteJobHandle = jobHandle;

            typeGUIDIndices.lookupJobManager.readWriteJobHandle = jobHandle;

            entityIndices.lookupJobManager.readWriteJobHandle = jobHandle;

            stream.lookupJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile, 
        CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
        UpdateInGroup(typeof(EntityDataSerializationSystemGroup),OrderFirst = true), 
        UpdateAfter(typeof(EntityDataSerializationInitializationSystem))]
    public partial struct EntityDataSerializationTypeGUIDSystem : ISystem
    {
        [BurstCompile]
        private struct InitTypes : IJob
        {
            [ReadOnly]
            public SharedList<Hash128>.Reader typeGuids;

            public NativeBuffer.Writer writer;

            public void Execute()
            {
                writer.Write(typeGuids.length);
                writer.Write(typeGuids.AsArray());
            }
        }

        private SharedList<Hash128> __typeGUIDs;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __typeGUIDs = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataSerializationInitializationSystem>().typeGUIDs;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var stream = ref SystemAPI.GetSingletonRW<EntityDataSerializationStream>().ValueRW;

            InitTypes initTypes;
            initTypes.writer = stream.writer;
            initTypes.typeGuids = __typeGUIDs.reader;
            var jobHandle = initTypes.ScheduleByRef(JobHandle.CombineDependencies(
                state.Dependency,
                __typeGUIDs.lookupJobManager.readOnlyJobHandle, 
                stream.lookupJobManager.readWriteJobHandle));

            __typeGUIDs.lookupJobManager.AddReadOnlyDependency(jobHandle);

            stream.lookupJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile,
        CreateAfter(typeof(EntityDataSerializationInitializationSystem)),
        UpdateInGroup(typeof(EntityDataSerializationSystemGroup), OrderLast = true)]
    public partial struct EntityDataSerializationPresentationSystem : ISystem
    {
        [BurstCompile]
        private struct Combine : IJob
        {
            public NativeBuffer.Writer writer;

            [ReadOnly]
            public EntityDataSerializationBufferManager bufferManager;

            public void Execute()
            {
                //Container Type Handle
                writer.Write(0);

                int numBuffers = bufferManager.count;
                for (int i = 0; i < numBuffers; ++i)
                {
                    bufferManager.Write(i, ref writer);

                    //Component Type Handle;
                    writer.Write(0);
                }
            }
        }

        private EntityDataSerializationBufferManager __bufferManager;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __bufferManager = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataSerializationInitializationSystem>().bufferManager;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var stream = ref SystemAPI.GetSingletonRW<EntityDataSerializationStream>().ValueRW;

            Combine combine;
            combine.writer = stream.writer;
            combine.bufferManager = __bufferManager;
            var jobHandle = combine.ScheduleByRef(JobHandle.CombineDependencies(state.Dependency, stream.lookupJobManager.readWriteJobHandle));

            stream.lookupJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    public struct EntityDataSerializationSystemCore
    {
        public struct ComponentDataSerializer : IEntityDataSerializer
        {
            public int expectedTypeSize;

            [ReadOnly]
            public NativeArray<byte> values;

            public unsafe void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
            {
                writer.Write((byte*)values.GetUnsafeReadOnlyPtr() + expectedTypeSize * index, expectedTypeSize);
            }
        }

        public struct ComponentDataSerializerFactory : IEntityDataFactory<ComponentDataSerializer>
        {
            public int expectedTypeSize;

            [ReadOnly]
            public DynamicComponentTypeHandle type;

            public ComponentDataSerializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                ComponentDataSerializer serializer;
                serializer.expectedTypeSize = expectedTypeSize;
                serializer.values = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref type, expectedTypeSize);

                return serializer;
            }
        }

        public struct BufferSerializer : IEntityDataSerializer
        {
            [ReadOnly]
            public UnsafeUntypedBufferAccessor values;

            public unsafe void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer)
            {
                var values = this.values.GetUnsafePtrAndLength(index, out int length);
                writer.Write(length);
                writer.Write(values, this.values.ElementSize * length);
            }
        }

        public struct BufferSerializerFactory : IEntityDataFactory<BufferSerializer>
        {
            [ReadOnly]
            public DynamicComponentTypeHandle type;

            public BufferSerializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                BufferSerializer serializer;
                serializer.values = chunk.GetUntypedBufferAccessor(ref type);

                return serializer;
            }
        }

        private EntityDataSerializationTypeHandle __typeHandle;
        private EntityQuery __group;

        private ComponentTypeHandle<EntityDataIdentity> __identityType;

        private EntityDataSerializationBufferManager __bufferManager;

        private SharedHashMap<Hash128, int> __entityIndices;

        public static EntityDataSerializationSystemCore Create<T>(ref SystemState state) where T : struct
        {
            EntityDataSerializationSystemCore result;
            result.__typeHandle = EntityDataSerializationUtility.GetTypeHandle(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                result.__group = builder
                    .WithAll<EntityDataIdentity, EntityDataSerializable, T>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            result.__identityType = state.GetComponentTypeHandle<EntityDataIdentity>(true);

            ref var system = ref state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataSerializationInitializationSystem>();
            result.__bufferManager = system.bufferManager;
            result.__entityIndices = system.entityIndices;

            return result;
        }

        public void Dispose()
        {

        }

        public void Update<TSerializer, TSerializerFactory>(
            ref TSerializerFactory factory, 
            ref SystemState state)
            where TSerializer : struct, IEntityDataSerializer
            where TSerializerFactory : struct, IEntityDataFactory<TSerializer>
        {
            EntityDataComponentSerialize<TSerializer, TSerializerFactory> serialize;
            serialize.typeHandle = __typeHandle.value;
            serialize.identityType = __identityType.UpdateAsRef(ref state);
            serialize.entityIndices = __entityIndices.reader;
            serialize.bufferManager = __bufferManager;
            serialize.factory = factory;

            ref var entityIndicesJobManager = ref __entityIndices.lookupJobManager;
            ref var stream = ref __typeHandle.stream;
            var joHandle = serialize.ScheduleParallelByRef(__group, JobHandle.CombineDependencies(
                stream.lookupJobManager.readWriteJobHandle,
                entityIndicesJobManager.readOnlyJobHandle, 
                state.Dependency));

            entityIndicesJobManager.AddReadOnlyDependency(joHandle);

            stream.lookupJobManager.readWriteJobHandle = joHandle;

            state.Dependency = joHandle;
        }

        public void Update(ref SystemState state, in DynamicComponentTypeHandle type, int expectedTypeSize)
        {
            if (expectedTypeSize > 0)
            {
                ComponentDataSerializerFactory factory;
                factory.expectedTypeSize = expectedTypeSize;
                factory.type = type;

                Update<ComponentDataSerializer, ComponentDataSerializerFactory>(ref factory, ref state);
            }
            else
            {
                BufferSerializerFactory factory;
                factory.type = type;

                Update<BufferSerializer, BufferSerializerFactory>(ref factory, ref state);
            }
        }
    }

    public struct EntityDataSerializationSystemCoreEx
    {
        private int __expectedTypeSize;

        private EntityDataSerializationSystemCore __core;

        private DynamicComponentTypeHandle __type;

        public static EntityDataSerializationSystemCoreEx Create<T>(ref SystemState state) where T : struct
        {
            EntityDataSerializationSystemCoreEx result;

            result.__expectedTypeSize = TypeManager.GetTypeIndex<T>().IsBuffer ? 0 : TypeManager.GetTypeInfo<T>().ElementSize;
            result.__core = EntityDataSerializationSystemCore.Create<T>(ref state);

            result.__type = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<T>());

            return result;
        }

        public void Dispose()
        {
            __core.Dispose();
        }

        public void Update(ref SystemState state)
        {
            __core.Update(ref state, __type.UpdateAsRef(ref state), __expectedTypeSize);
        }
    }

    public static class EntityDataSerializationUtility
    {
        public static EntityDataSerializationTypeHandle GetTypeHandle(ref SystemState state)
        {
            EntityDataSerializationTypeHandle result;
            var typeHandles = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataSerializationSystemGroup>().typeHandles;
            result.value = typeHandles[state.GetSystemTypeIndex()];
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                result.streamGroup = builder
                    .WithAllRW<EntityDataSerializationStream>()
                    .WithOptions(EntityQueryOptions.IncludeSystems)
                    .Build(ref state);

            return result;
        }

        public static void Update<T>(in EntityDataSerializationTypeHandle typeHandle, ref T container, ref SystemState state) where T : struct, IEntityDataContainerSerializer
        {
            ref var stream = ref typeHandle.stream;

            EntityDataContainerSerialize<T> serialize;
            serialize.typeHandle = typeHandle.value;
            serialize.writer = stream.writer;
            serialize.container = container;

            var jobHandle = serialize.ScheduleByRef(JobHandle.CombineDependencies(state.Dependency, stream.lookupJobManager.readWriteJobHandle));

            stream.lookupJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }
}