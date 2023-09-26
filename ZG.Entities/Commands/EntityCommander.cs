using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace ZG
{
    public struct EntityCommander : IDisposable
    {
        public struct ReadOnly
        {
            [ReadOnly]
            private EntityComponentAssigner.ReadOnly __assigner;
            [ReadOnly]
            private EntityStructChangeCommander.ReadOnly __structChangeCommander;
            [ReadOnly]
            private NativeList<Entity> __destroyEntityCommander;

            internal ReadOnly(in EntityCommander commander)
            {
                __assigner = commander.__assigner.AsReadOnly();
                __structChangeCommander = commander.__structChangeCommander.AsReadOnly();
                __destroyEntityCommander = commander.__destroyEntityCommander;
            }

            public void AppendTo(ref EntityCommander instance)
            {
                var writer = instance.__assigner.writer;
                __assigner.AppendTo(ref writer);

                __structChangeCommander.AppendTo(ref instance.__structChangeCommander);

                var destroyEntityCommander = instance.__destroyEntityCommander;
                Entity entity;
                int numDestroyEntityCommanders = __destroyEntityCommander.Length;
                for(int i = 0; i < numDestroyEntityCommanders; ++i)
                {
                    entity = __destroyEntityCommander[i];
                    if (!destroyEntityCommander.AsArray().Contains(entity))
                        destroyEntityCommander.Add(entity);
                }
            }
        }

        public struct Writer
        {
            private EntityComponentAssigner.Writer __assigner;
            private EntityStructChangeCommander.Writer __structChangeCommander;
            private NativeList<Entity> __destroyEntityCommander;

            internal Writer(ref EntityCommander commander)
            {
                __assigner = commander.__assigner.writer;
                __structChangeCommander = commander.__structChangeCommander.writer;
                __destroyEntityCommander = commander.__destroyEntityCommander;
            }

            public void Clear()
            {
                __assigner.Clear();
                __structChangeCommander.Clear();
                __destroyEntityCommander.Clear();
            }

            public void DestroyEntity(in Entity entity)
            {
                __destroyEntityCommander.Add(entity);
            }

            public bool RemoveComponent<T>(in Entity entity)
            {
                return __structChangeCommander.RemoveComponent<T>(entity);
            }

            public bool AddComponent<T>(in Entity entity)
            {
                return __structChangeCommander.AddComponent<T>(entity);
            }

            public void AddComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
            {
                AddComponent<T>(entity);

                __assigner.SetComponentData(entity, value);
            }
        }

        public struct ParallelWriter
        {
            private EntityComponentAssigner.ParallelWriter __assigner;
            private EntityStructChangeCommander.ParallelWriter __structChangeCommander;
            private NativeList<Entity>.ParallelWriter __destroyEntityCommander;

            internal ParallelWriter(ref EntityCommander commander, int bufferSize, int typeCount, int entityCount)
            {
                __assigner = commander.__assigner.AsParallelWriter(bufferSize, typeCount);
                __structChangeCommander = commander.__structChangeCommander.AsParallelWriter(typeCount);

                commander.__destroyEntityCommander.Capacity = math.max(commander.__destroyEntityCommander.Capacity, commander.__destroyEntityCommander.Length + entityCount);

                __destroyEntityCommander = commander.__destroyEntityCommander.AsParallelWriter();
            }

            public void DestroyEntity(in Entity entity)
            {
                __destroyEntityCommander.AddNoResize(entity);
            }

            public bool RemoveComponent<T>(in Entity entity)
            {
                return __structChangeCommander.RemoveComponent<T>(entity);
            }

            public bool AddComponent<T>(in Entity entity)
            {
                return __structChangeCommander.AddComponent<T>(entity);
            }

            public void AddComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
            {
                AddComponent<T>(entity);

                __assigner.SetComponentData(entity, value);
            }
        }

        private EntityComponentAssigner __assigner;
        private EntitySharedComponentAssigner __sharedComponentAssigner;
        private EntityStructChangeCommander __structChangeCommander;
        private NativeList<Entity> __destroyEntityCommander;

#if ENABLE_PROFILER
        private ProfilerMarker __setComponentProfilerMarker;
        private ProfilerMarker __setSharedComponentProfilerMarker;
        private ProfilerMarker __destoyEntityProfilerMarker;
#endif

        public JobHandle jobHandle
        {
            get => __assigner.jobHandle;

            set => __assigner.jobHandle = value;
        }

        public Writer writer => new Writer(ref this);

        public NativeArray<Entity> destroiedEntities => __destroyEntityCommander.AsDeferredJobArray();

        public EntityCommander(Allocator allocator)
        {
            __assigner = new EntityComponentAssigner(allocator);
            __sharedComponentAssigner = new EntitySharedComponentAssigner(allocator);
            __structChangeCommander = new EntityStructChangeCommander(allocator);
            __destroyEntityCommander = new NativeList<Entity>(allocator);

#if ENABLE_PROFILER
            __setComponentProfilerMarker = new ProfilerMarker("SetComponents");
            __setSharedComponentProfilerMarker = new ProfilerMarker("SetSharedComponents");
            __destoyEntityProfilerMarker = new ProfilerMarker("DestroyEntities");
#endif
        }

        public void Dispose()
        {
            __assigner.Dispose();
            __sharedComponentAssigner.Dispose();
            __structChangeCommander.Dispose();
            __destroyEntityCommander.Dispose();
        }

        public void CompleteDependency() => __assigner.CompleteDependency();

        public ReadOnly AsReadOnly()
        {
            return new ReadOnly(this);
        }

        public ParallelWriter AsParallelWriter(int bufferSize, int typeCount, int entityCount)
        {
            return new ParallelWriter(ref this, bufferSize, typeCount, entityCount);
        }

        public bool IsExists(Entity entity)
        {
            CompleteDependency();

            int length = __destroyEntityCommander.Length;
            for (int i = 0; i < length; ++i)
            {
                if (__destroyEntityCommander[i] == entity)
                    return false;
            }

            return true;
        }

        public bool IsAddOrRemoveComponent(in Entity entity, TypeIndex componentTypeIndex, out bool status)
        {
            CompleteDependency();

            return __structChangeCommander.IsAddOrRemoveComponent(entity, componentTypeIndex, out status);
        }

        public bool TryGetComponentData<T>(in Entity entity, ref T value) where T : struct, IComponentData
        {
            return __assigner.TryGetComponentData(entity, ref value) || __structChangeCommander.HasComponent<T>(entity);
        }

        public bool TryGetBuffer<T>(in Entity entity, int index, ref T value, int indexOffset = 0) where T : struct, IBufferElementData
        {
            return __assigner.TryGetBuffer(entity, index, ref value, indexOffset);
        }

        public bool TryGetBuffer<TValue, TList, TWrapper>(in Entity entity, ref TList list, ref TWrapper wrapper)
            where TValue : struct, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            return __assigner.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper) || __structChangeCommander.HasComponent<TValue>(entity);
        }

        public bool TryGetSharedComponentData<T>(in Entity entity, ref T value) where T : struct, ISharedComponentData
        {
            return __sharedComponentAssigner.TryGetSharedComponentData(entity, ref value);
        }

        public bool AddComponent<T>(in Entity entity)
        {
            CompleteDependency();

            if (__structChangeCommander.AddComponent<T>(entity))
                return true;

            //UnityEngine.Debug.LogWarning($"{entity}Add Component {ComponentType.ReadWrite<T>()} Fail");

            return false;
        }

        public bool RemoveComponent<T>(in Entity entity)
        {
            CompleteDependency();

            if (__structChangeCommander.RemoveComponent<T>(entity))
            {
                __sharedComponentAssigner.RemoveComponent<T>(entity);
                __assigner.RemoveComponent<T>(entity);

                return true;
            }

            //UnityEngine.Debug.LogWarning($"{entity}Remove Component {ComponentType.ReadWrite<T>()} Fail");

            return false;
        }

        public void SetComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
        {
            __assigner.SetComponentData(entity, value);
        }

        public void SetBuffer<T>(in Entity entity, in NativeArray<T> values)
                where T : struct, IBufferElementData
        {
            __assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, values);
        }

        public void SetBuffer<T>(in Entity entity, params T[] values)
                where T : unmanaged, IBufferElementData
        {
            __assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, values);
        }

        public void SetBuffer<TValue, TCollection>(in Entity entity, in TCollection values)
                where TValue : struct, IBufferElementData
                where TCollection : IReadOnlyCollection<TValue>
        {
            __assigner.SetBuffer<TValue, TCollection>(EntityComponentAssigner.BufferOption.Override, entity, values);
        }

        public void SetComponentEnabled<T>(in Entity entity, bool value) where T : struct, IEnableableComponent
        {
            __assigner.SetComponentEnabled<T>(entity, value);
        }

        public void SetComponentObject<T>(in Entity entity, in EntityObject<T> value)
        {
            __assigner.SetComponentData(entity, value);
        }

        public void SetSharedComponentData<T>(in Entity entity, in T value) where T : struct, ISharedComponentData
        {
            __sharedComponentAssigner.SetSharedComponentData(entity, value);
        }

        public void AddComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
        {
            AddComponent<T>(entity);

            __assigner.SetComponentData(entity, value);
        }

        public void AddBuffer<T>(in Entity entity, params T[] values) where T : unmanaged, IBufferElementData
        {
            AddComponent<T>(entity);

            __assigner.SetBuffer(EntityComponentAssigner.BufferOption.Override, entity, values);
        }

        public void AddBuffer<TValue, TCollection>(in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            AddComponent<TValue>(entity);

            __assigner.SetBuffer<TValue, TCollection>(EntityComponentAssigner.BufferOption.Override, entity, values);
        }

        public void AppendBuffer<T>(in Entity entity, params T[] values)
            where T : unmanaged, IBufferElementData
        {
            AddComponent<T>(entity);

            __assigner.SetBuffer(EntityComponentAssigner.BufferOption.Append, entity, values);
        }

        public void AppendBuffer<TValue, TCollection>(in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            AddComponent<TValue>(entity);

            __assigner.SetBuffer<TValue, TCollection>(EntityComponentAssigner.BufferOption.Append, entity, values);
        }

        public void AppendBufferUnique<TValue, TCollection>(in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            AddComponent<TValue>(entity);

            __assigner.SetBuffer<TValue, TCollection>(EntityComponentAssigner.BufferOption.AppendUnique, entity, values);
        }

        public void RemoveBufferElementSwapBack<TValue, TCollection>(in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            __assigner.SetBuffer<TValue, TCollection>(EntityComponentAssigner.BufferOption.RemoveSwapBack, entity, values);
        }

        public void AddComponentObject<T>(in Entity entity, in EntityObject<T> value)
        {
            AddComponent<EntityObject<T>>(entity);

            __assigner.SetComponentData(entity, value);
        }

        public void AddSharedComponentData<T>(in Entity entity, in T value) where T : struct, ISharedComponentData
        {
            AddComponent<T>(entity);

            __sharedComponentAssigner.SetSharedComponentData(entity, value);
        }

        public void DestroyEntity(in Entity entity)
        {
            CompleteDependency();

            __destroyEntityCommander.Add(entity);
        }

        public void DestroyEntity(in NativeArray<Entity> entities)
        {
            CompleteDependency();

            __destroyEntityCommander.AddRange(entities);
        }

        public void Clear()
        {
            __assigner.Clear();

            __sharedComponentAssigner.Clear();

            __destroyEntityCommander.Clear();
            __structChangeCommander.Clear();
        }

        public bool Apply(ref SystemState systemState)
        {
            CompleteDependency();

            bool result = __structChangeCommander.Apply(ref systemState);

            var entityManager = systemState.EntityManager;
            if (!__destroyEntityCommander.IsEmpty)
            {
                result = true;

#if ENABLE_PROFILER
                using (__destoyEntityProfilerMarker.Auto())
#endif
                    entityManager.DestroyEntity(__destroyEntityCommander.AsArray());
            }

#if ENABLE_PROFILER
            using (__setSharedComponentProfilerMarker.Auto())
#endif
                result = __sharedComponentAssigner.Apply(ref entityManager) || result;

#if ENABLE_PROFILER
            using (__setComponentProfilerMarker.Auto())
#endif
                return __assigner.Apply(ref systemState) || result;
        }

        public void Playback(ref SystemState systemState)
        {
            CompleteDependency();

            __structChangeCommander.Playback(ref systemState);

            var entityManager = systemState.EntityManager;
            if (!__destroyEntityCommander.IsEmpty)
            {
#if ENABLE_PROFILER
                using (__destoyEntityProfilerMarker.Auto())
#endif
                {
                    //systemState.CompleteDependency();

                    entityManager.DestroyEntity(__destroyEntityCommander.AsArray());
                }

                __destroyEntityCommander.Clear();
            }

#if ENABLE_PROFILER
            using (__setSharedComponentProfilerMarker.Auto())
#endif
                __sharedComponentAssigner.Playback(ref entityManager);

#if ENABLE_PROFILER
            using (__setComponentProfilerMarker.Auto())
#endif
                __assigner.Playback(ref systemState);
        }

        public long GetHashCode64()
        {
            return __assigner.GetHashCode();
        }
    }
}