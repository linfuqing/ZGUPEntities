using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using ZG.Unsafe;

namespace ZG
{
    [BurstCompile]
    public struct EntityComponentContainerCopyComponentJob<T> : IJobParallelFor where T : unmanaged, IComponentData
    {
        [ReadOnly]
        public EntityComponentContainer<T> container;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<T> values;

        public void Execute(int index)
        {
            container.CopyTo(index, ref values);
        }
    }

    [NativeContainer]
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

                __entities.AddNoResize(entity);
                __values.AddNoResize(value);
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

        internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<EntityComponentContainer<T>>();
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

        public unsafe NativeArray<Entity> entities
        {
            get
            {
                var shadow = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(__data->entities.Ptr, __data->entities.Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref shadow, m_Safety);
#endif
                return shadow;
            }
        }

        public unsafe Entity GetEntity(int index)
        {
            _CheckRead();

            return __data->entities[index];
        }

        public unsafe T GetValue(int index)
        {
            _CheckRead();

            return __data->values[index];
        }

        public unsafe void SetCapacityIfNeed(int value)
        {
            __CheckWrite();

            if(__data->entities.Capacity < value)
                __data->entities.SetCapacity(value);

            if (__data->values.Capacity < value)
                __data->values.SetCapacity(value);
        }

        public unsafe EntityComponentContainer(in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);

            CollectionHelper.SetStaticSafetyId<EntityComponentContainer<T>>(ref m_Safety, ref StaticSafetyID.Data);
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
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
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
            AtomicSafetyHandle.Release(m_Safety);
#endif

            var entities = __data->entities;
            var values = __data->values;
            var allocator = entities.Allocator;

            var jobHandle = JobHandle.CombineDependencies(entities.Dispose(inputDeps), values.Dispose(inputDeps));

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
        public static unsafe void CopyTo<T>(
            this in EntityComponentContainer<T> container, 
            int index, 
            ref ComponentLookup<T> values) where T : unmanaged, IComponentData
        {
            values[container.GetEntity(index)] = container.GetValue(index);
        }

        public static void AddComponentData<T>(
            this ref EntityComponentContainer<T> container, 
            ref SystemState state, 
            int innerloopBatchCount) where T : unmanaged, IComponentData
        {
            if (!container.isEmpty)
            {
                //state.CompleteDependency();
                state.EntityManager.AddComponent<T>(container.entities);

                EntityComponentContainerCopyComponentJob<T> job;
                job.container = container;
                job.values = state.GetComponentLookup<T>();

                var jobHandle = job.ScheduleByRef(container.length, innerloopBatchCount, state.Dependency);

                state.Dependency = jobHandle;
            }
        }
    }
}