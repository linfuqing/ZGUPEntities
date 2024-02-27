using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using UnityEngine.UIElements;

namespace ZG
{
    public interface IStateMachineScheduler
    {
        bool Execute(
            int runningStatus,
            in SystemHandle runningSystemHandle,
            in SystemHandle nextSystemHandle,
            in SystemHandle currentSystemHandle,
            int index);
    }

    public interface IStateMachineEscaper
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

    public struct StateMachineScheduler : IStateMachineScheduler
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

    public struct StateMachineEscaper : IStateMachineEscaper
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
    public struct StateMachineSchedulerJob<TSchedulerExit, TFactoryExit, TSchedulerEntry, TFactoryEntry> : IJobChunk
        where TSchedulerExit : struct, IStateMachineScheduler
        where TFactoryExit : struct, IStateMachineFactory<TSchedulerExit>
        where TSchedulerEntry : struct, IStateMachineScheduler
        where TFactoryEntry : struct, IStateMachineFactory<TSchedulerEntry>
    {
        public SystemHandle systemHandle;

        public TFactoryExit factoryExit;
        public TFactoryEntry factoryEntry;

        [ReadOnly]
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public ComponentTypeHandle<StateMachineInfo> infoType;
        public BufferTypeHandle<StateMachine> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TSchedulerExit schedulerExit = default;
            TSchedulerEntry schedulerEntry = default;

            var states = chunk.GetNativeArray(ref statusType);
            var infos = chunk.GetNativeArray(ref infoType);
            var instanceAccessor = chunk.GetBufferAccessor(ref instanceType);
            DynamicBuffer<StateMachine> instances;
            StateMachine instance;
            StateMachineStatus status;
            StateMachineInfo info;
            int length, j;
            bool isExit = false, isEntry = false, canExit = true, canEntry = true;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                info = infos[i];

                instances = instanceAccessor[i];
                length = instances.Length;
                for (j = 0; j < length; ++j)
                {
                    if (instances.ElementAt(j).systemHandle == systemHandle)
                        break;
                }

                if (j < length)
                {
                    if (info.systemHandle == systemHandle)
                        continue;

                    status = states[i];

                    if(!isExit)
                    {
                        isExit = true;
                        
                        canExit = factoryExit.Create(unfilteredChunkIndex, chunk, out schedulerExit);
                    }

                    if (!canExit || !schedulerExit.Execute(
                        status.value,
                        status.systemHandle,
                        info.systemHandle,
                        systemHandle,
                        i))
                        continue;

                    --info.count;

                    infos[i] = info;

                    instances.RemoveAtSwapBack(j);
                }
                else
                {
                    status = states[i];
                    if (status.systemHandle == SystemHandle.Null && info.systemHandle != SystemHandle.Null && info.systemHandle != systemHandle)
                        continue;

                    if (!isEntry)
                    {
                        isEntry = true;

                        canEntry = factoryEntry.Create(unfilteredChunkIndex, chunk, out schedulerEntry);
                    }

                    if (!canEntry || !schedulerEntry.Execute(
                        status.value,
                        status.systemHandle,
                        info.systemHandle,
                        systemHandle,
                        i))
                        continue;

                    ++info.count;

                    info.systemHandle = systemHandle;
                    infos[i] = info;

                    instance.systemHandle = systemHandle;
                    instance.executorSystemHandle = SystemHandle.Null;
                    instances.Add(instance);
                }
            }
        }
    }


    [BurstCompile]
    public struct StateMachineEscaperJob<TEscaper, TFactory> : IJobChunk
        where TEscaper : struct, IStateMachineEscaper
        where TFactory : struct, IStateMachineFactory<TEscaper>
    {
        public SystemHandle systemHandle;

        public SystemHandle executorSystemHandle;

        public TFactory factory;

        [ReadOnly]
        public ComponentTypeHandle<StateMachineInfo> infoType;
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public BufferTypeHandle<StateMachine> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TEscaper escaper = default;

            var states = chunk.GetNativeArray(ref statusType);
            var infos = chunk.GetNativeArray(ref infoType);

            var instanceAccessor = chunk.GetBufferAccessor(ref instanceType);
            DynamicBuffer<StateMachine> instances;
            StateMachineStatus status;
            StateMachineInfo info;
            int length, i, j;
            bool isCreated = false;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out i))
            {
                info = infos[i];
                if (info.systemHandle == systemHandle)
                    continue;

                instances = instanceAccessor[i];
                length = instances.Length;
                for (j = 0; j < length; ++j)
                {
                    if (instances.ElementAt(j).executorSystemHandle == executorSystemHandle)
                        break;
                }

                if (j < length)
                {
                    status = states[i];

                    if(!isCreated)
                    {
                        isCreated = true;

                        if (!factory.Create(unfilteredChunkIndex, chunk, out escaper))
                            break;
                    }

                    if (escaper.Execute(
                        status.value,
                        status.systemHandle,
                        info.systemHandle,
                        systemHandle,
                        i))
                    {
                        //instances.RemoveAt(j);
                        instances.ElementAt(j).executorSystemHandle = SystemHandle.Null;

                        if (length < 2 && status.systemHandle == systemHandle)
                        {
                            status.value = 0;
                            status.systemHandle = SystemHandle.Null;

                            states[i] = status;
                        }
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct StateMachineExecutorJob<TExecutor, TFactory> : IJobChunk
        where TExecutor : struct, IStateMachineExecutor
        where TFactory : struct, IStateMachineFactory<TExecutor>
    {
        public SystemHandle systemHandle;

        public SystemHandle executorSystemHandle;

        public TFactory factory;

        [ReadOnly]
        public ComponentTypeHandle<StateMachineInfo> infoType;
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public BufferTypeHandle<StateMachine> instanceType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TExecutor executor = default;

            var states = chunk.GetNativeArray(ref statusType);
            var infos = chunk.GetNativeArray(ref infoType);

            var instanceAccessor = chunk.GetBufferAccessor(ref instanceType);
            DynamicBuffer<StateMachine> instances;
            StateMachine instance;
            StateMachineStatus status;
            StateMachineInfo info;
            int length, i, j;
            bool isCreated = false;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out i))
            {
                info = infos[i];
                if (info.systemHandle != systemHandle)
                    continue;

                status = states[i];
                if (status.systemHandle != systemHandle)
                {
                    if (info.count < 2)
                        status.systemHandle = systemHandle;
                    else
                        continue;
                }

                instances = instanceAccessor[i];
                length = instances.Length;
                for (j = 0; j < length; ++j)
                {
                    if (instances.ElementAt(j).executorSystemHandle == executorSystemHandle)
                        break;
                }

                if (!isCreated)
                {
                    isCreated = true;

                    if (!factory.Create(unfilteredChunkIndex, chunk, out executor))
                        break;
                }
                
                if (j < length)
                    status.value = executor.Execute(false, i);
                else
                {
                    for (j = 0; j < length; ++j)
                    {
                        if (instances.ElementAt(j).systemHandle == systemHandle)
                            break;
                    }

                    if (j < length)
                        instances.ElementAt(j).executorSystemHandle = executorSystemHandle;
                    else
                    {
                        instance.systemHandle = systemHandle;
                        instance.executorSystemHandle = executorSystemHandle;
                        instances.Add(instance);
                    }

                    status.value = executor.Execute(true, i);
                }

                states[i] = status;
            }
        }
    }
}