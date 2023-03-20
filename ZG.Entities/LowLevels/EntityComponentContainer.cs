using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG.Unsafe;

namespace ZG
{
    [BurstCompile]
    public struct EntityComponentContainerMoveComponentJob<T> : IJobParallelFor where T : unmanaged, IComponentData
    {
        [ReadOnly]
        public EntityComponentContainer<T> container;
        [ReadOnly]
        public ComponentLookup<T> values;

        public void Execute(int index)
        {
            container.MoveTo(index, values);
        }
    }

    public struct EntityComponentContainer<T> where T : unmanaged
    {
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
            private UnsafeList<Entity>.ParallelWriter __entities;
            private UnsafeList<T>.ParallelWriter __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            private AtomicSafetyHandle m_Safety;
#endif

            public unsafe ParallelWriter(EntityComponentContainer<T> container)
            {
                __entities = container.__data->entities.AsParallelWriter();
                __values = container.__data->values.AsParallelWriter();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = container.m_Safety;
#endif

            }

        public void AddNoResize(in Entity entity, in T value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

                __entities.AddNoResizeEx(entity);
                __values.AddNoResizeEx(value);
            }
        }

        private struct Data
        {
            public UnsafeList<Entity> entities;
            public UnsafeList<T> values;
        }

        [NativeDisableUnsafePtrRestriction]
        private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public unsafe bool isEmpty
        {
            get
            {
                _CheckRead();

                return __data->entities.IsEmpty;
            }
        }

        public unsafe int length
        {
            get
            {
                _CheckRead();

                UnityEngine.Assertions.Assert.AreEqual(__data->values.Length, __data->entities.Length);

                return Unity.Mathematics.math.min(__data->values.Length, __data->entities.Length);
            }
        }

        public unsafe NativeArray<Entity> entities => __data->entities.ToNativeArray();

        public unsafe NativeArray<T> values => __data->values.ToNativeArray();

        public unsafe void SetCapacityIfNeed(int value)
        {
            __CheckWrite();

            if(__data->entities.Capacity < value)
                __data->entities.SetCapacity(value);

            if (__data->values.Capacity < value)
                __data->values.SetCapacity(value);
        }

        public unsafe EntityComponentContainer(Allocator allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif

            __data = AllocatorManager.Allocate<Data>(allocator);

            __data->entities = new UnsafeList<Entity>(0, allocator);
            __data->values = new UnsafeList<T>(0, allocator);
        }

        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(this);
        }

        public unsafe void Add(in Entity entity, in T value)
        {
            __CheckWrite();

            __data->entities.Add(entity);
            __data->values.Add(value);
        }

        public unsafe void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

            AtomicSafetyHandle.Release(m_Safety);
#endif

            var allocator = __data->entities.Allocator;
            __data->entities.Dispose();
            __data->values.Dispose();

            AllocatorManager.Free(allocator, __data);

            __data = null;
        }

        public unsafe JobHandle Dispose(in JobHandle inputDeps)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);

            AtomicSafetyHandle.Release(m_Safety);
#endif

            var allocator = __data->entities.Allocator;
            var jobHandle = JobHandle.CombineDependencies(__data->entities.Dispose(inputDeps), __data->values.Dispose(inputDeps));

            return Unsafe.CollectionUtility.Dispose(__data, allocator, jobHandle);
        }



        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void _CheckRead()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckWrite()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
        }
    }

    public static class EntityComponentContainerUtility
    {
        public static unsafe void MoveTo<T>(
            this ref EntityComponentContainer<T> container, 
            int index, 
            ComponentLookup<T> values) where T : unmanaged, IComponentData
        {
            container._CheckRead();

            values[container.entities[index]] = container.values[index];
        }

        public static void AddComponentData<T>(
            this ref EntityComponentContainer<T> container, 
            ref SystemState state, 
            int innerloopBatchCount) where T : unmanaged, IComponentData
        {
            JobHandle jobHandle;
            if (container.isEmpty)
                jobHandle = state.Dependency;
            else
            {
                //state.CompleteDependency();
                state.EntityManager.AddComponentBurstCompatible<T>(container.entities);

                EntityComponentContainerMoveComponentJob<T> job;
                job.container = container;
                job.values = state.GetComponentLookup<T>();

                jobHandle = job.Schedule(container.length, innerloopBatchCount, state.Dependency);
            }

            state.Dependency = jobHandle;
        }
    }
}