﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Profiling;
using ZG;

[assembly: RegisterGenericJobType(typeof(ClearHashMap<ComponentType, JobHandle>))]

namespace ZG
{
    public interface IEntityCommandScheduler
    {
        EntityCommander commander { get; }

        EntityManager entityManager { get; }
    }

    [BurstCompile, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true)]
    public partial struct EntityCommandFactorySystem : ISystem
    {
        public EntityCommandFactory factory
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            /*__prefabGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<EntityObjects>(), 
                    //ComponentType.ReadOnly<Prefab>()
                },
                Options = EntityQueryOptions.IncludePrefab// | EntityQueryOptions.IncludeDisabled
            });

            __instanceGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<EntityObjects>(),
                },
                //Options = EntityQueryOptions.IncludeDisabled
            });*/

            factory = new EntityCommandFactory(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            factory.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //factory.Playback(ref this.GetState(), __prefabGroup, __instanceGroup);

            this.factory.Playback(ref state);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderLast = true)]
    public partial struct EntityCommanderSystem : ISystem
    {
        public EntityCommander value
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            /*__group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<EntityObjects>()
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
                });*/

            value = new EntityCommander(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            value.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            value.Playback(ref state);
        }
    }

    [UpdateInGroup(typeof(ManagedSystemGroup)), 
        CreateAfter(typeof(EntityCommandFactorySystem)),
        CreateAfter(typeof(EntityCommanderSystem))]
        //CreateAfter(typeof(EntitySharedComponentCommanderSystem))]
    public partial class EntityCommandSharedSystemGroup : ComponentSystemGroup, IEntityCommandScheduler
    {
        public EntityCommandFactory factory
        {
            get;

            private set;
        }

        public EntityCommander commander
        {
            get;

            private set;
        }

        public EntityCommandSharedSystemGroup()
        {
            //UseLegacySortOrder = false;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            World world = World;
            factory = world.GetExistingSystemUnmanaged<EntityCommandFactorySystem>().factory;
            commander = world.GetExistingSystemUnmanaged<EntityCommanderSystem>().value;
        }

        EntityManager IEntityCommandScheduler.entityManager => EntityManager;
    }

    [CreateAfter(typeof(EntityCommandSharedSystemGroup)), UpdateInGroup(typeof(BeginFrameEntityCommandSystemGroup))]
    public partial class BeginFrameEntityCommandSystem : SystemBase, IEntityCommandScheduler
    {
        private EntityCommandSharedSystemGroup __sharedSystemGroup;

        public EntityCommander commander => __sharedSystemGroup.commander;

        protected override void OnCreate()
        {
            base.OnCreate();

            __sharedSystemGroup = World.GetExistingSystemManaged<EntityCommandSharedSystemGroup>();
        }

        protected override sealed void OnUpdate()
        {
            __sharedSystemGroup.Update();
        }

        EntityManager IEntityCommandScheduler.entityManager => EntityManager;
    }

    public static partial class EntityCommandUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(this IEntityCommandScheduler scheduler, Entity entity) => scheduler.commander.DestroyEntity(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(this IEntityCommandScheduler scheduler, NativeArray<Entity> entities) => scheduler.commander.DestroyEntity(entities);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AddComponent<T>(this IEntityCommandScheduler scheduler, in Entity entity)
        {
            return scheduler.commander.AddComponent<T>(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RemoveComponent<T>(this IEntityCommandScheduler scheduler, in Entity entity)
        {
            return scheduler.commander.RemoveComponent<T>(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, IComponentData => scheduler.commander.SetComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBuffer<T>(this IEntityCommandScheduler scheduler, in Entity entity, in NativeArray<T> values) where T : struct, IBufferElementData => scheduler.commander.SetBuffer(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBuffer<T>(this IEntityCommandScheduler scheduler, in Entity entity, params T[] values) where T : unmanaged, IBufferElementData => scheduler.commander.SetBuffer(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBuffer<TValue, TCollection>(this IEntityCommandScheduler scheduler, in Entity entity, in TCollection values) 
            where TValue : struct, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
            => scheduler.commander.SetBuffer<TValue, TCollection>(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetComponentEnabled<T>(this IEntityCommandScheduler scheduler, in Entity entity, bool value)
            where T : unmanaged, IEnableableComponent
            => scheduler.commander.SetComponentEnabled<T>(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetSharedComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, ISharedComponentData => scheduler.commander.SetSharedComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetComponentObject<T>(this IEntityCommandScheduler scheduler, in Entity entity, in EntityObject<T> value) => scheduler.commander.SetComponentObject(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, IComponentData => scheduler.commander.AddComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddBuffer<T>(this IEntityCommandScheduler scheduler, in Entity entity, params T[] values) where T : unmanaged, IBufferElementData => scheduler.commander.AddBuffer(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddBuffer<TValue, TCollection>(this IEntityCommandScheduler scheduler, in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue> => scheduler.commander.AddBuffer<TValue, TCollection>(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSharedComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, ISharedComponentData => scheduler.commander.AddSharedComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddComponentObject<T>(this IEntityCommandScheduler scheduler, in Entity entity, in EntityObject<T> value) => scheduler.commander.AddComponentObject(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AppendBuffer<TValue, TCollection>(this IEntityCommandScheduler scheduler, in Entity entity, TCollection values) 
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue> => scheduler.commander.AppendBuffer<TValue, TCollection>(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AppendBufferUnique<TValue, TCollection>(this IEntityCommandScheduler scheduler, in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue> => scheduler.commander.AppendBufferUnique<TValue, TCollection>(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveBufferElementSwapBack<TValue, TCollection>(this IEntityCommandScheduler scheduler, in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue> => scheduler.commander.RemoveBufferElementSwapBack<TValue, TCollection>(entity, values);

        public static bool TryGetComponentData<TValue>(this IEntityCommandScheduler scheduler, in Entity entity, ref TValue value, bool isOverride = false) 
            where TValue : unmanaged, IComponentData
        {
            var entityManager = scheduler.entityManager;
            if (entityManager.HasComponent<TValue>(entity))
            {
                if(!isOverride)
                    value = entityManager.GetComponentData<TValue>(entity);

                scheduler.commander.TryGetComponentData(entity, ref value);

                return true;
            }
            else
            {
                value = default;

                if (scheduler.commander.TryGetComponentData(entity, ref value))
                    return true;
            }

            return false;
        }

        public static bool TryGetSharedComponentData<TValue>(this IEntityCommandScheduler scheduler, in Entity entity, out TValue value)
            where TValue : struct, ISharedComponentData
        {
            var entityManager = scheduler.entityManager;
            if (entityManager.HasComponent<TValue>(entity))
            {
                value = entityManager.GetSharedComponentManaged<TValue>(entity);

                scheduler.commander.TryGetSharedComponentData(entity, ref value);

                return true;
            }
            else
            {
                value = default;

                if (scheduler.commander.TryGetSharedComponentData(entity, ref value))
                    return true;
            }

            return false;
        }

        public static bool TryGetComponentObject<TValue>(this IEntityCommandScheduler scheduler, in Entity entity, out TValue value)
        {
            EntityObject<TValue> result = default;
            if (TryGetComponentData(scheduler, entity, ref result))
            {
                value = result.value;

                return true;
            }

            value = default;

            return false;
        }

        public static bool TryGetBuffer<TValue, TList, TWrapper>(
            this IEntityCommandScheduler scheduler,
            in Entity entity,
            ref TList list,
            ref TWrapper wrapper, 
            bool isOverride = false)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            var entityManager = scheduler.entityManager;
            if (entityManager.HasComponent<TValue>(entity))
            {
                if (!isOverride)
                {
                    var buffer = entityManager.GetBuffer<TValue>(entity, true);
                    int length = buffer.Length;
                    wrapper.SetCount(ref list, length);
                    for (int i = 0; i < length; ++i)
                        wrapper.Set(ref list, buffer[i], i);
                }

                scheduler.commander.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);

                return true;
            }

            return scheduler.commander.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);
        }

        public static bool TryGetBuffer<T>(
            this IEntityCommandScheduler scheduler, 
            in Entity entity, 
            int index, 
            ref T value, 
            int indexOffset = 0)
            where T : unmanaged, IBufferElementData
        {
            bool result = false;
            if (indexOffset == 0)
            {
                var entityManager = scheduler.entityManager;
                if (entityManager.HasComponent<T>(entity))
                {
                    var buffer = entityManager.GetBuffer<T>(entity, true);
                    indexOffset = buffer.Length;

                    result = indexOffset > index;

                    value = result ? buffer[index] : default;
                }
            }

            return scheduler.commander.TryGetBuffer(entity, index, ref value, indexOffset) || result;
        }

        public static bool IsComponentEnabled(this IEntityCommandScheduler scheduler, in Entity entity, in ComponentType componentType, out bool isOverride)
        {
            var commander = scheduler.commander;
            if (commander.IsComponentEnabled(entity, componentType.TypeIndex, out isOverride))
                return true;

            return scheduler.entityManager.IsComponentEnabled(entity, componentType);
        }

        public static bool IsComponentEnabled<T>(this IEntityCommandScheduler scheduler, in Entity entity, out bool isOverride) where T :　IEnableableComponent => IsComponentEnabled(scheduler, entity, ComponentType.ReadWrite<T>(), out isOverride);

        public static bool HasComponent(this IEntityCommandScheduler scheduler, in Entity entity, in ComponentType componentType, out bool isOverride)
        {
            isOverride = true;

            var commander = scheduler.commander;
            if (!commander.IsExists(entity))
                return true;

            bool status;
            if (commander.IsAddOrRemoveComponent(entity, componentType.TypeIndex, out status))
                return status;

            isOverride = false;

            return scheduler.entityManager.HasComponent(entity, componentType);
        }

        public static bool HasComponent<T>(this IEntityCommandScheduler scheduler, in Entity entity, out bool isOverride) => HasComponent(scheduler, entity, ComponentType.ReadWrite<T>(), out isOverride);

        /*public static void CompleteAll(this ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps)
        {
            inputDeps.Complete();

            if (dependency.IsEmpty)
                return;

            using (var jobHandles = dependency.GetValueArray(Allocator.Temp))
                JobHandle.CompleteAll(jobHandles);

            dependency.Clear();
        }*/
    }
}