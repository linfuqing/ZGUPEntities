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

    /*public abstract partial class StateMachineSystemBase : SystemBase
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
    }*/

    [UpdateInGroup(typeof(StateMachineSchedulerGroup))]
    public struct StateMachineSchedulerSystemCore// : StateMachineSystemBase
        //where TSystem : StateMachineSchedulerSystem<TSchedulerExit, TSchedulerEntry, TFactoryExit, TFactoryEntry, TSystem>
    {

        //[Serializable, InternalBufferCapacity(1)]
        /*public struct StateMachine : IStateMachine
        {
            public SystemHandle executor
            {
                get;

                set;
            }
        }*/

        /*public abstract partial class StateMachineExecutorSystem<TExecutor, TExecutorFactory> : StateMachineExecutorSystem<StateMachineEscaper, TExecutor, StateMachineFactory<StateMachineEscaper>, TExecutorFactory>
            where TExecutor : struct, IStateMachineExecutor
            where TExecutorFactory : struct, IStateMachineFactory<TExecutor>
        {
            protected override StateMachineFactory<StateMachineEscaper> _GetExit(ref JobHandle inputDeps)
            {
                return default;
            }
        }*/

        private ComponentTypeHandle<StateMachineStatus> __statusType;
        private ComponentTypeHandle<StateMachineInfo> __infoType;
        private BufferTypeHandle<StateMachine> __instanceType;

        public EntityQuery group
        {
            get;
        }

        public StateMachineSchedulerSystemCore(ref SystemState state, EntityQueryBuilder entryBuilder)
        {
            group = entryBuilder
                .WithAll<StateMachineStatus>()
                .WithAllRW<StateMachineInfo, StateMachine>()
                //.WithNone<StateMachine>()
                .Build(ref state);

            __statusType = state.GetComponentTypeHandle<StateMachineStatus>(true);
            __infoType = state.GetComponentTypeHandle<StateMachineInfo>();
            __instanceType = state.GetBufferTypeHandle<StateMachine>();
        }

        public void Update<TSchedulerExit, TSchedulerEntry, TFactoryExit, TFactoryEntry>(
            ref SystemState state, 
            ref TFactoryEntry factoryEntry, 
            ref TFactoryExit factoryExit)
            where TSchedulerExit : struct, IStateMachineScheduler
            where TSchedulerEntry : struct, IStateMachineScheduler
            where TFactoryExit : struct, IStateMachineFactory<TSchedulerExit>
            where TFactoryEntry : struct, IStateMachineFactory<TSchedulerEntry>
        {
            StateMachineSchedulerJob<TSchedulerExit, TFactoryExit, TSchedulerEntry, TFactoryEntry> job;
            job.systemHandle = state.SystemHandle;
            job.factoryExit = factoryExit;
            job.factoryEntry = factoryEntry;
            job.statusType = __statusType.UpdateAsRef(ref state);
            job.infoType = __infoType.UpdateAsRef(ref state);
            job.instanceType = __instanceType.UpdateAsRef(ref state);
            state.Dependency = job.ScheduleParallelByRef(group, state.Dependency);
        }
    }

    [UpdateInGroup(typeof(StateMachineExecutorGroup))]
    public struct StateMachineExecutorSystemCore
    {
        private SystemHandle __schedulerSystemHandle;
        private ComponentTypeHandle<StateMachineInfo> __infoType;
        private ComponentTypeHandle<StateMachineStatus> __statusType;
        private BufferTypeHandle<StateMachine> __instanceType;

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

        public StateMachineExecutorSystemCore(ref SystemState state, EntityQueryBuilder runBuilder, in SystemHandle schedulerSystemHandle)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                exitGroup = builder
                        .WithAll<StateMachine, StateMachineInfo>()
                        .WithAllRW<StateMachineStatus>()
                        .Build(ref state);

            runGroup = runBuilder
                .WithAll<StateMachineInfo>()
                .WithAllRW<StateMachineStatus, StateMachine>()
                .Build(ref state);

            __infoType = state.GetComponentTypeHandle<StateMachineInfo>(true);
            __statusType = state.GetComponentTypeHandle<StateMachineStatus>();
            __instanceType = state.GetBufferTypeHandle<StateMachine>();

            __schedulerSystemHandle = schedulerSystemHandle;
        }

        public void Update<TEscaper, TExecutor, TEscaperFactory, TExecutorFactory>(
            ref SystemState state, 
            ref TExecutorFactory executorFactory, 
            ref TEscaperFactory escaperFactory)
            where TEscaper : struct, IStateMachineEscaper
            where TExecutor : struct, IStateMachineExecutor
            where TEscaperFactory : struct, IStateMachineFactory<TEscaper>
            where TExecutorFactory : struct, IStateMachineFactory<TExecutor>
        {
            SystemHandle executorSystemHandle = state.SystemHandle;
            var infoType = __infoType.UpdateAsRef(ref state);
            var statusType = __statusType.UpdateAsRef(ref state);
            var instanceType = __instanceType.UpdateAsRef(ref state);

            StateMachineEscaperJob<TEscaper, TEscaperFactory> escaper;
            escaper.systemHandle = __schedulerSystemHandle;
            escaper.executorSystemHandle = executorSystemHandle;
            escaper.factory = escaperFactory;
            escaper.infoType = infoType;
            escaper.statusType = statusType;
            escaper.instanceType = instanceType;
            var jobHandle = escaper.ScheduleParallelByRef(exitGroup, state.Dependency);

            StateMachineExecutorJob<TExecutor, TExecutorFactory> executor;
            executor.systemHandle = __schedulerSystemHandle;
            executor.executorSystemHandle = executorSystemHandle;
            executor.factory = executorFactory;
            executor.infoType = infoType;
            executor.statusType = statusType;
            executor.instanceType = instanceType;

            state.Dependency = executor.ScheduleParallelByRef(runGroup, jobHandle);
        }
    }

    /*public abstract partial class StateMachineSchedulerSystemCore<TSchedulerEntry, TFactoryEntry, TSystem> : StateMachineSchedulerSystem<StateMachineScheduler, TSchedulerEntry, StateMachineFactory<StateMachineScheduler>, TFactoryEntry, TSystem>
        where TSchedulerEntry : struct, IStateMachineScheduler
        where TFactoryEntry : struct, IStateMachineFactory<TSchedulerEntry>
        where TSystem : StateMachineSchedulerSystem<TSchedulerEntry, TFactoryEntry, TSystem>
    {
        protected override StateMachineFactory<StateMachineScheduler> _GetExit(ref JobHandle inputDeps)
        {
            return default;
        }
    }*/
}