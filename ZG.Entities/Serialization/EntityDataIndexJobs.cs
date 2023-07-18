using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;

namespace ZG
{
    [BurstCompile]
    public struct EntityDataIndexComponentInit<TValue, TWrapper> : IJobChunk
        where TValue : unmanaged, IComponentData
        where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
    {
        private struct Executor
        {
            [ReadOnly]
            public NativeArray<Hash128>.ReadOnly inputs;

            [ReadOnly]
            public NativeArray<TValue> instances;

            public SharedHashMap<int, int>.Writer guidIndices;

            public SharedList<Hash128>.Writer outputs;

            public TWrapper wrapper;

            public void Execute(int index)
            {
                var instance = this.instances[index];
                if (wrapper.TryGet(instance, out int temp) && guidIndices.TryAdd(temp, outputs.length))
                    outputs.Add(inputs[temp]);
            }
        }

        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly inputs;

        [ReadOnly]
        public ComponentTypeHandle<TValue> instanceType;

        public SharedHashMap<int, int>.Writer guidIndices;

        public SharedList<Hash128>.Writer outputs;

        public TWrapper wrapper;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Executor executor;
            executor.inputs = inputs;
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.guidIndices = guidIndices;
            executor.outputs = outputs;
            executor.wrapper = wrapper;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
                executor.Execute(i);
        }
    }

    [BurstCompile]
    public struct EntityDataIndexBufferInit<TValue, TWrapper> : IJobChunk
        where TValue : unmanaged, IBufferElementData
        where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
    {
        private struct Executor
        {
            [ReadOnly]
            public NativeArray<Hash128>.ReadOnly inputs;

            [ReadOnly]
            public BufferAccessor<TValue> instances;

            public SharedHashMap<int, int>.Writer guidIndices;

            public SharedList<Hash128>.Writer outputs;

            public TWrapper wrapper;

            public void Execute(int index)
            {
                var instances = this.instances[index];
                int numInstances = instances.Length, temp;
                for (int i = 0; i < numInstances; ++i)
                {
                    if (wrapper.TryGet(instances[i], out temp) && guidIndices.TryAdd(temp, outputs.length))
                        outputs.Add(inputs[temp]);
                }
            }
        }

        [ReadOnly]
        public NativeArray<Hash128>.ReadOnly inputs;

        [ReadOnly]
        public BufferTypeHandle<TValue> instanceType;

        public SharedHashMap<int, int>.Writer guidIndices;

        public SharedList<Hash128>.Writer outputs;

        public TWrapper wrapper;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Executor executor;
            executor.inputs = inputs;
            executor.instances = chunk.GetBufferAccessor(ref instanceType);
            executor.guidIndices = guidIndices;
            executor.outputs = outputs;
            executor.wrapper = wrapper;

            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
                executor.Execute(i);
        }
    }


    public static class EntityDataIndexComponentUtility<TValue, TWrapper>
        where TValue : unmanaged, IComponentData
        where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
    {
        public static JobHandle Schedule(
            in EntityQuery group,
            in NativeArray<Hash128>.ReadOnly inputs,
            in ComponentTypeHandle<TValue> instanceType,
            ref SharedHashMap<int, int>.Writer guidIndices,
            ref SharedList<Hash128>.Writer outputs,
            ref TWrapper wrapper,
            in JobHandle inputDeps)
        {
            EntityDataIndexComponentInit<TValue, TWrapper> init;
            init.inputs = inputs;
            init.instanceType = instanceType;
            init.guidIndices = guidIndices;
            init.outputs = outputs;
            init.wrapper = wrapper;
            return init.ScheduleByRef(group, inputDeps);
        }
    }

    public static class EntityDataIndexBufferUtility<TValue, TWrapper>
        where TValue : unmanaged, IBufferElementData
        where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
    { 
        public static JobHandle Schedule(
            in EntityQuery group,
            in NativeArray<Hash128>.ReadOnly inputs,
            in BufferTypeHandle<TValue> instanceType,
            ref SharedHashMap<int, int>.Writer guidIndices,
            ref SharedList<Hash128>.Writer outputs,
            ref TWrapper wrapper,
            in JobHandle inputDeps)
        {
            EntityDataIndexBufferInit<TValue, TWrapper> init;
            init.inputs = inputs;
            init.instanceType = instanceType;
            init.guidIndices = guidIndices;
            init.outputs = outputs;
            init.wrapper = wrapper;
            return init.ScheduleByRef(group, inputDeps);
        }
    }
}