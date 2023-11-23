using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using ZG;
using Unity.Mathematics;
using Unity.Entities.LowLevel.Unsafe;
using System.Diagnostics;

[assembly: RegisterGenericJobType(typeof(EntityDataContainerDeserialize<EntityDataDeserializationContainerSystemCore.Deserializer>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<EntityDataDeserializationSystemCore.ComponentDataDeserializer, EntityDataDeserializationSystemCore.ComponentDataDeserializerFactory>))]
[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<EntityDataDeserializationSystemCore.BufferDeserializer, EntityDataDeserializationSystemCore.BufferDeserializerFactory>))]

[assembly: RegisterGenericJobType(typeof(EntityDataComponentDeserialize<EntityDataDeserializationSystemCore.DeserializationCounter, EntityDataDeserializationSystemCore.DeserializationCounterFactory>))]

namespace ZG
{
    public struct EntityDataIdentityBlockKey : IEquatable<EntityDataIdentityBlockKey>
    {
        public int typeHandle;
        public int guidIndex;

        public bool Equals(EntityDataIdentityBlockKey other)
        {
            return typeHandle == other.typeHandle && guidIndex == other.guidIndex;
        }

        public override int GetHashCode()
        {
            return typeHandle ^ guidIndex;
        }
    }

    public struct EntityDataReader
    {
        private UnsafeBlock.Reader __value;

        public EntityDataReader(in UnsafeBlock block)
        {
            __value = block.reader;
        }

        public unsafe void* Read(int length) => __value.Read(length);

        public T Read<T>() where T : struct => __value.Read<T>();

        public NativeArray<T> ReadArray<T>(int length) where T : struct => __value.ReadArray<T>(length);
    }

    public struct EntityDataDeserializationInitializer : IEntityDataInitializer
    {
        private EntityDataIdentity __identity;

        public Hash128 guid => __identity.guid;

        public EntityDataDeserializationInitializer(in EntityDataIdentity identity)
        {
            __identity = identity;
        }

        public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
        {
            gameObjectEntity.AddComponentData(__identity);

            EntityDataDeserializable deserializable;
            deserializable.guid = __identity.guid;
            gameObjectEntity.AddComponentData(deserializable);
        }
    }

    public struct EntityDataDeserializationBuilder
    {
        public enum Status
        {
            None, 
            Created, 
            Init
        }

        private NativeReference<Status> __status;
        private NativeBuffer __buffer;
        private NativeHashMap<int, int> __typeHandles;

        public static Dictionary<Type, Dictionary<int, Type>> CollectSystemTypes()
        {
            var result = new Dictionary<Type, Dictionary<int, Type>>();

            Dictionary<int, Type> types;
            EntityDataDeserializationSystemAttribute attribute;
            foreach (var systemType in TypeManager.GetSystems())
            {
                attribute = systemType.GetCustomAttribute<EntityDataDeserializationSystemAttribute>();
                if (attribute != null)
                {
                    if (!result.TryGetValue(attribute.type, out types))
                    {
                        types = new Dictionary<int, Type>();

                        result[attribute.type] = types;
                    }

                    types.Add(attribute.version, systemType);
                }
            }

            return result;
        }

        public static Dictionary<string, Type> CollectTypeNames()
        {
            var result = new Dictionary<string, Type>();

            Type[] types;
            IEnumerable<EntityDataTypeNameAttribute> attributes;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                types = assembly.GetTypes();
                foreach (var type in types)
                {
                    attributes = type.GetCustomAttributes<EntityDataTypeNameAttribute>();
                    if (attributes != null)
                    {
                        foreach (var attribute in attributes)
                            result.Add(attribute.name, type);
                    }
                }
            }

            return result;
        }

        public Status status
        {
            get => __status.Value;

            private set => __status.Value = value;
        }

        public NativeBuffer.Reader reader => __buffer.reader;

        public NativeHashMap<int, int>.ReadOnly typeHandles => __typeHandles.AsReadOnly();

        public EntityDataDeserializationBuilder(in AllocatorManager.AllocatorHandle allocator)
        {
            __status = new NativeReference<Status>(allocator);

            __buffer = new NativeBuffer(allocator, 1);

            __typeHandles = new NativeHashMap<int, int>(1, allocator);

            status = Status.None;
        }

        public void Dispose()
        {
            __status.Dispose();

            __buffer.Dispose();

            __typeHandles.Dispose();
        }

        public bool Init()
        {
            if (status != Status.Created)
                return false;

            status = Status.Init;

            return true;
        }

