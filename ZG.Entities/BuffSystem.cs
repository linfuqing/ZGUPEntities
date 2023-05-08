using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using UnityEngine.Jobs;
using ZG;

namespace ZG
{
    public interface IBuff<T, U>
           where T : IComponentData
           where U : struct, IComponentData
    {
        void Add(T x);

        void Subtract(T x);

        void Apply(ref U result, float time);
    }

    public interface IBuff<T>
    {
        void Add(T x);

        void Subtract(T x);
    }

    public struct Buff<T> where T : struct
    {
        public float time;

        public T value;
    }

    [BurstCompile]
    public struct BuffAdd<T, U> : IJob
        where T : unmanaged
        where U : unmanaged, IComponentData, IBuff<T>
    {
        public double time;

        public NativeList<Buff<EntityData<T>>> inputs;

        public ComponentLookup<U> instances;

        public TimeManager<EntityData<T>>.Writer outputs;

        public void Execute()
        {
            Buff<EntityData<T>> buff;
            U instance;
            int length = inputs.Length;
            for (int i = 0; i < length; ++i)
            {
                buff = inputs[i];
                if (instances.HasComponent(buff.value.entity))
                {
                    instance = instances[buff.value.entity];

                    instance.Add(buff.value.value);

                    instances[buff.value.entity] = instance;

                    outputs.Invoke(buff.time + time, buff.value);
                }
            }

            inputs.Clear();
        }
    }

    [BurstCompile]
    public struct BuffSubtract<T, U> : IJob
        where T : unmanaged
        where U : unmanaged, IComponentData, IBuff<T>
    {
        [ReadOnly]
        public NativeArray<EntityData<T>> buffs;

        public ComponentLookup<U> instances;

        public void Execute()
        {
            U instance;
            EntityData<T> buff;
            int length = buffs.Length;
            for (int i = 0; i < length; ++i)
            {
                buff = buffs[i];
                if (instances.HasComponent(buff.entity))
                {
                    instance = instances[buff.entity];

                    instance.Subtract(buff.value);

                    instances[buff.entity] = instance;
                }
            }
        }
    }

    public struct SharedBuffManager<T, U>
        where T : unmanaged
        where U : unmanaged, IComponentData, IBuff<T>
    {
        private NativeArray<JobHandle> __jobHandle;

        private NativeList<Buff<EntityData<T>>> __buffs;
        private TimeManager<EntityData<T>> __timeManager;

        public bool isCreated => __jobHandle.IsCreated;

        public JobHandle jobHandle
        {
            get => __jobHandle[0];

            private set => __jobHandle[0] = value;
        }

        public SharedBuffManager(Allocator allocator)
        {
            BurstUtility.InitializeJob<BuffAdd<T, U>>();
            BurstUtility.InitializeJob<BuffSubtract<T, U>>();

            __jobHandle = new NativeArray<JobHandle>(1, allocator, NativeArrayOptions.ClearMemory);
            __buffs = new NativeList<Buff<EntityData<T>>>(allocator);
            __timeManager = new TimeManager<EntityData<T>>(allocator);
        }

        public void Dispose()
        {
            __jobHandle.Dispose();
            __buffs.Dispose();
            __timeManager.Dispose();
        }

        public unsafe void Set(in EntityData<T> value, float time)
        {
            jobHandle.Complete();
            jobHandle = default;

            Buff<EntityData<T>> buff;
            buff.time = time;
            buff.value = value;

            __buffs.Add(buff);
        }

        public void Update(double time, ref SystemState systemState)
        {
            var instances = systemState.GetComponentLookup<U>();

            BuffAdd<T, U> add;
            add.time = systemState.WorldUnmanaged.Time.ElapsedTime;
            add.inputs = __buffs;
            add.instances = instances;
            add.outputs = __timeManager.writer;
            var jobHandle = add.Schedule(systemState.Dependency);

            this.jobHandle = jobHandle;

            __timeManager.Flush();

            jobHandle = __timeManager.Schedule(time, jobHandle);

            BuffSubtract<T, U> subtract;
            subtract.buffs = __timeManager.values;
            subtract.instances = instances;

            systemState.Dependency = subtract.Schedule(jobHandle);
        }
    }
}