using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Burst.Intrinsics;

namespace ZG
{
    public interface IStateMachineScheduler
    {
        bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index,
            in Entity entity);
    }

    public interface IStateMachineEscaper
    {
        bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index);
    }

    public interface IStateMachineExecutor
    {
        int Execute(bool isEntry, int index);
    }

    public interface IStateMachineFactory<T> where T : struct
    {
        bool Create(int index, in ArchetypeChunk chunk, out T scheduler);
    }

    public struct StateMachineFactory<T> : IStateMachineFactory<T> where T : struct
    {
        public bool Create(int index, in ArchetypeChunk chunk, out T scheduler)
        {
            scheduler = default;

            return true;
        }
    }

    public struct StateMachineScheduler : IStateMachineScheduler
    {
        public bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index,
            in Entity entity)
        {
            return true;
        }
    }

    public struct StateMachineEscaper : IStateMachineEscaper
    {
        public bool Execute(
            int runningStatus,
            int runningSystemIndex,
            int nextSystemIndex,
            int currentSystemIndex,
            int index)
        {
            return true;
        }
    }

    [BurstCompile]
    public struct StateMachineExit<TStateMachine, TScheduler, TFactory> : IJobChunk, IEntityCommandProducerJob
        where TStateMachine : unmanaged, IStateMachine
        where TScheduler : struct, IStateMachineScheduler
        where TFactory : struct, IStateMachineFactory<TScheduler>
    {
        public int systemIndex;
        public TFactory factory;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public ComponentTypeHandle<StateMachineInfo> infoType;
        [ReadOnly]
        public BufferTypeHandle<TStateMachine> stateMachineType;
        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!factory.Create(unfilteredChunkIndex, chunk, out var scheduler))
                return;

            var entities = chunk.GetNativeArray(entityType);
            var states = chunk.GetNativeArray(ref statusType);
            var infos = chunk.GetNativeArray(ref infoType);

            var stateMachines = chunk.GetBufferAccessor(ref stateMachineType);
            StateMachineStatus status;
            StateMachineInfo info;
            Entity entity;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
            {
                if (stateMachines[i].Length > 0)
                    continue;

                info = infos[i];

                if (info.systemIndex == systemIndex)
                    continue;

                entity = entities[i];

                status = states[i];
                if (!scheduler.Execute(
                    status.value,
                    status.systemIndex,
                    info.systemIndex,
                    systemIndex,
                    i,
                    entity))
                    continue;

                --info.count;

                infos[i] = info;

                entityManager.Enqueue(entity);
            }
        }
    }

    [BurstCompile]
    public struct StateMachineEntry<TScheduler, TFactory> : IJobChunk, IEntityCommandProducerJob
        where TScheduler : struct, IStateMachineScheduler
        where TFactory : struct, IStateMachineFactory<TScheduler>
    {
        public int systemIndex;
        public TFactory factory;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public ComponentTypeHandle<StateMachineInfo> infoType;
        public EntityCommandQueue<Entity>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!factory.Create(unfilteredChunkIndex, chunk, out var scheduler))
                return;

            var entities = chunk.GetNativeArray(entityType);
            var states = chunk.GetNativeArray(ref statusType);
            var infos = chunk.GetNativeArray(ref infoType);

            StateMachineStatus status;
            StateMachineInfo info;
            Entity entity;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                status = states[i];
                info = infos[i];
                if (status.systemIndex < 0 && info.systemIndex >= 0 && info.systemIndex != systemIndex)
                    continue;

                entity = entities[i];

                if (!scheduler.Execute(
                    status.value,
                    status.systemIndex,
                    info.systemIndex,
                    systemIndex,
                    i,
                    entity))
                    continue;

                ++info.count;

                info.systemIndex = systemIndex;
                infos[i] = info;

                entityManager.Enqueue(entity);
            }
        }
    }


    [BurstCompile]
    public struct StateMachineEscaper<TStateMachine, TEscaper, TFactory> : IJobChunk
        where TStateMachine : unmanaged, IStateMachine
        where TEscaper : struct, IStateMachineEscaper
        where TFactory : struct, IStateMachineFactory<TEscaper>
    {
        public int systemIndex;

        public int executorIndex;

        public TFactory factory;

        [ReadOnly]
        public ComponentTypeHandle<StateMachineInfo> infoType;
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public BufferTypeHandle<TStateMachine> stateMachineType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!factory.Create(unfilteredChunkIndex, chunk, out var escaper))
                return;

            var states = chunk.GetNativeArray(ref statusType);
            var infos = chunk.GetNativeArray(ref infoType);

            var stateMachineAccessor = chunk.GetBufferAccessor(ref stateMachineType);
            DynamicBuffer<TStateMachine> stateMachines;
            StateMachineStatus status;
            StateMachineInfo info;
            int length, i, j;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out i))
            {
                info = infos[i];
                if (info.systemIndex == systemIndex)
                    continue;

                stateMachines = stateMachineAccessor[i];
                length = stateMachines.Length;
                for (j = 0; j < length; ++j)
                {
                    if (stateMachines[j].executorIndex == executorIndex)
                        break;
                }

                if (j < length)
                {
                    status = states[i];

                    if (escaper.Execute(
                        status.value,
                        status.systemIndex,
                        info.systemIndex,
                        systemIndex,
                        i))
                    {
                        stateMachines.RemoveAt(j);

                        if (length < 2 && status.systemIndex == systemIndex)
                        {
                            status.value = 0;
                            status.systemIndex = -1;

                            states[i] = status;
                        }
                    }
                }
            }
        }
    }

    [BurstCompile]
    public struct StateMachineExecutor<TStateMachine, TExecutor, TFactory> : IJobChunk
        where TStateMachine : unmanaged, IStateMachine
        where TExecutor : struct, IStateMachineExecutor
        where TFactory : struct, IStateMachineFactory<TExecutor>
    {
        public int systemIndex;

        public int executorIndex;

        public TFactory factory;

        [ReadOnly]
        public ComponentTypeHandle<StateMachineInfo> infoType;
        public ComponentTypeHandle<StateMachineStatus> statusType;
        public BufferTypeHandle<TStateMachine> stateMachineType;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!factory.Create(unfilteredChunkIndex, chunk, out var executor))
                return;

            var states = chunk.GetNativeArray(ref statusType);
            var infos = chunk.GetNativeArray(ref infoType);

            var stateMachineAccessor = chunk.GetBufferAccessor(ref stateMachineType);
            DynamicBuffer<TStateMachine> stateMachines;
            TStateMachine stateMachine = default;
            StateMachineStatus status;
            StateMachineInfo info;
            int length, i, j;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out i))
            {
                info = infos[i];
                if (info.systemIndex != systemIndex)
                    continue;

                status = states[i];
                if (status.systemIndex != systemIndex)
                {
                    if (info.count < 2)
                        status.systemIndex = systemIndex;
                    else
                        continue;
                }

                stateMachines = stateMachineAccessor[i];
                length = stateMachines.Length;
                for (j = 0; j < length; ++j)
                {
                    if (stateMachines[j].executorIndex == executorIndex)
                        break;
                }

                if (j < length)
                    status.value = executor.Execute(false, i);
                else
                {
                    stateMachine.executorIndex = executorIndex;
                    stateMachines.Add(stateMachine);

                    status.value = executor.Execute(true, i);
                }

                states[i] = status;
            }
        }
    }
}