        public bool Build(EntityDataDeserializationManagedSystem managedSystem, out int oldVersion, out int typeCount)
        {
            var bytes = managedSystem.GetBytes();

            if (bytes == null)
            {
                oldVersion = 0;
                typeCount = 0;

                return false;
            }

            __buffer.Reset();

            var writer = __buffer.writer;

            writer.Write(bytes);

            __buffer.position = 0;

            var reader = __buffer.reader;

            oldVersion = reader.Read<int>();

            typeCount = reader.Read<int>();

            __typeHandles.Clear();

            int version, systemVersion;
            string typeName;
            Match match;
            Type type, deserializationSystemType;
            Dictionary<int, Type> systemTypes;
            var systemTypeMap = CollectSystemTypes();
            var typeNameMap = CollectTypeNames();
            for (int i = 0; i < typeCount; ++i)
            {
                deserializationSystemType = null;

                typeName = reader.ReadString();

                match = Regex.Match(typeName, @"(^|\.)(\w+),");
                if (!match.Success || !typeNameMap.TryGetValue(match.Groups[2].Value, out type))
                    type = managedSystem.GetType(typeName);

                if (type != null && systemTypeMap.TryGetValue(type, out systemTypes))
                {
                    systemVersion = int.MaxValue;
                    foreach (var pair in systemTypes)
                    {
                        version = pair.Key;
                        if (version == oldVersion)
                        {
                            deserializationSystemType = pair.Value;

                            break;
                        }

                        if (version > oldVersion && version < systemVersion)
                        {
                            systemVersion = version;

                            deserializationSystemType = pair.Value;
                        }
                    }
                }

                if (deserializationSystemType == null)
                {
                    UnityEngine.Debug.LogError($"System Type {typeName}(Version {oldVersion}) Can not be found!");

                    continue;
                }

                __typeHandles[TypeManager.GetSystemTypeIndex(deserializationSystemType)] = i + 1;
            }

            status = Status.Created;

            return true;
        }
    }

    public struct EntityDataDeserializationStatusQuery
    {
        public struct Container
        {
            public readonly Entity StatusEntity;

            public ComponentLookup<EntityDataDeserializationStatus> states;

            public EntityDataDeserializationStatus.Value value
            {
                get => states[StatusEntity].value;

                set
                {
                    EntityDataDeserializationStatus status;
                    status.value = value;
                    states[StatusEntity] = status;
                }
            }

            public Container(ref EntityDataDeserializationStatusQuery query, ref SystemState state)
            {
                StatusEntity = query.Group.GetSingletonEntity();

                states = query.states.UpdateAsRef(ref state);
            }
        }

        public readonly EntityQuery Group;

        public ComponentLookup<EntityDataDeserializationStatus> states;

        public EntityDataDeserializationStatus.Value value => Group.GetSingleton<EntityDataDeserializationStatus>().value;

        public EntityDataDeserializationStatusQuery(ref SystemState state, bool isReadOnly)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                Group = (isReadOnly ? builder.WithAll<EntityDataDeserializationStatus>() : builder.WithAllRW<EntityDataDeserializationStatus>())
                    .WithOptions(EntityQueryOptions.IncludeSystems)
                .Build(ref state);

            states = state.GetComponentLookup<EntityDataDeserializationStatus>(isReadOnly);
        }

        public Container AsContainer(ref SystemState state)
        {
            return new Container(ref this, ref state);
        }
    }

    public struct EntityDataDeserializable : IComponentData, IEnableableComponent
    {
        public Hash128 guid;
    }

    public struct EntityDataDeserializationStatus : IComponentData
    {
        public enum Value
        {
            None,
            Created, 
            Init, 
            Complete
        }

        public Value value;
    }

    public struct EntityDataDeserializationTypeHandle
    {
        private int __systemTypeIndex;
        private NativeHashMap<int, int>.ReadOnly __typeHandles;

        public int value => __typeHandles.TryGetValue(__systemTypeIndex, out int typeHandle) ? typeHandle : -1;

        /*public EntityQuery streamGroup;

        public ref EntityDataDeserializationStream stream => ref streamGroup.GetSingletonRW<EntityDataDeserializationStream>().ValueRW;*/

        public EntityDataDeserializationTypeHandle(ref SystemState state)
        {
            __systemTypeIndex = state.GetSystemTypeIndex();
            __typeHandles = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataDeserializationSystemGroup>().typeHandles;
            //value = typeHandles[state.GetSystemTypeIndex()];

            /*using (var builder = new EntityQueryBuilder(Allocator.Temp))
                streamGroup = builder
                    .WithAllRW<EntityDataDeserializationStream>()
                    .WithOptions(EntityQueryOptions.IncludeSystems)
                    .Build(ref state);*/
        }
    }

    public struct EntityDataDeserializationStream : IComponentData
    {
        public NativeBuffer.Reader reader;

        public JobHandle jobHandle;
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false)]
    public class EntityDataDeserializationSystemAttribute : Attribute
    {
        public int version;
        public Type type;

        public EntityDataDeserializationSystemAttribute(Type type, int version)
        {
            this.type = type;
            this.version = version;
        }
    }

    [DisableAutoCreation]
    public partial class EntityDataDeserializationManagedSystem : SystemBase
    {
        internal EntityDataDeserializationBuilder _builder;

        public NativeArray<EntityDataIdentity>.ReadOnly identities
        {
            get
            {
                var identities = World.GetExistingSystemUnmanaged<EntityDataDeserializationInitializationSystem>().identities;

                identities.lookupJobManager.CompleteReadOnlyDependency();

                return identities.reader.AsArray().AsReadOnly();
            }
        }

        public bool Build(out int version, out int typeCount)
        {
            return _builder.Build(this, out version, out typeCount);
        }

        public virtual byte[] GetBytes()
        {
            return null;
        }

        public virtual Type GetType(string typeName)
        {
            return Type.GetType(typeName);
        }

        protected override void OnUpdate()
        {
            throw new NotImplementedException();
        }
    }

    [BurstCompile, UpdateInGroup(typeof(ManagedSystemGroup))]
    public struct EntityDataDeserializationSystemGroup : ISystem
    {
        private SystemGroup __systemGroup;

        private EntityDataDeserializationBuilder __builder;

        public NativeHashMap<int, int>.ReadOnly typeHandles => __builder.typeHandles;

        public NativeBuffer.Reader reader => __builder.reader;

        //[BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(EntityDataDeserializationSystemGroup));

            __builder = new EntityDataDeserializationBuilder(Allocator.Persistent);

            var enttiyManager = state.EntityManager;
            var systemHandle = state.SystemHandle;

            EntityDataDeserializationStream stream;
            stream.reader = __builder.reader;
            stream.jobHandle = default;
            enttiyManager.AddComponentData(systemHandle, stream);

            enttiyManager.AddComponent<EntityDataDeserializationStatus>(systemHandle);

            var system = state.World.GetOrCreateSystemManaged<EntityDataDeserializationManagedSystem>();
            system._builder = __builder;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __builder.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            var systemHandle = state.SystemHandle;

            bool isInit;
            switch(__builder.status)
            {
                case EntityDataDeserializationBuilder.Status.Created:
                    isInit = false;
                    break;
                case EntityDataDeserializationBuilder.Status.Init:
                    isInit = true;
                    break;
                default:
                    return;
            }

            if (isInit && 
                entityManager.GetComponentData<EntityDataDeserializationStatus>(systemHandle).value == EntityDataDeserializationStatus.Value.Complete)
                    return;

            EntityDataDeserializationStatus status;
            status.value = isInit ? EntityDataDeserializationStatus.Value.Init : EntityDataDeserializationStatus.Value.Created;
            entityManager.SetComponentData(systemHandle, status);

            ref var stream = ref entityManager.GetComponentDataRW<EntityDataDeserializationStream>(systemHandle).ValueRW;
            stream.jobHandle.Complete();
            stream.jobHandle = default;

            var world = state.WorldUnmanaged;
            __systemGroup.Update(ref world);

            __builder.Init();
        }
    }

    [BurstCompile, 
        //CreateAfter(typeof(EntityDataDeserializationSystemGroup)), 
        UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true)]
    public partial struct EntityDataDeserializationInitializationSystem : ISystem
    {
        [BurstCompile]
        private struct Command : IJob
        {
            //public Unity.Mathematics.Random random;

            [ReadOnly]
            public NativeArray<Hash128>.ReadOnly typeGUIDs;

            public NativeArray<int> entityCount;

            //public NativeList<Hash128> typeOutputs;

            public NativeList<EntityDataIdentity> identities;

            public NativeList<int> typeGUIDIndices;

            public SharedHashMap<Hash128, int>.Writer identityGUIDIndices;

            public NativeBuffer.Reader reader;

            public void Execute()
            {
                int entityCount = reader.Read<int>();

                this.entityCount[0] = entityCount;

                identities.Clear();
                identities.AddRange(reader.ReadArray<EntityDataIdentity>(entityCount));

                int typeCount = reader.Read<int>();
                //typeOutputs.Clear();
                //typeOutputs.AddRange(reader.ReadArray<Hash128>(typeCount));
                var typeGUIDs = reader.ReadArray<Hash128>(typeCount);

                typeGUIDIndices.ResizeUninitialized(typeCount);
                int i;
                for(i = 0; i < typeCount; ++i)
                    typeGUIDIndices[i] = this.typeGUIDs.IndexOf(typeGUIDs[i]);

                identityGUIDIndices.Clear();

                //int j, source, destination;
                for (i = 0; i < entityCount; ++i)
                {
                    ref var identity = ref identities.ElementAt(i);

                    /*for (j = 0; j < i; ++j)
                    {
                        if (identityGUIDs[j] == identity.guid)
                        {
                            UnityEngine.Debug.LogError($"The Same Guids: {identity.guid}");

                            identity.guid.Value = random.NextUInt4();

                            j = -1;
                        }
                    }

                    identityGUIDs[i] = identity.guid;*/

                    if (identityGUIDIndices.TryAdd(identity.guid, i))
                    {
                        /*source = identity.type;
                        if (!typeGUIDIndices.TryGetValue(source, out destination))
                        {
                            destination = typeInputs.IndexOf(typeOutputs[source]);

                            typeGUIDIndices[source] = destination;
                        }

                        identity.type = destination;*/

                        identity.type = typeGUIDIndices[identity.type];
                    }
                    else
                        UnityEngine.Debug.LogError($"The Same Guids: {identity.guid}");
                }

                //identities.Dispose();
            }
        }

        private NativeArray<int> __entityCount;

        /*public SharedList<Hash128> typeGUIDs
        {
            get;

            private set;
        }*/

        public NativeArray<int>.ReadOnly entityCount => __entityCount.AsReadOnly();

        public SharedList<EntityDataIdentity> identities
        {
            get;

            private set;
        }

        public SharedList<int> typeGUIDIndices
        {
            get;

            private set;
        }

        public SharedHashMap<Hash128, int> identityGUIDIndices
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __entityCount = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            identities = new SharedList<EntityDataIdentity>(Allocator.Persistent);

            //typeGUIDs = new SharedList<Hash128>(Allocator.Persistent);

            typeGUIDIndices = new SharedList<int>(Allocator.Persistent);

            identityGUIDIndices = new SharedHashMap<Hash128, int>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            //Destroy<EntityDataIdentity, EntityDataDeserializationCommander>(EntityCommandManager.QUEUE_PRESENT);

            //typeGUIDs.Dispose();

            __entityCount.Dispose();

            identities.Dispose();

            typeGUIDIndices.Dispose();

            identityGUIDIndices.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<EntityDataDeserializationStatus>().value == EntityDataDeserializationStatus.Value.Created)
            {
                ref var typeGUIDIndicesJobManager = ref typeGUIDIndices.lookupJobManager;
                ref var identityGUIDIndicesJobManager = ref identityGUIDIndices.lookupJobManager;
                ref var identitiesJobManager = ref identities.lookupJobManager;

                ref var stream = ref SystemAPI.GetSingletonRW<EntityDataDeserializationStream>().ValueRW;

                Command command;
                //command.random = RandomUtility.Create(state.WorldUnmanaged.Time.ElapsedTime);
                command.entityCount = __entityCount;
                command.typeGUIDs = SystemAPI.GetSingleton<EntityDataCommon>().typesGUIDs;
                //command.typeOutputs = typeGUIDs.writer;
                command.typeGUIDIndices = typeGUIDIndices.writer;
                command.identityGUIDIndices = identityGUIDIndices.writer;
                command.identities = identities.writer;
                command.reader = stream.reader;

                var jobHandle = JobHandle.CombineDependencies(
                    typeGUIDIndicesJobManager.readWriteJobHandle,
                    identityGUIDIndicesJobManager.readWriteJobHandle,
                    identitiesJobManager.readWriteJobHandle);
                jobHandle = command.ScheduleByRef(JobHandle.CombineDependencies(stream.jobHandle, jobHandle, state.Dependency));

                typeGUIDIndicesJobManager.readWriteJobHandle = jobHandle;
                identityGUIDIndicesJobManager.readWriteJobHandle = jobHandle;
                identitiesJobManager.readWriteJobHandle = jobHandle;
                stream.jobHandle = jobHandle;

                state.Dependency = jobHandle;
            }
        }
    }

    [BurstCompile, 
        CreateAfter(typeof(EntityDataDeserializationInitializationSystem)), 
        UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true), 
        UpdateAfter(typeof(EntityDataDeserializationInitializationSystem))]
    public partial struct EntityDataDeserializationPresentationSystem : ISystem
    {
        [BurstCompile]
        private struct Recapacity : IJob
        {
            public EntityDataDeserializationStatusQuery.Container status;

            [ReadOnly]
            public NativeArray<int>.ReadOnly entityCount;
            //public SharedList<Hash128>.Reader identityGUIDs;

            public SharedHashMap<int, Entity>.Writer identityEntities;

            public void Execute()
            {
                if(status.value == EntityDataDeserializationStatus.Value.Created)
                    identityEntities.Clear();

                identityEntities.capacity = math.max(identityEntities.capacity, entityCount[0]);
            }
        }

        private struct Build
        {
            [ReadOnly]
            //public SharedList<Hash128>.Reader identityGUIDs;
            public SharedHashMap<Hash128, int>.Reader identityGUIDIndices;
            [ReadOnly]
            public NativeArray<Entity> entityArray;
            [ReadOnly]
            public NativeArray<EntityDataDeserializable> identities;
            public SharedHashMap<int, Entity>.ParallelWriter identityEntities;

            public void Execute(int index)
            {
                bool result = identityGUIDIndices.TryGetValue(identities[index].guid, out int identityIndex);

                UnityEngine.Assertions.Assert.IsTrue(result, $"{entityArray[index]}");

                result = identityEntities.TryAdd(identityIndex, entityArray[index]);
                //UnityEngine.Debug.Log($"{identities[index].guid} : {entityArray[index]} : {NativeArrayExtensions.IndexOf<Hash128>(guids, identities[index].guid)} : {result}");// : {EntityComponentAssigner.hehe}");
                //if(!result)
                UnityEngine.Assertions.Assert.IsTrue(result, $"{entityArray[index]}");
            }
        }

        [BurstCompile]
        private struct BuildEx : IJobChunk
        {
            [ReadOnly]
            public SharedHashMap<Hash128, int>.Reader identityGUIDIndices;
            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public ComponentTypeHandle<EntityDataDeserializable> identityType;
            public SharedHashMap<int, Entity>.ParallelWriter identityEntities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Build build;
                build.identityGUIDIndices = identityGUIDIndices;
                build.entityArray = chunk.GetNativeArray(entityType);
                build.identities = chunk.GetNativeArray(ref identityType);
                build.identityEntities = identityEntities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    build.Execute(i);
            }
        }

        [BurstCompile]
        private struct Change : IJob
        {
            public EntityDataDeserializationStatusQuery.Container status;

            [ReadOnly]
            public NativeArray<int>.ReadOnly entityCount;
            [ReadOnly]
            public SharedHashMap<int, Entity>.Reader identityEntities;

            public void Execute()
            {
                if(status.value != EntityDataDeserializationStatus.Value.Created && identityEntities.Count() == entityCount[0])
                    status.value = EntityDataDeserializationStatus.Value.Complete;
            }
        }

        private EntityDataDeserializationStatusQuery __statusQuery;

        private EntityQuery __group;

        private EntityTypeHandle __entityType;
        private ComponentTypeHandle<EntityDataDeserializable> __identityType;
        private NativeArray<int>.ReadOnly __entityCount;
        private SharedHashMap<Hash128, int> __identityGUIDIndices;

        public SharedHashMap<int, Entity> identityEntities
        {
            get;

            private set;
        }

        public static EntityQuery CreateGroup(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                return builder
                        .WithAll<EntityDataDeserializable>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __group = CreateGroup(ref state);

            __statusQuery = new EntityDataDeserializationStatusQuery(ref state, false);

            __entityType = state.GetEntityTypeHandle();
            __identityType = state.GetComponentTypeHandle<EntityDataDeserializable>(true);

            ref var system = ref state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataDeserializationInitializationSystem>();
            __entityCount = system.entityCount;
            __identityGUIDIndices = system.identityGUIDIndices;

            identityEntities = new SharedHashMap<int, Entity>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            identityEntities.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var identityEntities = this.identityEntities;
            ref var identityEntitiesManager = ref identityEntities.lookupJobManager;
            ref var identityGUIDIndicesJobManager = ref __identityGUIDIndices.lookupJobManager;

            var status = __statusQuery.AsContainer(ref state);

            Recapacity recapacity;
            recapacity.status = status;
            recapacity.entityCount = __entityCount;
            recapacity.identityEntities = identityEntities.writer;
            var jobHandle = recapacity.ScheduleByRef(JobHandle.CombineDependencies(identityEntitiesManager.readWriteJobHandle, identityGUIDIndicesJobManager.readOnlyJobHandle, state.Dependency));

            BuildEx build;
            build.identityGUIDIndices = __identityGUIDIndices.reader;
            build.entityType = __entityType.UpdateAsRef(ref state);
            build.identityType = __identityType.UpdateAsRef(ref state);
            build.identityEntities = identityEntities.parallelWriter;
            jobHandle = build.ScheduleParallelByRef(__group, jobHandle);

            Change change;
            change.status = __statusQuery.AsContainer(ref state);
            change.entityCount = __entityCount;
            change.identityEntities = identityEntities.reader;
            jobHandle = change.ScheduleByRef(jobHandle);

            identityGUIDIndicesJobManager.AddReadOnlyDependency(jobHandle);

            identityEntitiesManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile,
        UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true),
        UpdateAfter(typeof(EntityDataDeserializationInitializationSystem))]
    public partial struct EntityDataDeserializationContainerSystem : ISystem
    {
        [BurstCompile]
        private struct Build : IJob
        {
            public NativeBuffer.Reader reader;

            public SharedHashMap<int, UnsafeBlock>.Writer blocks;

            public void Execute()
            {
                blocks.Clear();

                int typeHandle = reader.Read<int>(), length;
                while (typeHandle != 0)
                {
                    length = reader.Read<int>();
                    blocks[typeHandle] = reader.ReadBlock(length);

                    typeHandle = reader.Read<int>();
                }
            }
        }

        internal SharedHashMap<int, UnsafeBlock> _blocks
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _blocks = new SharedHashMap<int, UnsafeBlock>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _blocks.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.GetSingleton<EntityDataDeserializationStatus>().value == EntityDataDeserializationStatus.Value.Created)
            {
                ref var stream = ref SystemAPI.GetSingletonRW<EntityDataDeserializationStream>().ValueRW;
                var blocks = _blocks;
                ref var blockJobManager = ref blocks.lookupJobManager;

                Build build;
                build.reader = stream.reader;
                build.blocks = blocks.writer;
                var jobHandle = build.ScheduleByRef(JobHandle.CombineDependencies(stream.jobHandle, blockJobManager.readWriteJobHandle, state.Dependency));

                blockJobManager.readWriteJobHandle = jobHandle;

                stream.jobHandle = jobHandle;

                state.Dependency = jobHandle;
            }
        }
    }

    [BurstCompile,
        CreateAfter(typeof(EntityDataDeserializationInitializationSystem)), 
        UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true),
        //UpdateBefore(typeof(EntityDataDeserializationCommandSystem)),
        UpdateAfter(typeof(EntityDataDeserializationContainerSystem))]
    public partial struct EntityDataDeserializationComponentSystem : ISystem
    {
        [BurstCompile]
        private struct Build : IJob
        {
            [ReadOnly]
            public EntityDataDeserializationStatusQuery.Container status;

            //[ReadOnly]
            //public SharedList<Hash128>.Reader identityGUIDs;
            [ReadOnly]
            public NativeArray<int>.ReadOnly entityCount;

            public NativeBuffer.Reader reader;

            public SharedHashMap<EntityDataIdentityBlockKey, UnsafeBlock>.Writer identityBlocks;

            public void Execute()
            {
                if (status.value != EntityDataDeserializationStatus.Value.Created)
                    return;

                identityBlocks.Clear();

                bool result;
                int entityCount = this.entityCount[0], typeHandle;
                EntityDataIdentityBlockKey key;
                //Hash128 guid;
                //UnsafeHashMap<int, UnsafeBlock> identityBlocks;
                for (int i = 0; i < entityCount; ++i)
                {
                    //guid = identityGUIDs[i];

                    typeHandle = reader.Read<int>();
                    while (typeHandle != 0)
                    {
                        key.typeHandle = typeHandle;
                        key.guidIndex = i;

                        result = identityBlocks.TryAdd(key, reader.ReadBlock(reader.Read<int>()));

                        UnityEngine.Assertions.Assert.IsTrue(result);

                        //this.identityBlocks[typeHandle] = identityBlocks;

                        typeHandle = reader.Read<int>();
                    }
                }

                UnityEngine.Assertions.Assert.AreEqual(reader.length, reader.position);
            }
        }

        private EntityDataDeserializationStatusQuery __statusQuery;
        private NativeArray<int>.ReadOnly __entityCount;

        internal SharedHashMap<EntityDataIdentityBlockKey, UnsafeBlock> _identityBlocks
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __statusQuery = new EntityDataDeserializationStatusQuery(ref state, true);

            __entityCount = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataDeserializationInitializationSystem>().entityCount;

            _identityBlocks = new SharedHashMap<EntityDataIdentityBlockKey, UnsafeBlock>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _identityBlocks.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var stream = ref SystemAPI.GetSingletonRW<EntityDataDeserializationStream>().ValueRW;

            //var identityBlocks = _identityBlocks;
            ref var identityBlocksJobManager = ref _identityBlocks.lookupJobManager;
            //ref var identityGUIDsJobManager = ref __identityGUIDs.lookupJobManager;

            Build build;
            build.status = __statusQuery.AsContainer(ref state);
            build.entityCount = __entityCount;
            build.reader = stream.reader;
            build.identityBlocks = _identityBlocks.writer;

            var jobHandle = build.ScheduleByRef(JobHandle.CombineDependencies(identityBlocksJobManager.readWriteJobHandle, /*identityGUIDsJobManager.readOnlyJobHandle, */state.Dependency));

            //identityGUIDsJobManager.AddReadOnlyDependency(jobHandle);

            identityBlocksJobManager.readWriteJobHandle = jobHandle;

            stream.jobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile, 
        //CreateAfter(typeof(EntityDataDeserializationPresentationSystem)), 
        UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderLast = true)]
    public partial struct EntityDataDeserializationStructChangeSystem : ISystem
    {
        [BurstCompile]
        private struct Clear : IJobChunk
        {
            public ComponentTypeHandle<EntityDataDeserializable> deserializableType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    chunk.SetComponentEnabled(ref deserializableType, i, false);
            }
        }

        private EntityQuery __group;

        private ComponentTypeHandle<EntityDataDeserializable> __deserializableType;

        public EntityComponentAssigner assigner
        {
            get;

            private set;
        }

        public EntityCommandStructChangeManager manager
        {
            get;

            private set;
        }

        public EntityAddDataPool addDataCommander => new EntityAddDataPool(manager.addComponentPool, assigner);

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __group = EntityDataDeserializationPresentationSystem.CreateGroup(ref state);

            __deserializableType = state.GetComponentTypeHandle<EntityDataDeserializable>();

            assigner = new EntityComponentAssigner(Allocator.Persistent);

            manager = new EntityCommandStructChangeManager(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            assigner.Dispose();

            manager.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            manager.Playback(ref state);

            assigner.Playback(ref state);

            Clear clear;
            clear.deserializableType = __deserializableType.UpdateAsRef(ref state);

            state.Dependency = clear.ScheduleParallelByRef(__group, state.Dependency);
        }
    }

    public struct EntityDataDeserializationContainerSystemCore
    {
        public struct Deserializer : IEntityDataContainerDeserializer
        {
            [ReadOnly]
            public NativeArray<Hash128>.ReadOnly typeGUIDs;
            public NativeList<int> typeGUIDIndices;

            public void Deserialize(in UnsafeBlock block)
            {
                var typeGUIDs = block.AsArray<Hash128>();

                int length = typeGUIDs.Length;
                typeGUIDIndices.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    typeGUIDIndices[i] = this.typeGUIDs.IndexOf(typeGUIDs[i]);

#if DEBUG
                    /*if (typeGuids[i].ToString() == "4b959ef3998812f82e760687c088b17e")
                        UnityEngine.Debug.Log($"{types[i]}");*/

                    if (typeGUIDIndices[i] == -1)
                        UnityEngine.Debug.LogError($"{typeGUIDs[i]} Deserialize Fail.");
#endif
                }
            }
        }

        private EntityDataDeserializationTypeHandle __typeHandle;

        private EntityDataDeserializationStatusQuery __statusQuery;

        private SharedHashMap<int, UnsafeBlock> __blocks;

        public EntityDataDeserializationContainerSystemCore(ref SystemState state)
        {
            __typeHandle = new EntityDataDeserializationTypeHandle(ref state);

            __statusQuery = new EntityDataDeserializationStatusQuery(ref state, true);

            __blocks = state.WorldUnmanaged.GetExistingSystemUnmanaged<EntityDataDeserializationContainerSystem>()._blocks;
        }

        public void Dispose()
        {

        }

        public void Update<T>(ref T container, ref SystemState state) where T : struct, IEntityDataContainerDeserializer
        {
            ref var blocksJobManager = ref __blocks.lookupJobManager;

            EntityDataContainerDeserialize<T> deserialize;
            deserialize.typeHandle = __typeHandle.value;
            deserialize.status = __statusQuery.AsContainer(ref state);
            deserialize.blocks = __blocks.reader;
            deserialize.container = container;

            var jobHandle = deserialize.ScheduleByRef(JobHandle.CombineDependencies(blocksJobManager.readOnlyJobHandle, state.Dependency));

            blocksJobManager.AddReadOnlyDependency(jobHandle);

            state.Dependency = jobHandle;
        }

        public void Update(in NativeArray<Hash128>.ReadOnly typeGUIDs, ref SharedList<int> typeGUIDIndices, ref SystemState state)
        {
            Deserializer deserializer;
            deserializer.typeGUIDs = typeGUIDs;
            deserializer.typeGUIDIndices = typeGUIDIndices.writer;

            ref var typeGUIDIndicesJobManager = ref typeGUIDIndices.lookupJobManager;
            state.Dependency = JobHandle.CombineDependencies(typeGUIDIndicesJobManager.readWriteJobHandle, state.Dependency);

            Update(ref deserializer, ref state);

            var jobHandle = state.Dependency;

            typeGUIDIndicesJobManager.readWriteJobHandle = jobHandle;
        }
    }

    public struct EntityDataDeserializationSystemCore
    {
        /*[BurstCompile]
        private struct GetMap : IJob
        {
            public int typeHandle;
            [ReadOnly]
            public SharedHashMap<int, UnsafeHashMap<int, UnsafeBlock>>.Reader input;
            public NativeReference<UnsafeHashMap<int, UnsafeBlock>> output;

            public void Execute()
            {
                if (!input.TryGetValue(typeHandle, out var result))
                    result = default;

                output.Value = result;
            }
        }*/

        public struct DeserializationCounter : IEntityDataDeserializer
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<int> count;

            public bool Fallback(int index)
            {
                return false;
            }

            public void Deserialize(int index, ref EntityDataReader reader)
            {
                count.Add(0, reader.Read<int>());
            }
        }

        public struct DeserializationCounterFactory : IEntityDataFactory<DeserializationCounter>
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<int> count;

            public DeserializationCounter Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                DeserializationCounter counter;
                counter.count = count;

                return counter;
            }
        }

        public struct ComponentDataDeserializer : IEntityDataDeserializer
        {
            public bool isComponentEnabled;
            public int expectedTypeSize;
            public NativeArray<byte> values;
            public DynamicComponentTypeHandle type;
            public ArchetypeChunk chunk;

            public bool Fallback(int index)
            {
                if (isComponentEnabled)
                {
                    chunk.SetComponentEnabled(ref type, index, false);

                    return true;
                }

                return false;
            }

            public unsafe void Deserialize(int index, ref EntityDataReader reader)
            {
                UnityEngine.Assertions.Assert.IsTrue(index >= 0 && index < values.Length);

                UnsafeUtility.MemCpy((byte*)values.GetUnsafePtr() + expectedTypeSize * index, reader.Read(expectedTypeSize), expectedTypeSize);

                if (isComponentEnabled)
                    chunk.SetComponentEnabled(ref type, index, true);
            }
        }

        public struct ComponentDataDeserializerFactory : IEntityDataFactory<ComponentDataDeserializer>
        {
            public bool isComponentEnabled;
            public int expectedTypeSize;
            public DynamicComponentTypeHandle type;

            public ComponentDataDeserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                ComponentDataDeserializer deserializer;
                deserializer.isComponentEnabled = isComponentEnabled;
                deserializer.expectedTypeSize = expectedTypeSize;
                deserializer.values = chunk.GetDynamicComponentDataArrayReinterpret<byte>(ref type, expectedTypeSize);
                deserializer.type = type;
                deserializer.chunk = chunk;

                return deserializer;
            }
        }

        public struct BufferDeserializer : IEntityDataDeserializer
        {
            public bool isComponentEnabled;
            public UnsafeUntypedBufferAccessor values;
            public DynamicComponentTypeHandle type;
            public ArchetypeChunk chunk;

            public bool Fallback(int index)
            {
                if (isComponentEnabled)
                {
                    chunk.SetComponentEnabled(ref type, index, false);

                    return true;
                }

                return false;
            }

            public unsafe void Deserialize(int index, ref EntityDataReader reader)
            {
                int length = reader.Read<int>();
                values.ResizeUninitialized(index, length);
                int size = length * values.ElementSize;
                UnsafeUtility.MemCpy(values.GetUnsafePtr(index), reader.Read(size), size);

                if (isComponentEnabled)
                    chunk.SetComponentEnabled(ref type, index, true);
            }
        }

        public struct BufferDeserializerFactory : IEntityDataFactory<BufferDeserializer>
        {
            public bool isComponentEnabled;
            public DynamicComponentTypeHandle type;

            public BufferDeserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                BufferDeserializer deserializer;
                deserializer.isComponentEnabled = isComponentEnabled;
                deserializer.values = chunk.GetUntypedBufferAccessor(ref type);
                deserializer.type = type;
                deserializer.chunk = chunk;

                return deserializer;
            }
        }

        private EntityQuery __group;

        private EntityDataDeserializationTypeHandle __typeHandle;

        private ComponentTypeHandle<EntityDataDeserializable> __deserializableType;
        private ComponentTypeHandle<EntityDataIdentity> __identityType;

        private SharedHashMap<Hash128, int> __identityGUIDIndices;
        private SharedHashMap<EntityDataIdentityBlockKey, UnsafeBlock> __identityBlocks;
        //private NativeReference<UnsafeHashMap<int, UnsafeBlock>> __blocks;

        public EntityQuery group => __group;

        public static EntityDataDeserializationSystemCore Create<T>(ref SystemState state) where T : struct
        {
            return new EntityDataDeserializationSystemCore(ComponentType.ReadWrite<T>(), ref state);
        }

        public unsafe EntityDataDeserializationSystemCore(in ComponentType componentType, ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
            using (var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
            {
                ComponentType.ReadOnly<EntityDataIdentity>(),
                ComponentType.ReadOnly<EntityDataDeserializable>(),
                componentType,
            })
                __group = builder
                        .WithAll(componentTypes.GetUnsafePtr(), componentTypes.Length)
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IgnoreComponentEnabledState)
                        .Build(ref state);

            __typeHandle = new EntityDataDeserializationTypeHandle(ref state);

            __deserializableType = state.GetComponentTypeHandle<EntityDataDeserializable>(true);

            __identityType = state.GetComponentTypeHandle<EntityDataIdentity>(true);

            var world = state.WorldUnmanaged;
            __identityGUIDIndices = world.GetExistingSystemUnmanaged<EntityDataDeserializationInitializationSystem>().identityGUIDIndices;

            __identityBlocks = world.GetExistingSystemUnmanaged<EntityDataDeserializationComponentSystem>()._identityBlocks;
        }

        public void Dispose()
        {
            //__blocks.Dispose();
        }

        /*public JobHandle BeginUpdate(in JobHandle inputDeps)
        {
            ref var identityBlocksJobManager = ref __identityBlocks.lookupJobManager;

            GetMap getMap;
            getMap.typeHandle = __typeHandle.value;
            getMap.input = __identityBlocks.reader;
            getMap.output = __blocks;
            var jobHandle = getMap.ScheduleByRef(JobHandle.CombineDependencies(inputDeps, identityBlocksJobManager.readOnlyJobHandle));

            identityBlocksJobManager.AddReadOnlyDependency(inputDeps);

            return jobHandle;
        }*/

        public void Update<TDeserializer, TDeserializerFactory>(in EntityQuery group, ref TDeserializerFactory factory, ref SystemState state, bool isParallel)
            where TDeserializer : struct, IEntityDataDeserializer
            where TDeserializerFactory : struct, IEntityDataFactory<TDeserializer>
        {
            //不能这样，否则GameDataItem出错
            /*if (group.IsEmptyIgnoreFilter)
                return;*/

            ref var identityGUIDIndicesJobManager = ref __identityGUIDIndices.lookupJobManager;
            ref var identityBlocksJobManager = ref __identityBlocks.lookupJobManager;

            EntityDataComponentDeserialize<TDeserializer, TDeserializerFactory> deserialize;
            deserialize.typeHandle = __typeHandle.value;

            if (deserialize.typeHandle == -1)
                return;

#if DEBUG
            deserialize.systemTypeName = TypeManager.GetSystemName(state.GetSystemTypeIndex());
#endif
            deserialize.deserializableType = __deserializableType.UpdateAsRef(ref state);
            deserialize.identityType = __identityType.UpdateAsRef(ref state);
            deserialize.identityGUIDIndices = __identityGUIDIndices.reader;
            deserialize.identityBlocks = __identityBlocks.reader;
            deserialize.factory = factory;

            var jobHandle = JobHandle.CombineDependencies(identityGUIDIndicesJobManager.readOnlyJobHandle, identityBlocksJobManager.readOnlyJobHandle, state.Dependency);

            jobHandle = isParallel ? deserialize.ScheduleParallelByRef(group, jobHandle) : deserialize.ScheduleByRef(group, jobHandle);

            identityGUIDIndicesJobManager.AddReadOnlyDependency(jobHandle);

            identityBlocksJobManager.AddReadOnlyDependency(jobHandle);

            state.Dependency = jobHandle;
        }

        public void Update<TDeserializer, TDeserializerFactory>(ref TDeserializerFactory factory, ref SystemState state, bool isParallel)
            where TDeserializer : struct, IEntityDataDeserializer
            where TDeserializerFactory : struct, IEntityDataFactory<TDeserializer>
        {
            Update<TDeserializer, TDeserializerFactory>(__group, ref factory, ref state, isParallel);
        }

        public void Update(ref SystemState state, ref DynamicComponentTypeHandle type, int expectedTypeSize, bool isComponentEnabled)
        {
            if (expectedTypeSize > 0)
            {
                ComponentDataDeserializerFactory factory;
                factory.isComponentEnabled = isComponentEnabled;
                factory.expectedTypeSize = expectedTypeSize;
                factory.type = type;

                Update<ComponentDataDeserializer, ComponentDataDeserializerFactory>(ref factory, ref state, true);
            }
            else
            {
                BufferDeserializerFactory factory;
                factory.isComponentEnabled = isComponentEnabled;
                factory.type = type;

                Update<BufferDeserializer, BufferDeserializerFactory>(ref factory, ref state, true);
            }
        }

        public void Count(ref NativeArray<int> count, ref SystemState state)
        {
            DeserializationCounterFactory factory;
            factory.count = count;

            Update<DeserializationCounter, DeserializationCounterFactory>(ref factory, ref state, true);
        }
    }

    public struct EntityDataDeserializationSystemCoreEx
    {
        private bool __isComponentEnabled;
        private int __expectedTypeSize;

        private DynamicComponentTypeHandle __type;

        private EntityDataDeserializationSystemCore __value;

        public EntityDataDeserializationSystemCore value => __value;

        public static EntityDataDeserializationSystemCoreEx Create<T>(ref SystemState state) where T : struct
        {
            EntityDataDeserializationSystemCoreEx result;

            var type = ComponentType.ReadWrite<T>();
            result.__isComponentEnabled = type.IsEnableable;
            result.__expectedTypeSize = type.IsBuffer ? 0 : TypeManager.GetTypeInfo<T>().ElementSize;
            result.__type = state.GetDynamicComponentTypeHandle(type);

            result.__value = EntityDataDeserializationSystemCore.Create<T>(ref state);

            return result;
        }

        public void Dispose()
        {
            __value.Dispose();
        }

        public void Update(ref SystemState state)
        {
            var type = __type.UpdateAsRef(ref state);
            __value.Update(ref state, ref type, __expectedTypeSize, __isComponentEnabled);
        }
    }

    public struct EntityDataDeserializationContainerSystemCoreEx
    {
        private EntityDataDeserializationContainerSystemCore __core;

        public SharedList<int> typeGUIDIndices
        {
            get;

            private set;
        }

        public EntityDataDeserializationContainerSystemCoreEx(ref SystemState state)
        {
            __core = new EntityDataDeserializationContainerSystemCore(ref state);

            typeGUIDIndices = new SharedList<int>(Allocator.Persistent);
        }

        public void Dispose()
        {
            __core.Dispose();

            typeGUIDIndices.Dispose();
        }

        public void Update(in NativeArray<Hash128>.ReadOnly typeGUIDs, ref SystemState state)
        {
            var typeGUIDIndices = this.typeGUIDIndices;

            __core.Update(typeGUIDs, ref typeGUIDIndices, ref state);
        }
    }
}