using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    [BurstCompile, UpdateInGroup(typeof(TimeSystemGroup))]
    public struct StateMachineGroup : ISystem
    {
        private SystemGroup __systemGroup;

        public void OnCreate(ref SystemState state)
        {
            __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(state.World, typeof(StateMachineGroup));
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var world = state.WorldUnmanaged;
            __systemGroup.Update(ref world);
        }
    }

    //[UpdateInGroup(typeof(StateMachineSchedulerGroup))]
    public struct StateMachineSystemCore
    {
        private ComponentTypeHandle<StateMachineStatus> __statusType;
        private ComponentTypeHandle<StateMachineResult> __resultType;

        public EntityQuery exitGroup
        {
            get;
        }

        public EntityQuery entryGroup
        {
            get;
        }

        public StateMachineSystemCore(
            ref SystemState state, 
            EntityQueryBuilder entryBuilder)
        {
            entryGroup = entryBuilder
                .WithAllRW<StateMachineStatus, StateMachineResult>()
                .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                exitGroup = builder
                    .WithAll<StateMachineStatus>()
                    .WithAllRW<StateMachineResult>()
                    .Build(ref state);

            __statusType = state.GetComponentTypeHandle<StateMachineStatus>();
            __resultType = state.GetComponentTypeHandle<StateMachineResult>();
        }

        public void Update<
            TSchedulerExit, 
            TSchedulerEntry, 
            TSchedulerRun, 
            TFactoryExit, 
            TFactoryEntry, 
            TFactoryRun>(
            ref SystemState state, 
            ref TFactoryRun factoryRun, 
            ref TFactoryEntry factoryEntry, 
            ref TFactoryExit factoryExit)
            where TSchedulerExit : struct, IStateMachineCondition
            where TSchedulerEntry : struct, IStateMachineCondition
            where TSchedulerRun : struct, IStateMachineExecutor
            where TFactoryExit : struct, IStateMachineFactory<TSchedulerExit>
            where TFactoryEntry : struct, IStateMachineFactory<TSchedulerEntry>
            where TFactoryRun : struct, IStateMachineFactory<TSchedulerRun>
        {
            var systemHandle = state.SystemHandle;
            var statusType = __statusType.UpdateAsRef(ref state);
            var resultType = __resultType.UpdateAsRef(ref state);
            
            StateMachineRunJob<TSchedulerRun, TFactoryRun> run;
            run.systemHandle = systemHandle;
            run.factory = factoryRun;
            run.resultType = resultType;
            run.statusType = statusType;
            var jobHandle = run.ScheduleParallelByRef(entryGroup, state.Dependency);
            
            StateMachineEntryJob<TSchedulerEntry, TFactoryEntry> entry;
            entry.systemHandle = systemHandle;
            entry.factoryEntry = factoryEntry;
            entry.statusType = statusType;
            entry.resultType = resultType;
            jobHandle = entry.ScheduleParallelByRef(entryGroup, jobHandle);

            StateMachineExitJob<TSchedulerExit, TFactoryExit> exit;
            exit.systemHandle = systemHandle;
            exit.factoryExit = factoryExit;
            exit.statusType = statusType;
            exit.resultType = resultType;
            state.Dependency = exit.ScheduleParallelByRef(exitGroup, jobHandle);
        }
    }
}