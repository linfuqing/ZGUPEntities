using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using ZG;

[assembly: RegisterGenericJobType(typeof(ClearList<Hash128>))]
[assembly: RegisterGenericJobType(typeof(ClearHashMap<Hash128, int>))]

namespace ZG
{
    public interface IEntityDataInitializer
    {
        void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity;
    }

    public struct EntityDataInitializer : IEntityDataInitializer
    {
        private EntityDataIdentity __identity;

        public Hash128 guid => __identity.guid;

        public EntityDataInitializer(in EntityDataIdentity identity)
        {
            __identity = identity;
        }

        public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
        {
            gameObjectEntity.AddComponentData(__identity);
        }
    }

    public struct EntityDataIdentity : IComponentData
    {
        public int type;

        public Hash128 guid;

        public override string ToString()
        {
            return $"EntityDataIdentity({type} : {guid})";
        }
    }

    public struct EntityDataCommon : IComponentData
    {
        public NativeArray<Hash128>.ReadOnly typesGUIDs;

        public static EntityQuery GetEntityQuery(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                return builder
                        .WithAll<EntityDataCommon>()
                        .WithOptions(EntityQueryOptions.IncludeSystems)
                        .Build(ref state);
        }
    }

    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public class EntityDataTypeNameAttribute : Attribute
    {
        public string name;

        public EntityDataTypeNameAttribute(string name)
        {
            this.name = name;
        }
    }

    [BurstCompile, RequireMatchingQueriesForUpdate, UpdateInGroup(typeof(PresentationSystemGroup))/*, UpdateBefore(typeof(EndFrameEntityCommandSystemGroup))*/, UpdateAfter(typeof(CallbackSystem))]
    public partial struct EntityDataSystem : ISystem
    {
        [BurstCompile]
        private struct DidChange : IJobChunk
        {
            public uint lastSystemVersion;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> result;

            [ReadOnly]
            public ComponentTypeHandle<EntityDataIdentity> identityType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.DidChange(ref identityType, lastSystemVersion))
                    return;

