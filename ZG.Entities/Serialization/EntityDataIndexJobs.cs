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
            public NativeArray<Hash128> inputs;

            [ReadOnly]
            public NativeArray<TValue> instances;

            public NativeParallelHashMap<int, int> indices;

            public NativeList<Hash128> outputs;

            public TWrapper wrapper;

            public void Execute(int index)
            {
                var instance = this.instances[index];
                if (wrapper.TryGet(instance, out int temp) && indices.TryAdd(temp, outputs.Length))
                    outputs.Add(inputs[temp]);
            }
        }

        [ReadOnly]
        public NativeArray<Hash128> inputs;

        [ReadOnly]
        public ComponentTypeHandle<TValue> instanceType;

        public NativeParallelHashMap<int, int> indices;

        public NativeList<Hash128> outputs;

        public TWrapper wrapper;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Executor executor;
            executor.inputs = inputs;
            executor.instances = chunk.GetNativeArray(ref instanceType);
            executor.indices = indices;
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
            public NativeArray<Hash128> inputs;

            [ReadOnly]
            public BufferAccessor<TValue> instances;

            public NativeParallelHashMap<int, int> indices;

            public NativeList<Hash128> outputs;

            public TWrapper wrapper;

            public void Execute(int index)
            {
                var instances = this.instances[index];
                int numInstances = instances.Length, temp;
                for (int i = 0; i < numInstances; ++i)
                {
                    if (wrapper.TryGet(instances[i], out temp) && indices.TryAdd(temp, outputs.Length))
                        outputs.Add(inputs[temp]);
                }
            }
        }

        [ReadOnly]
        public NativeArray<Hash128> inputs;

        [ReadOnly]
        public BufferTypeHandle<TValue> instanceType;

        public NativeParallelHashMap<int, int> indices;

        public NativeList<Hash128> outputs;

        public TWrapper wrapper;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Executor executor;
            executor.inputs = inputs;
            executor.instances = chunk.GetBufferAccessor(ref instanceType);
            executor.indices = indices;
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
            in NativeArray<Hash128> inputs,
            in ComponentTypeHandle<TValue> instanceType,
            ref NativeParallelHashMap<int, int> indices,
            ref NativeList<Hash128> outputs,
            ref TWrapper wrapper,
            in JobHandle inputDeps)
        {
            EntityDataIndexComponentInit<TValue, TWrapper> init;
            init.inputs = inputs;
            init.instanceType = instanceType;
            init.indices = indices;
            init.outputs = outputs;
            init.wrapper = wrapper;
            return init.Schedule(group, inputDeps);
        }
    }

    public static class EntityDataIndexBufferUtility<TValue, TWrapper>
        where TValue : unmanaged, IBufferElementData
        where TWrapper : struct, IEntityDataIndexReadOnlyWrapper<TValue>
    {
        public static JobHandle Schedule(
           in EntityQuery group,
           in NativeArray<Hash128> inputs,
           in BufferTypeHandle<TValue> instanceType,
           ref NativeParallelHashMap<int, int> indices,
           ref NativeList<Hash128> outputs,
           ref TWrapper wrapper,
           in JobHandle inputDeps)
        {
            EntityDataIndexBufferInit<TValue, TWrapper> init;
            init.inputs = inputs;
            init.instanceType = instanceType;
            init.indices = indices;
            init.outputs = outputs;
            init.wrapper = wrapper;
            return init.Schedule(group, inputDeps);
        }
    }
}