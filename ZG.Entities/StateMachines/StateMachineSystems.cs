using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    [UpdateInGroup(typeof(TimeSystemGroup)), UpdateBefore(typeof(StateMachineExecutorGroup))]
    public class StateMachineSchedulerGroup : ComponentSystemGroup
    {

    }

    [UpdateInGroup(typeof(TimeSystemGroup))]
    public class StateMachineExecutorGroup : ComponentSystemGroup
    {

    }


    public abstract partial class StateMachineSystemBase : SystemBase
    {
        private static Pool<StateMachineSystemBase> __systems;

        private int __systemIndex;

        public int systemIndex
        {
            get
            {
                return __systemIndex;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            if (__systems == null)
                __systems = new Pool<StateMachineSystemBase>();

            __systemIndex = __systems.Add(this);
        }

        protected override void OnDestroy()
        {
            if (__systems != null)
                __systems.RemoveAt(__systemIndex);

            __systemIndex = -1;

            base.OnDestroy();
        }
    }

    [UpdateInGroup(typeof(StateMachineSchedulerGroup))]
    public abstract partial class StateMachineSchedulerSystem<TSchedulerExit, TSchedulerEntry, TFactoryExit, TFactoryEntry, TSystem> : StateMachineSystemBase
        where TSchedulerExit : struct, IStateMachineScheduler
        where TSchedulerEntry : struct, IStateMachineScheduler
        where TFactoryExit : struct, IStateMachineFactory<TSchedulerExit>
        where TFactoryEntry : struct, IStateMachineFactory<TSchedulerEntry>
        where TSystem : StateMachineSchedulerSystem<TSchedulerExit, TSchedulerEntry, TFactoryExit, TFactoryEntry, TSystem>
    {
        [Serializable, InternalBufferCapacity(1)]
        public struct StateMachine : IStateMachine
        {
            public int executorIndex
            {
                get;

                set;
            }
        }

        [UpdateInGroup(typeof(StateMachineExecutorGroup))]
        public abstract partial class StateMachineExecutorSystem<TEscaper, TExecutor, TEscaperFactory, TExecutorFactory> : StateMachineSystemBase
            where TEscaper : struct, IStateMachineEscaper
            where TExecutor : struct, IStateMachineExecutor
            where TEscaperFactory : struct, IStateMachineFactory<TEscaper>
            where TExecutorFactory : struct, IStateMachineFactory<TExecutor>
        {
            private TSystem __system;

            public EntityQuery exitGroup
            {
                get;

                private set;
            }

            public EntityQuery runGroup
            {
                get;

                private set;
            }

            public abstract IEnumerable<EntityQueryDesc> runEntityArchetypeQueries
            {
                get;
            }

            protected override void OnCreate()
            {
                base.OnCreate();

                exitGroup = GetEntityQuery(
                    ComponentType.ReadOnly<StateMachine>(),
                    ComponentType.ReadOnly<StateMachineInfo>(),
                    ComponentType.ReadWrite<StateMachineStatus>());

                IEnumerable<EntityQueryDesc> runEntityArchetypeQueries = this.runEntityArchetypeQueries;
                if (runEntityArchetypeQueries != null)
                {
                    List<ComponentType> componentTypes = new List<ComponentType>();
                    List<EntityQueryDesc> entityArchetypeQueries = null;
                    EntityQueryDesc destination;
                    foreach (EntityQueryDesc source in runEntityArchetypeQueries)
                    {
                        if (source == null)
                            continue;

                        destination = new EntityQueryDesc();

                        componentTypes.Clear();
                        componentTypes.Add(ComponentType.ReadOnly<StateMachineInfo>());
                        componentTypes.Add(ComponentType.ReadWrite<StateMachineStatus>());
                        componentTypes.Add(ComponentType.ReadWrite<StateMachine>());
                        if (source.All != null)
                            componentTypes.AddRange(source.All);

                        destination.All = componentTypes.ToArray();
                        destination.Any = source.Any;
                        destination.None = source.None;
                        destination.Options = source.Options;

                        if (entityArchetypeQueries == null)
                            entityArchetypeQueries = new List<EntityQueryDesc>();

                        entityArchetypeQueries.Add(destination);
                    }

                    if (entityArchetypeQueries != null)
                        runGroup = GetEntityQuery(entityArchetypeQueries.ToArray());
                }

                __system = World.GetOrCreateSystemManaged<TSystem>();
            }

            protected override void OnUpdate()
            {
                int systemIndex = __system.systemIndex, executorIndex = base.systemIndex;
                var inputDeps = Dependency;
                var infoType = GetComponentTypeHandle<StateMachineInfo>(true);
                var statusType = GetComponentTypeHandle<StateMachineStatus>();
                var stateMachineType = GetBufferTypeHandle<StateMachine>();

                StateMachineEscaper<StateMachine, TEscaper, TEscaperFactory> escaper;
                escaper.systemIndex = systemIndex;
                escaper.executorIndex = executorIndex;
                escaper.factory = _GetExit(ref inputDeps);
                escaper.infoType = infoType;
                escaper.statusType = statusType;
                escaper.stateMachineType = stateMachineType;
                inputDeps = escaper.ScheduleParallel(exitGroup, inputDeps);

                StateMachineExecutor<StateMachine, TExecutor, TExecutorFactory> executor;
                executor.systemIndex = systemIndex;
                executor.executorIndex = executorIndex;
                executor.factory = _GetRun(ref inputDeps);
                executor.infoType = infoType;
                executor.statusType = statusType;
                executor.stateMachineType = stateMachineType;

                Dependency = executor.ScheduleParallel(runGroup, inputDeps);
            }

            protected abstract TExecutorFactory _GetRun(ref JobHandle inputDeps);

            protected abstract TEscaperFactory _GetExit(ref JobHandle inputDeps);
        }

        public abstract partial class StateMachineExecutorSystem<TExecutor, TExecutorFactory> : StateMachineExecutorSystem<StateMachineEscaper, TExecutor, StateMachineFactory<StateMachineEscaper>, TExecutorFactory>
            where TExecutor : struct, IStateMachineExecutor
            where TExecutorFactory : struct, IStateMachineFactory<TExecutor>
        {
            protected override StateMachineFactory<StateMachineEscaper> _GetExit(ref JobHandle inputDeps)
            {
                return default;
            }
        }

        private EntityQuery __exitGroup;
        private EntityQuery __entryGroup;
        private EntityCommandPool<Entity> __addComponentCommander;
        private EntityCommandPool<Entity> __removeComponentCommander;

        public abstract IEnumerable<EntityQueryDesc> entryEntityArchetypeQueries
        {
            get;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            __exitGroup = GetEntityQuery(
                ComponentType.ReadOnly<StateMachine>(),
                ComponentType.ReadOnly<StateMachineStatus>(),
                ComponentType.ReadWrite<StateMachineInfo>());

            IEnumerable<EntityQueryDesc> entryEntityArchetypeQueries = this.entryEntityArchetypeQueries;
            if (entryEntityArchetypeQueries != null)
            {
                List<ComponentType> componentTypes = new List<ComponentType>();
                List<EntityQueryDesc> entityArchetypeQueries = null;
                EntityQueryDesc destination;
                foreach (EntityQueryDesc source in entryEntityArchetypeQueries)
                {
                    if (source == null)
                        continue;

                    destination = new EntityQueryDesc();

                    componentTypes.Clear();
                    componentTypes.Add(ComponentType.ReadOnly<StateMachineStatus>());
                    componentTypes.Add(ComponentType.ReadWrite<StateMachineInfo>());
                    if (source.All != null)
                        componentTypes.AddRange(source.All);

                    destination.All = componentTypes.ToArray();
                    destination.Any = source.Any;

                    componentTypes.Clear();
                    componentTypes.Add(typeof(StateMachine));
                    if (source.None != null)
                        componentTypes.AddRange(source.None);

                    destination.None = componentTypes.ToArray();
                    destination.Options = source.Options;

                    if (entityArchetypeQueries == null)
                        entityArchetypeQueries = new List<EntityQueryDesc>();

                    entityArchetypeQueries.Add(destination);
                }

                if (entityArchetypeQueries != null)
                    __entryGroup = GetEntityQuery(entityArchetypeQueries.ToArray());
            }

            var endFrameBarrier = World.GetOrCreateSystemManaged<EndTimeSystemGroupEntityCommandSystem>();

            __removeComponentCommander = endFrameBarrier.CreateRemoveComponentCommander<StateMachine>();
            __addComponentCommander = endFrameBarrier.CreateAddComponentCommander<StateMachine>();

#if DEBUG
            EntityCommandUtility.RegisterProducerJobType<StateMachineExit<StateMachine, TSchedulerExit, TFactoryExit>>();
            EntityCommandUtility.RegisterProducerJobType<StateMachineEntry<TSchedulerEntry, TFactoryEntry>>();
#endif
        }

        protected override void OnUpdate()
        {
            var inputDeps = Dependency;
            var entityType = GetEntityTypeHandle();
            var statusType = GetComponentTypeHandle<StateMachineStatus>(true);
            var infoType = GetComponentTypeHandle<StateMachineInfo>();

            if (!__exitGroup.IsEmptyIgnoreFilter)
            {
                var removeComponentCommander = __removeComponentCommander.Create();

                StateMachineExit<StateMachine, TSchedulerExit, TFactoryExit> exit;
                exit.systemIndex = systemIndex;
                exit.factory = _GetExit(ref inputDeps);
                exit.entityType = entityType;
                exit.statusType = statusType;
                exit.infoType = infoType;
                exit.stateMachineType = GetBufferTypeHandle<StateMachine>();
                exit.entityManager = removeComponentCommander.parallelWriter;
                inputDeps = exit.ScheduleParallel(__exitGroup, inputDeps);

                removeComponentCommander.AddJobHandleForProducer<StateMachineExit<StateMachine, TSchedulerExit, TFactoryExit>>(inputDeps);
            }

            if (!__entryGroup.IsEmptyIgnoreFilter)
            {
                var addComponentCommander = __addComponentCommander.Create();

                StateMachineEntry<TSchedulerEntry, TFactoryEntry> entry;
                entry.systemIndex = systemIndex;
                entry.factory = _GetEntry(ref inputDeps);
                entry.entityType = entityType;
                entry.statusType = statusType;
                entry.infoType = infoType;
                entry.entityManager = addComponentCommander.parallelWriter;
                inputDeps = entry.ScheduleParallel(__entryGroup, inputDeps);

                addComponentCommander.AddJobHandleForProducer<StateMachineEntry<TSchedulerEntry, TFactoryEntry>>(inputDeps);
            }

            Dependency = inputDeps;
        }

        protected abstract TFactoryEntry _GetEntry(ref JobHandle inputDeps);

        protected abstract TFactoryExit _GetExit(ref JobHandle inputDeps);
    }

    public abstract partial class StateMachineSchedulerSystem<TSchedulerEntry, TFactoryEntry, TSystem> : StateMachineSchedulerSystem<StateMachineScheduler, TSchedulerEntry, StateMachineFactory<StateMachineScheduler>, TFactoryEntry, TSystem>
        where TSchedulerEntry : struct, IStateMachineScheduler
        where TFactoryEntry : struct, IStateMachineFactory<TSchedulerEntry>
        where TSystem : StateMachineSchedulerSystem<TSchedulerEntry, TFactoryEntry, TSystem>
    {
        protected override StateMachineFactory<StateMachineScheduler> _GetExit(ref JobHandle inputDeps)
        {
            return default;
        }
    }
}