                result[0] = 1;
            }
        }

        [BurstCompile]
        private struct Clear : IJob
        {
            public int entityCount;

            [ReadOnly]
            public NativeArray<int> result;

            public SharedHashMap<Hash128, Entity>.Writer guidEntities;

            public void Execute()
            {
                if (result[0] == 0)
                    return;

                guidEntities.Clear();

                guidEntities.capacity = Unity.Mathematics.math.max(guidEntities.capacity, entityCount);
            }
        }

        private struct Rebuild
        {
            [ReadOnly]
            public NativeArray<Entity> entities;

            [ReadOnly]
            public NativeArray<EntityDataIdentity> identities;

            public SharedHashMap<Hash128, Entity>.ParallelWriter guidEntities;

            public void Execute(int index)
            {
                bool result = guidEntities.TryAdd(identities[index].guid, entities[index]);

                UnityEngine.Assertions.Assert.IsTrue(result);
            }
        }

        [BurstCompile]
        private struct RebuildEx : IJobChunk
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> result;

            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<EntityDataIdentity> identityType;

            public SharedHashMap<Hash128, Entity>.ParallelWriter guidEntities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (result[0] == 0)
                    return;

                Rebuild rebuild;
                rebuild.entities = chunk.GetNativeArray(entityType);
                rebuild.identities = chunk.GetNativeArray(ref identityType);
                rebuild.guidEntities = guidEntities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    rebuild.Execute(i);
            }
        }

        private int __entityCount;
        private EntityQuery __group;
        private EntityTypeHandle __entityType;
        private ComponentTypeHandle<EntityDataIdentity> __identityType;

        public SharedHashMap<Hash128, Entity> guidEntities
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<EntityDataIdentity>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            __entityType = state.GetEntityTypeHandle();

            __identityType = state.GetComponentTypeHandle<EntityDataIdentity>(true);

            guidEntities = new SharedHashMap<Hash128, Entity>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            guidEntities.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var identityType = __identityType.UpdateAsRef(ref state);

            var result = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            JobHandle jobHandle;

            int entityCount = __group.CalculateEntityCount();
            if (entityCount == __entityCount)
            {
                DidChange didChange;
                didChange.lastSystemVersion = state.LastSystemVersion;
                didChange.result = result;
                didChange.identityType = identityType;
                jobHandle = didChange.ScheduleParallelByRef(__group, state.Dependency);
            }
            else
            {
                __entityCount = entityCount;

                jobHandle = state.Dependency;
            }

            var guidEntities = this.guidEntities;

            Clear clear;
            clear.entityCount = entityCount;
            clear.result = result;
            clear.guidEntities = guidEntities.writer;

            ref var guidEntitiesJobManager = ref guidEntities.lookupJobManager;

            jobHandle = clear.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, guidEntitiesJobManager.readWriteJobHandle));

            RebuildEx rebuild;
            rebuild.result = result;
            rebuild.entityType = __entityType.UpdateAsRef(ref state);
            rebuild.identityType = __identityType;
            rebuild.guidEntities = guidEntities.parallelWriter;

            jobHandle = rebuild.ScheduleParallelByRef(__group, jobHandle);

            guidEntitiesJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    /*public abstract class EntityDataCommander : IEntityCommander<EntityDataIdentity>
    {
        public struct Initializer : IEntityDataInitializer
        {
            private EntityDataIdentity __identity;

            public Hash128 guid => __identity.guid;

            public Initializer(in EntityDataIdentity identity)
            {
                __identity = identity;
            }

            public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
            {
                gameObjectEntity.AddComponentData(__identity);
            }
        }

        public bool isDone { get;  private set; }

        public abstract bool Create<T>(in EntityDataIdentity identity, in T initializer) where T : IEntityDataInitializer;

        public virtual bool Create(in EntityDataIdentity identity) => Create(identity, new Initializer(identity));

        public virtual void Execute(
            EntityCommandPool<EntityDataIdentity>.Context context, 
            EntityCommandSystem system, 
            ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
            in JobHandle inputDeps)
        {
            isDone = false;

            while (context.TryDequeue(out var command))
            {
                dependency.CompleteAll(inputDeps);

                if (!Create(command))
                    return;
            }

            isDone = true;
        }

        void IDisposable.Dispose()
        {

        }
    }*/

    #region Deserialization

    /*[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class EntityDataDeserializeAttribute : Attribute
    {
        public Type type;
        public Type systemType;
        public int version;

        public EntityDataDeserializeAttribute(Type type, Type systemType, int version = 0)
        {
            /*bool isVailType = false;
            Type genericType = null;
            var interfaceTypes = type.GetInterfaces();
            foreach (var interfaceType in interfaceTypes)
            {
                if (interfaceType == typeof(IEntityDataContainer))
                    isVailType = systemType.IsGenericTypeOf(typeof(EntityDataDeserializationContainerSystem<>), out genericType);
                else if (interfaceType == typeof(IComponentData))
                    isVailType = systemType.IsGenericTypeOf(typeof(ComponentDataDeserializationSystem<>), out genericType);
                else if (interfaceType == typeof(IBufferElementData))
                    isVailType = systemType.IsGenericTypeOf(typeof(BufferDeserializationSystem<>), out genericType);

                if (isVailType)
                {
                    isVailType = genericType.GetGenericArguments()[0] == type;
                    if(isVailType)
                        break;
                }
            }

            if (!isVailType)
                throw new InvalidCastException($"Invail Entity Data Deserialize : {type}, {systemType}!");/

            this.version = version;
            this.type = type;
            this.systemType = systemType;
        }

        public EntityDataDeserializeAttribute(Type type, int version = 0)
        {
            systemType = null;

            var interfaceTypes = type.GetInterfaces();
            foreach (var interfaceType in interfaceTypes)
            {
                if (interfaceType == typeof(IComponentData))
                {
                    systemType = typeof(ComponentDataDeserializationSystem<>).MakeGenericType(type);

                    break;
                }
                else if (interfaceType == typeof(IBufferElementData))
                {
                    systemType = typeof(BufferDeserializationSystem<>).MakeGenericType(type);

                    break;
                }
            }

            if (systemType == null)
                throw new InvalidCastException();

            this.type = type;
            this.version = version;
        }
    }

    public abstract class EntityDataDeserializationCommander : EntityDataCommander
    {
        public new struct Initializer : IEntityDataInitializer
        {
            private EntityDataCommander.Initializer __origin;

            public Initializer(EntityDataCommander.Initializer origin)
            {
                __origin = origin;
            }


            public void Invoke<T>(ref T gameObjectEntity) where T : IGameObjectEntity
            {
                __origin.Invoke(ref gameObjectEntity);

                EntityDataDeserializable deserializable;
                deserializable.guid = __origin.guid;
                gameObjectEntity.AddComponentData(deserializable);
            }
        }

        public override bool Create(in EntityDataIdentity identity)
        {
            return Create(identity, new Initializer(new EntityDataCommander.Initializer(identity)));
        }
    }*/

    /*public abstract class EntityDataDeserializationSystemGroup : ComponentSystemGroup, ILookupJobManager
    {
        private NativeBuffer __buffer;
        private LookupJobManager __lookupJobManager;

        public bool isInit
        {
            get;

            private set;
        }

        public int version
        {
            get;

            private set;
        }

        public int typeCount
        {
            get;

            private set;
        }

        public NativeBuffer.Reader reader => __buffer.reader;

        public abstract NativeArray<Hash128>.ReadOnly types { get; }

        public EntityDataDeserializationInitializationSystem initializationSystem
        {
            get;

            private set;
        }

        public EntityDataDeserializationContainerSystem containerSystem
        {
            get;

            private set;
        }

        public EntityDataDeserializationComponentSystem componentSystem
        {
            get;

            private set;
        }

        public EntityDataDeserializationCommandSystem commandSystem
        {
            get;

            private set;
        }

        public EntityDataDeserializationPresentationSystem presentationSystem
        {
            get;

            private set;
        }

        public EntityDataDeserializationClearSystem clearSystem
        {
            get;

            private set;
        }

        #region LookupJob
        public JobHandle readOnlyJobHandle
        {
            get => __lookupJobManager.readOnlyJobHandle;
        }

        public JobHandle readWriteJobHandle
        {
            get => __lookupJobManager.readWriteJobHandle;

            set => __lookupJobManager.readWriteJobHandle = value;
        }

        public void CompleteReadWriteDependency() => __lookupJobManager.CompleteReadWriteDependency();

        public void CompleteReadOnlyDependency() => __lookupJobManager.CompleteReadOnlyDependency();

        public void AddReadOnlyDependency(in JobHandle inputDeps) => __lookupJobManager.AddReadOnlyDependency(inputDeps);
        #endregion

        public static Dictionary<Type, Dictionary<int, Type>> CollectSystemTypes()
        {
            var result = new Dictionary<Type, Dictionary<int, Type>>();

            Dictionary<int, Type> types;
            IEnumerable<EntityDataDeserializeAttribute> attributes;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                attributes = assembly.GetCustomAttributes<EntityDataDeserializeAttribute>();
                if (attributes != null)
                {
                    foreach (var attribute in attributes)
                    {
                        if(!result.TryGetValue(attribute.type, out types))
                        {
                            types = new Dictionary<int, Type>();

                            result[attribute.type] = types;
                        }

                        types.Add(attribute.version, attribute.systemType);
                    }
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

        public EntityDataDeserializationSystemGroup()
        {
            //UseLegacySortOrder = false;
        }

        public abstract EntityDataDeserializationCommander CreateCommander();

        protected override void OnCreate()
        {
            base.OnCreate();

            var world = World;
            var initializationSystem = world.GetOrCreateSystemManaged<EntityDataDeserializationInitializationSystem>();
            initializationSystem._systemGroup = this;
            
            this.initializationSystem = initializationSystem;

            var containerSystem = world.GetOrCreateSystemManaged<EntityDataDeserializationContainerSystem>();
            containerSystem._systemGroup = this;

            this.containerSystem = containerSystem;

            var componentSystem = world.GetOrCreateSystemManaged<EntityDataDeserializationComponentSystem>();
            componentSystem._systemGroup = this;

            this.componentSystem = componentSystem;

            commandSystem = world.GetOrCreateSystemManaged<EntityDataDeserializationCommandSystem>();

            var presentationSystem = world.GetOrCreateSystemManaged<EntityDataDeserializationPresentationSystem>();
            presentationSystem._systemGroup = this;
            this.presentationSystem = presentationSystem;

            var clearSystem = world.GetOrCreateSystemManaged<EntityDataDeserializationClearSystem>();
            clearSystem._systemGroup = this;

            this.clearSystem = clearSystem;
        }

        protected override void OnDestroy()
        {
            if(__buffer.isCreated)
                __buffer.Dispose();

            base.OnDestroy();
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            isInit = false;

            var bytes = _GetBytes();

            if (bytes == null)
            {
                Enabled = false;

                return;
            }

            if (__buffer.isCreated)
                return;

            __buffer = new NativeBuffer(Allocator.Persistent, 1, UnsafeUtility.SizeOf<byte>() * bytes.Length);
            var writer = __buffer.writer;

            writer.Write(bytes);

            __buffer.position = 0;

            var reader = __buffer.reader;

            //m_systemsToUpdate.Clear();

            AddSystemToUpdateList(initializationSystem);
            AddSystemToUpdateList(containerSystem);
            AddSystemToUpdateList(componentSystem);
            AddSystemToUpdateList(commandSystem);
            AddSystemToUpdateList(presentationSystem);

            int oldVersion = reader.Read<int>();
            this.version = oldVersion;

            int typeCount = reader.Read<int>();
            this.typeCount = typeCount;

            var world = World;

            int version, systemVersion;
            string typeName;
            Match match;
            EntityDataDeserializationSystem system;
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
                    type = _CreateType(typeName);

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

                        if(version > oldVersion && version < systemVersion)
                        {
                            systemVersion = version;

                            deserializationSystemType = pair.Value;
                        }
                    }
                }

                if(deserializationSystemType == null)
                {
                    UnityEngine.Debug.LogError($"System Type {typeName}(Version {oldVersion}) Can not be found!");

                    continue;
                }

                system = (EntityDataDeserializationSystem)world.GetOrCreateSystemManaged(deserializationSystemType);
                system.typeIndex = i;
                system.systemGroup = this;
                AddSystemToUpdateList(system);
            }

            AddSystemToUpdateList(clearSystem);

            //SortSystems();
        }

        /*protected override void OnStopRunning()
        {
            //EntityManager.CompleteAllJobs();

            foreach(var system in m_systemsToUpdate)
                World.DestroySystem(system);

            m_systemsToUpdate.Clear();

            base.OnStopRunning();
        }/

        protected override void OnUpdate()
        {
            __lookupJobManager.CompleteReadWriteDependency();

            base.OnUpdate();

            isInit = true;
        }

        protected virtual Type _CreateType(string name)
        {
            return Type.GetType(name);
        }

        protected abstract byte[] _GetBytes();
    }*/

    /*[DisableAutoCreation, UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true)]
    public partial class EntityDataDeserializationInitializationSystem : EntityCommandSystem
    {
        [BurstCompile]
        private struct Command : IJob
        {
            public Unity.Mathematics.Random random;

            [ReadOnly]
            public NativeArray<Hash128>.ReadOnly typeInputs;

            public NativeList<Hash128> typeOutputs;

            public NativeList<Hash128> guids;

            public NativeParallelHashMap<int, int> typeIndices;

            public NativeBuffer.Reader reader;

            public EntityCommandQueue<EntityDataIdentity>.Writer entityManager;

            public void Execute()
            {
                int entityCount = reader.Read<int>();

                guids.ResizeUninitialized(entityCount);

                var identities = new NativeArray<EntityDataIdentity>(reader.ReadArray<EntityDataIdentity>(entityCount), Allocator.Temp);

                int typeCount = reader.Read<int>();
                typeOutputs.Clear();
                typeOutputs.AddRange(reader.ReadArray<Hash128>(typeCount));

                int i, j, source, destination;
                EntityDataIdentity identity;
                for (i = 0; i < entityCount; ++i)
                {
                    identity = identities[i];

                    for (j = 0; j < i; ++j)
                    {
                        if (guids[j] == identity.guid)
                        {
                            UnityEngine.Debug.LogError($"The Same Guids: {identity.guid}");

                            identity.guid.Value = random.NextUInt4();

                            break;
                        }
                    }

                    guids[i] = identity.guid;

                    source = identity.type;
                    if (!typeIndices.TryGetValue(source, out destination))
                    {
                        destination = typeInputs.IndexOf<Hash128, Hash128>(typeOutputs[source]);

                        typeIndices[source] = destination;
                    }

                    identity.type = destination;

                    entityManager.Enqueue(identity);
                }

                identities.Dispose();
            }
        }

        internal EntityDataDeserializationSystemGroup _systemGroup;

        private EntityCommandPool<EntityDataIdentity> __entityManager;

        private NativeList<Hash128> __guids;
        private NativeList<Hash128> __types;

        public int entityCount => __guids.Length;

        public NativeArray<Hash128> guids => __guids.AsArray();

        public NativeArray<Hash128> types => __types.AsArray();

        public NativeParallelHashMap<int, int> typeIndices
        {
            get;

            private set;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            __guids = new NativeList<Hash128>(Allocator.Persistent);

            __types = new NativeList<Hash128>(Allocator.Persistent);

            typeIndices = new NativeParallelHashMap<int, int>(1, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            //Destroy<EntityDataIdentity, EntityDataDeserializationCommander>(EntityCommandManager.QUEUE_PRESENT);

            __guids.Dispose();

            __types.Dispose();

            typeIndices.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (!_systemGroup.isInit)
            {
                _systemGroup.CompleteReadWriteDependency();

                if(!__entityManager.isCreated)
                    __entityManager = Create<EntityDataIdentity, EntityDataDeserializationCommander>(EntityCommandManager.QUEUE_PRESENT, _systemGroup.CreateCommander());

                __entityManager.Clear();

                Command command;
                command.random = new Unity.Mathematics.Random((uint)DateTime.UtcNow.Ticks);
                command.typeInputs = _systemGroup.types;
                command.typeOutputs = __types;
                command.typeIndices = typeIndices;
                command.guids = __guids;
                command.reader = _systemGroup.reader;
                command.entityManager = __entityManager.Create().writer;

                command.Run();
            }

            base.OnUpdate();
        }
    }*/

    /*[DisableAutoCreation, UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true), UpdateAfter(typeof(EntityDataDeserializationInitializationSystem))]
    public partial class EntityDataDeserializationCommandSystem : EntityCommandSystemHybrid
    {

    }*/

    /*[DisableAutoCreation, AlwaysUpdateSystem, UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true), UpdateAfter(typeof(EntityDataDeserializationCommandSystem))]
    public partial class EntityDataDeserializationPresentationSystem : ReadOnlyLookupSystem
    {
        private struct Build
        {
            [ReadOnly]
            public NativeArray<Hash128> guids;
            [ReadOnly]
            public NativeArray<Entity> entityArray;
            [ReadOnly]
            public NativeArray<EntityDataDeserializable> identities;
            public NativeParallelHashMap<int, Entity>.ParallelWriter entities;

            public void Execute(int index)
            {
                bool result = entities.TryAdd(NativeArrayExtensions.IndexOf(guids, identities[index].guid), entityArray[index]);
                //UnityEngine.Debug.Log($"{identities[index].guid} : {entityArray[index]} : {NativeArrayExtensions.IndexOf<Hash128>(guids, identities[index].guid)} : {result}");// : {EntityComponentAssigner.hehe}");
                //if(!result)
                    UnityEngine.Assertions.Assert.IsTrue(result, $"{entityArray[index]}");
            }
        }

        [BurstCompile]
        private struct BuildEx : IJobChunk
        {
            [ReadOnly]
            public NativeArray<Hash128> guids;
            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public ComponentTypeHandle<EntityDataDeserializable> identityType;
            public NativeParallelHashMap<int, Entity>.ParallelWriter entities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Build build;
                build.guids = guids;
                build.entityArray = chunk.GetNativeArray(entityType);
                build.identities = chunk.GetNativeArray(ref identityType);
                build.entities = entities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while(iterator.NextEntityIndex(out int i))
                    build.Execute(i);
            }
        }

        internal EntityDataDeserializationSystemGroup _systemGroup;

        public EntityQuery group
        {
            get;

            private set;
        }

        public NativeParallelHashMap<int, Entity> entities
        {
            get;

            private set;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            group = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        //ComponentType.ReadOnly<EntityDataIdentity>(), 
                        ComponentType.ReadOnly<EntityDataDeserializable>()
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

        }

        protected override void OnDestroy()
        {
            if(entities.IsCreated)
                entities.Dispose();

            base.OnDestroy();
        }

        protected override void _Update()
        {
            if(!entities.IsCreated)
                entities = new NativeParallelHashMap<int, Entity>(_systemGroup.initializationSystem.guids.Length, Allocator.Persistent);

            BuildEx build;
            build.guids = _systemGroup.initializationSystem.guids;
            build.entityType = GetEntityTypeHandle();
            build.identityType = GetComponentTypeHandle<EntityDataDeserializable>(true);
            build.entities = entities.AsParallelWriter();
            var jobHandle = build.ScheduleParallel(group, Dependency);

            Dependency = jobHandle;
        }
    }*/

    /*[DisableAutoCreation,
        UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true),
        UpdateAfter(typeof(EntityDataDeserializationInitializationSystem))]
    public partial class EntityDataDeserializationContainerSystem : ReadOnlyLookupSystem
    {
        [BurstCompile]
        private struct Build : IJob
        {
            public NativeBuffer.Reader reader;

            public NativeParallelHashMap<int, UnsafeBlock> blocks;

            public void Execute()
            {
                int typeHandle = reader.Read<int>(), length;
                while (typeHandle > 0)
                {
                    length = reader.Read<int>();
                    blocks[typeHandle - 1] = reader.ReadBlock(length);

                    typeHandle = reader.Read<int>();
                }
            }
        }

        internal EntityDataDeserializationSystemGroup _systemGroup;

        public NativeParallelHashMap<int, UnsafeBlock> blocks
        {
            get;

            private set;
        }

        protected override void OnDestroy()
        {
            if(blocks.IsCreated)
                blocks.Dispose();

            base.OnDestroy();
        }

        protected override void _Update()
        {
            if (_systemGroup.isInit)
                return;

            if(!blocks.IsCreated)
                blocks = new NativeParallelHashMap<int, UnsafeBlock>(_systemGroup.typeCount, Allocator.Persistent);

            Build build;
            build.reader = _systemGroup.reader;
            build.blocks = blocks;
            var jobHandle = build.Schedule(JobHandle.CombineDependencies(_systemGroup.readWriteJobHandle, Dependency));

            _systemGroup.readWriteJobHandle = jobHandle;
            Dependency = jobHandle;
        }
    }*/

    /*[DisableAutoCreation,
        UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderFirst = true),
        UpdateBefore(typeof(EntityDataDeserializationCommandSystem)),
        UpdateAfter(typeof(EntityDataDeserializationContainerSystem))]
    public partial class EntityDataDeserializationComponentSystem : SystemBase
    {
        [BurstCompile]
        private struct Build : IJob
        {
            [ReadOnly]
            public NativeArray<Hash128> guids;

            public NativeBuffer.Reader reader;

            public NativeParallelHashMap<int, UnsafeParallelHashMap<Hash128, UnsafeBlock>> blocks;

            public void Execute()
            {
                int entityCount = guids.Length, typeHandle, typeIndex;
                Hash128 guid;
                UnsafeParallelHashMap<Hash128, UnsafeBlock> blocks;
                for (int i = 0; i < entityCount; ++i)
                {
                    guid = guids[i];

                    typeHandle = reader.Read<int>();
                    while (typeHandle > 0)
                    {
                        typeIndex = typeHandle - 1;
                        if (!this.blocks.TryGetValue(typeIndex, out blocks))
                        {
                            blocks = new UnsafeParallelHashMap<Hash128, UnsafeBlock>(entityCount, Allocator.Persistent);

                            this.blocks[typeIndex] = blocks;
                        }

                        blocks[guid] = reader.ReadBlock(reader.Read<int>());

                        typeHandle = reader.Read<int>();
                    }
                }
            }
        }

        internal EntityDataDeserializationSystemGroup _systemGroup;

        public JobHandle jobHandle
        {
            get;

            private set;
        }

        public NativeParallelHashMap<int, UnsafeParallelHashMap<Hash128, UnsafeBlock>> blocks
        {
            get;

            private set;
        }

        protected override void OnDestroy()
        {
            if(blocks.IsCreated)
                blocks.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (_systemGroup.isInit)
                return;

            if(!blocks.IsCreated)
                blocks = new NativeParallelHashMap<int, UnsafeParallelHashMap<Hash128, UnsafeBlock>>(_systemGroup.typeCount, Allocator.Persistent);

            Build build;
            build.guids = _systemGroup.initializationSystem.guids;
            build.reader = _systemGroup.reader;
            build.blocks = blocks;

            var jobHandle = build.Schedule(JobHandle.CombineDependencies(_systemGroup.readWriteJobHandle, Dependency));

            _systemGroup.readWriteJobHandle = jobHandle;

            this.jobHandle = jobHandle;

            Dependency = jobHandle;
        }
    }*/

    /*[DisableAutoCreation, UpdateInGroup(typeof(EntityDataDeserializationSystemGroup), OrderLast = true)]
    public partial class EntityDataDeserializationClearSystem : EntityCommandSystem
    {
        internal EntityDataDeserializationSystemGroup _systemGroup;

        protected override void OnUpdate()
        {
            var presentationSystem = _systemGroup.presentationSystem;

            EntityManager.RemoveComponent<EntityDataDeserializable>(presentationSystem.group);

            base.OnUpdate();

            presentationSystem.CompleteReadOnlyDependency();
            if(presentationSystem.entities.Count() == _systemGroup.initializationSystem.guids.Length)
                _systemGroup.Enabled = false;
        }
    }*/

    /*[UpdateInGroup(typeof(EntityDataDeserializationSystemGroup))]
    public abstract partial class EntityDataDeserializationSystem : SystemBase
    {
        public int typeIndex
        {
            get;

            internal set;
        }

        public EntityDataDeserializationSystemGroup systemGroup
        {
            get;

            internal set;
        }
    }*/

    /*public abstract partial class EntityDataDeserializationContainerSystem<T> : EntityDataDeserializationSystem where T : struct, IEntityDataContainerDeserializer
    {
        protected override void OnUpdate()
        {
            if (systemGroup.isInit)
                return;

            var jobHandle = Dependency;

            var containerSystem = systemGroup.containerSystem;

            EntityDataContainerDeserialize<T> deserialize;
            deserialize.typeIndex = typeIndex;
            deserialize.blocks = containerSystem.blocks;
            deserialize.container = _Create(ref jobHandle);

            jobHandle = deserialize.Schedule(JobHandle.CombineDependencies(jobHandle, containerSystem.readOnlyJobHandle));

            containerSystem.AddReadOnlyDependency(jobHandle);

            Dependency = jobHandle;
        }

        protected abstract T _Create(ref JobHandle inputDeps);
    }

    public abstract partial class EntityDataDeserializationComponentSystem<TData, TDeserializer, TDeserializerFactory> : EntityDataDeserializationSystem
        where TData : struct
        where TDeserializer : struct, IEntityDataDeserializer
        where TDeserializerFactory : struct, IEntityDataFactory<TDeserializer>
    {
        public EntityQuery group
        {
            get;

            private set;
        }

        public virtual bool isSingle => false;

        protected override void OnCreate()
        {
            base.OnCreate();

            group = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<EntityDataIdentity>(),
                        ComponentType.ReadOnly<EntityDataDeserializable>(),
                        ComponentType.ReadWrite<TData>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
        }

        protected override void OnUpdate()
        {
            //不能这样，否则GameDataItem出错
            /*if (group.IsEmptyIgnoreFilter)
                return;/

            var componentSystem = systemGroup.componentSystem;

            var jobHandle = Dependency;
            var blocks = new NativeArray<UnsafeParallelHashMap<Hash128, UnsafeBlock>>(1, Allocator.TempJob);

            EntityDataDeserializationGetMap getMap;
            getMap.typeIndex = typeIndex;
            getMap.input = componentSystem.blocks;
            getMap.output = blocks;
            jobHandle = getMap.Schedule(JobHandle.CombineDependencies(jobHandle, componentSystem.jobHandle));

            jobHandle = _Update(blocks, jobHandle);

            EntityDataComponentDeserialize<TDeserializer, TDeserializerFactory> deserialize;
            deserialize.componentTypeName = typeof(TData).Name;
            deserialize.identityType = GetComponentTypeHandle<EntityDataIdentity>(true);
            deserialize.blocks = blocks;
            deserialize.factory = _Get(ref jobHandle);

            jobHandle = isSingle ? deserialize.Schedule(group, jobHandle) : deserialize.ScheduleParallel(group, jobHandle);

            Dependency = jobHandle;
        }

        protected virtual JobHandle _Update(in NativeArray<UnsafeParallelHashMap<Hash128, UnsafeBlock>> blocks, in JobHandle inputDeps)
        {
            return inputDeps;
        }

        protected abstract TDeserializerFactory _Get(ref JobHandle jobHandle);
    }

    public partial class ComponentDataDeserializationSystem<T> : EntityDataDeserializationComponentSystem<
        T,
        ComponentDataDeserializationSystem<T>.Deserializer,
        ComponentDataDeserializationSystem<T>.DeserializerFactory> where T : unmanaged, IComponentData
    {
        public struct Deserializer : IEntityDataDeserializer
        {
            public NativeArray<T> values;

            public void Deserialize(int index, ref EntityDataReader reader)
            {
                values[index] = reader.Read<T>();
            }
        }

        public struct DeserializerFactory : IEntityDataFactory<Deserializer>
        {
            public ComponentTypeHandle<T> type;

            public Deserializer Create(in ArchetypeChunk chunk, int unfilteredChunkIndex)
            {
                Deserializer deserializer;
                deserializer.values = chunk.GetNativeArray(ref type);

                return deserializer;
            }
        }

        protected override DeserializerFactory _Get(ref JobHandle jobHandle)
        {
            DeserializerFactory factory;
            factory.type = GetComponentTypeHandle<T>();

            return factory;
        }
    }

    public partial class BufferDeserializationSystem<T> : EntityDataDeserializationComponentSystem<
        T,
        BufferDeserializationSystem<T>.Deserializer,
        BufferDeserializationSystem<T>.DeserializerFactory> where T : unmanaged, IBufferElementData
    {
        public struct Deserializer : IEntityDataDeserializer
        {
            [ReadOnly]
            public BufferAccessor<T> values;

            public void Deserialize(int index, ref EntityDataReader reader)
            {
                var values = this.values[index];
                values.CopyFrom(reader.ReadArray<T>(reader.Read<int>()));
            }
        }

        public struct DeserializerFactory : IEntityDataFactory<Deserializer>
        {
            public BufferTypeHandle<T> type;

            public Deserializer Create(in ArchetypeChunk chunk, int firstEntityIndex)
            {
                Deserializer deserializer;
                deserializer.values = chunk.GetBufferAccessor(ref type);

                return deserializer;
            }
        }

        protected override DeserializerFactory _Get(ref JobHandle jobHandle)
        {
            DeserializerFactory factory;
            factory.type = GetBufferTypeHandle<T>();

            return factory;
        }
    }*/
    #endregion
}