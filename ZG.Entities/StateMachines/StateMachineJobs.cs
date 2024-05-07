using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using ZG;

[assembly: RegisterGenericJobType(typeof(StateMachineExitJob<
    StateMachineCondition, 
    StateMachineFactory<StateMachineCondition>>))]

namespace ZG
{
    public interface IStateMachineCondition
    {
        bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index);
    }

    public interface IStateMachineExecutor
    {
        int Execute(bool isEntry, int index);
    }

    public interface IStateMachineFactory<T> where T : struct
    {
        bool Create(int unfilteredChunkIndex, in ArchetypeChunk chunk, out T value);
    }

    public struct StateMachineFactory<T> : IStateMachineFactory<T> where T : struct
    {
        public bool Create(int unfilteredChunkIndex, in ArchetypeChunk chunk, out T value)
        {
            value = default;

            return true;
        }
    }

    public struct StateMachineCondition : IStateMachineCondition
    {
        public bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index)
        {
            return true;
        }
    }

    [BurstCompile]
    public struct StateMachineExitJob<TExit, TFactoryExit> : IJobChunk
        where TExit : struct, IStateMachineCondition
        where TFactoryExit : struct, IStateMachineFactory<TExit>
    {
        public SystemHandle systemHandle;

        public TFactoryExit factoryExit;

        public ComponentTypeHandle<StateMachineStatus> statusType;
        [ReadOnly]
        public ComponentTypeHandle<StateMachineResult> resultType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TExit exit = default;

            var states = chunk.GetNativeArray(ref statusType);
            var results = chunk.GetNativeArray(ref resultType);
            StateMachineStatus status;
            StateMachineResult result;
            bool isCreated = false;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                result = results[i];
                if (result.systemHandle == systemHandle)
                    continue;

                status = states[i];
                if(status.systemHandle != systemHandle)
                    continue;

                if(!isCreated)
                {
                    if (!factoryExit.Create(unfilteredChunkIndex, chunk, out exit))
                        break;
                    
                    isCreated = true;
                }

                if (!exit.Execute(
                    status.value,
                    status.systemHandle,
                    result.systemHandle,
                    systemHandle,
                    i))
                    continue;

                status.systemHandle = SystemHandle.Null;
                states[i] = status;
            }
        }
    }
    
    [BurstCompile]
    public struct StateMachineEntryJob<TEntry, TFactoryEntry> : IJobChunk
        where TEntry : struct, IStateMachineCondition
        where TFactoryEntry : struct, IStateMachineFactory<TEntry>
    {
        public SystemHandle systemHandle;

        public TFactoryEntry factoryEntry;

        [ReadOnly]
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public ComponentTypeHandle<StateMachineResult> resultType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TEntry entry = default;

            var states = chunk.GetNativeArray(ref statusType);
            var results = chunk.GetNativeArray(ref resultType);
            StateMachineStatus status;
            StateMachineResult result;
            bool isCreated = false;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                status = states[i];
                if (status.systemHandle == systemHandle)
                    continue;

                result = results[i];
                if(result.systemHandle == systemHandle)
                    continue;

                if (!isCreated)
                {
                    if (!factoryEntry.Create(unfilteredChunkIndex, chunk, out entry))
                        break;
                    
                    isCreated = true;
                }

                if (!entry.Execute(
                    status.value,
                    status.systemHandle,
                    result.systemHandle,
                    systemHandle,
                    i))
                    continue;

                result.systemHandle = systemHandle;
                results[i] = result;
            }
        }
    }

    [BurstCompile]
    public struct StateMachineRunJob<TRun, TFactoryRun> : IJobChunk
        where TRun : struct, IStateMachineExecutor
        where TFactoryRun : struct, IStateMachineFactory<TRun>
    {
        public SystemHandle systemHandle;

        public TFactoryRun factory;

        [ReadOnly]
        public ComponentTypeHandle<StateMachineResult> resultType;
        public ComponentTypeHandle<StateMachineStatus> statusType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TRun run = default;

            var states = chunk.GetNativeArray(ref statusType);
            var results = chunk.GetNativeArray(ref resultType);

            bool isCreated = false;
            StateMachineStatus status;
            StateMachineResult result;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                result = results[i];
                if (result.systemHandle != systemHandle)
                    continue;

                if (!isCreated)
                {
                    isCreated = true;

                    if (!factory.Create(unfilteredChunkIndex, chunk, out run))
                        break;
                }
                
                status = states[i];
                status.value = run.Execute(status.systemHandle != systemHandle, i);
                status.systemHandle = systemHandle;

                states[i] = status;
            }
        }
    }
}