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
                __assigner.AppendTo(ref instance.__assigner);

                __structChangeCommander.AppendTo(ref instance.__structChangeCommander);

                NativeList<Entity> destroyEntityCommander = instance.__destroyEntityCommander;
                Entity entity;
                int numDestroyEntityCommanders = __destroyEntityCommander.Length;
                for(int i = 0; i < numDestroyEntityCommanders; ++i)
                {
                    entity = __destroyEntityCommander[i];
                    if (destroyEntityCommander.IndexOf(entity) == -1)
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
        }

        private EntityComponentAssigner __assigner;
        private EntityStructChangeCommander __structChangeCommander;
        private NativeListLite<Entity> __destroyEntityCommander;

#if ENABLE_PROFILER
        private ProfilerMarker __setComponentProfilerMarker;
        private ProfilerMarker __destoyEntityProfilerMarker;
#endif

        public JobHandle jobHandle
        {
            get => __assigner.jobHandle;

            set => __assigner.jobHandle = value;
        }

        public Writer writer => new Writer(ref this); 

        public EntityCommander(Allocator allocator)
        {
            __assigner = new EntityComponentAssigner(allocator);
            __structChangeCommander = new EntityStructChangeCommander(allocator);
            __destroyEntityCommander = new NativeListLite<Entity>(allocator);

#if ENABLE_PROFILER
            __setComponentProfilerMarker = new ProfilerMarker("SetComponents");
            __destoyEntityProfilerMarker = new ProfilerMarker("DestroyEntities");
#endif
        }

        public void Dispose()
        {
            __assigner.Dispose();
            __structChangeCommander.Dispose();
            __destroyEntityCommander.Dispose();
        }

        public void CompleteDependency() => __assigner.CompleteDependency();

        public ReadOnly AsReadOnly()
        {
            return new ReadOnly(this);
        }

        public bool IsExists(Entity entity)
        {
            int length = __destroyEntityCommander.Length;
            for (int i = 0; i < length; ++i)
            {
                if (__destroyEntityCommander[i] == entity)
                    return false;
            }

            return true;
        }

        public bool IsAddOrRemoveComponent(in Entity entity, int componentTypeIndex, out bool status) => __structChangeCommander.IsAddOrRemoveComponent(entity, componentTypeIndex, out status);

        public bool TryGetComponentData<T>(in Entity entity, ref T value) where T : struct, IComponentData => __assigner.TryGetComponentData<T>(entity, ref value);

        public bool TryGetBuffer<TValue, TList, TWrapper>(in Entity entity, ref TList list, ref TWrapper wrapper)
            where TValue : struct, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList> => __assigner.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);

        public bool AddComponent<T>(in Entity entity)
        {
            if (__structChangeCommander.AddComponent<T>(entity))
                return true;

            //UnityEngine.Debug.LogWarning($"{entity}Add Component {ComponentType.ReadWrite<T>()} Fail");

            return false;
        }

        public bool RemoveComponent<T>(in Entity entity)
        {
            if (__structChangeCommander.RemoveComponent<T>(entity))
            {
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
            __assigner.SetBuffer(true, entity, values);
        }

        public void SetBuffer<T>(in Entity entity, params T[] values)
                where T : unmanaged, IBufferElementData
        {
            __assigner.SetBuffer(true, entity, values);
        }

        public void SetBuffer<TValue, TCollection>(in Entity entity, in TCollection values)
                where TValue : unmanaged, IBufferElementData
                where TCollection : IReadOnlyCollection<TValue>
        {
            __assigner.SetBuffer<TValue, TCollection>(true, entity, values);
        }

        public void SetComponentEnabled<T>(in Entity entity, bool value) where T : unmanaged, IEnableableComponent
        {
            __assigner.SetComponentEnabled<T>(entity, value);
        }

        public void SetComponentObject<T>(in Entity entity, in EntityObject<T> value)
        {
            __assigner.SetComponentData(entity, value);
        }

        public void AddComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
        {
            AddComponent<T>(entity);

            __assigner.SetComponentData(entity, value);
        }

        public void AddBuffer<T>(in Entity entity, params T[] values) where T : unmanaged, IBufferElementData
        {
            AddComponent<T>(entity);

            __assigner.SetBuffer(true, entity, values);
        }

        public void AddBuffer<TValue, TCollection>(in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            AddComponent<TValue>(entity);

            __assigner.SetBuffer<TValue, TCollection>(true, entity, values);
        }

        public void AppendBuffer<T>(in Entity entity, params T[] values)
            where T : unmanaged, IBufferElementData
        {
            AddComponent<T>(entity);

            __assigner.SetBuffer(false, entity, values);
        }

        public void AppendBuffer<TValue, TCollection>(in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            AddComponent<TValue>(entity);

            __assigner.SetBuffer<TValue, TCollection>(false, entity, values);
        }

        public void AddComponentObject<T>(in Entity entity, in EntityObject<T> value)
        {
            AddComponent<EntityObject<T>>(entity);

            __assigner.SetComponentData(entity, value);
        }

        public void DestroyEntity(Entity entity)
        {
            __destroyEntityCommander.Add(entity);
        }

        public void DestroyEntity(NativeArray<Entity> entities)
        {
            __destroyEntityCommander.AddRange(entities);
        }

        public void Clear()
        {
            __destroyEntityCommander.Clear();
            __structChangeCommander.Clear();

            __assigner.Clear();
        }

        public bool Apply(ref SystemState systemState)
        {
            __structChangeCommander.Apply(ref systemState);

            if (!__destroyEntityCommander.IsEmpty)
            {
#if ENABLE_PROFILER
                using (__destoyEntityProfilerMarker.Auto())
#endif
                {
                    //systemState.CompleteDependency();

                    systemState.EntityManager.DestroyEntity(__destroyEntityCommander.AsArray());
                }
            }

#if ENABLE_PROFILER
            using (__setComponentProfilerMarker.Auto())
#endif
                return __assigner.Apply(ref systemState);
        }

        public void Playback(ref SystemState systemState)
        {
            __structChangeCommander.Playback(ref systemState);

            if (!__destroyEntityCommander.IsEmpty)
            {
#if ENABLE_PROFILER
                using (__destoyEntityProfilerMarker.Auto())
#endif
                {
                    //systemState.CompleteDependency();

                    systemState.EntityManager.DestroyEntity(__destroyEntityCommander.AsArray());
                }

                __destroyEntityCommander.Clear();
            }

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