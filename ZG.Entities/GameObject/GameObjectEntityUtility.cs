using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using System.Reflection;
using FNSDK;

namespace ZG
{
    public static partial class GameObjectEntityUtility
    {
        private static EntityCommandSharedSystemGroup __commander = null;

        public static void AddComponent<T>(this IGameObjectEntity gameObjectEntity)
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.AddComponent<T>(entity);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.AddComponent<T>(entity);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void AddComponentData<T>(this IGameObjectEntity gameObjectEntity, T value) where T : struct, IComponentData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.AddComponentData(entity, value);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.AddComponentData(entity, value);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void AddBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.AddBuffer(entity, values);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.AddBuffer(entity, values);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void AppendBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.AppendBuffer(entity, values);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.AppendBuffer<T, T[]>(entity, values);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void AppendBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.AppendBuffer<TValue, TCollection>(entity, values);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.AppendBuffer<TValue, TCollection>(entity, values);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void RemoveComponent<T>(this IGameObjectEntity gameObjectEntity) where T : struct
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.RemoveComponent<T>(entity);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.RemoveComponent<T>(entity);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void SetComponentData<T>(this IGameObjectEntity gameObjectEntity, in T value) where T : struct, IComponentData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.SetComponentData(entity, value);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.SetComponentData(entity, value);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void SetBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.SetBuffer(entity, values);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.SetBuffer(entity, values);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void SetBuffer<T>(this IGameObjectEntity gameObjectEntity, in NativeArray<T> values) where T : struct, IBufferElementData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.SetBuffer(entity, values);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.SetBuffer(entity, values);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void SetBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, in TCollection values)
            where TValue : struct, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.SetBuffer<TValue, TCollection>(entity, values);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.SetBuffer<TValue, TCollection>(entity, values);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        public static void SetComponentEnabled<T>(this IGameObjectEntity gameObjectEntity, bool value)
            where T : unmanaged, IEnableableComponent
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    commandSystem.factory.SetComponentEnabled<T>(entity, value);
                    break;
                case GameObjectEntityStatus.Created:
                    commandSystem.SetComponentEnabled<T>(entity, value);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        /*public static void SetSharedComponentData<T>(this IGameObjectEntity gameObjectEntity, T value) where T : struct, ISharedComponentData
        {
            __GetCommandSystem(gameObjectEntity).SetSharedComponentData(gameObjectEntity.entity, value);
        }
        
        public static void SetComponentObject<T>(this IGameObjectEntity gameObjectEntity, EntityObject<T> value)
        {
            __GetCommandSystem(gameObjectEntity).SetComponentObject(gameObjectEntity.entity, value);
        }*/

        public static bool TryGetComponentData<T>(this IGameObjectEntity gameObjectEntity, out T value) where T : unmanaged, IComponentData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    return __TryGetComponentData(commandSystem, entity, commandSystem.factory, out value);
                case GameObjectEntityStatus.Created:
                    return commandSystem.TryGetComponentData(entity, out value);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static bool TryGetBuffer<T>(this IGameObjectEntity gameObjectEntity, int index, out T value) where T : unmanaged, IBufferElementData
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    return __TryGetBuffer(index, commandSystem, entity, commandSystem.factory, out value);
                case GameObjectEntityStatus.Created:
                    return commandSystem.TryGetBuffer(entity, index, out value, out _);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static bool TryGetBuffer<TValue, TList, TWrapper>(
            this IGameObjectEntity gameObjectEntity,
            ref TList list,
            ref TWrapper wrapper)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    return __TryGetBuffer<TValue, TList, TWrapper, EntityCommandSharedSystemGroup>(
                        commandSystem,
                        entity,
                        commandSystem.factory,
                        ref wrapper,
                        ref list);
                case GameObjectEntityStatus.Created:
                    return commandSystem.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static bool TryGetComponentObject<T>(this IGameObjectEntity gameObjectEntity, out T value)
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    if (__TryGetComponentData(
                        commandSystem,
                        entity,
                        commandSystem.factory,
                        out EntityObject<T> target))
                    {
                        value = target.value;

                        return true;
                    }

                    value = default;

                    return false;
                case GameObjectEntityStatus.Created:
                    return commandSystem.TryGetComponentObject(entity, out value);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static T GetComponentData<T>(this IGameObjectEntity gameObjectEntity) where T : unmanaged, IComponentData
        {
            bool result = TryGetComponentData<T>(gameObjectEntity, out var value);

            UnityEngine.Assertions.Assert.IsTrue(result);

            return value;
        }

        public static T[] GetBuffer<T>(this IGameObjectEntity gameObjectEntity) where T : unmanaged, IBufferElementData
        {
            var list = new NativeList<T>(Allocator.Temp);
            NativeListWriteOnlyWrapper<T> wrapper;
            if (TryGetBuffer<T, NativeList<T>, NativeListWriteOnlyWrapper<T>>(gameObjectEntity, ref list, ref wrapper))
            {
                int length = list.Length;
                if (length > 0)
                {
                    var result = new T[length];
                    for (int i = 0; i < length; ++i)
                        result[i] = list[i];

                    list.Dispose();

                    return result;
                }

                list.Dispose();

                return null;
            }
            list.Dispose();

#if UNITY_ASSERTIONS
            throw new InvalidOperationException();
#else
            return null;
#endif
        }

        public static T GetBuffer<T>(this IGameObjectEntity gameObjectEntity, int index) where T : unmanaged, IBufferElementData
        {
            bool result = TryGetBuffer<T>(gameObjectEntity, index, out var value);

            UnityEngine.Assertions.Assert.IsTrue(result);

            return value;
        }

        public static T GetComponentObject<T>(this IGameObjectEntity gameObjectEntity)
        {
            bool result = TryGetComponentObject(gameObjectEntity, out T value);

            UnityEngine.Assertions.Assert.IsTrue(result);

            return value;
        }

        public static bool HasComponent<T>(this IGameObjectEntity gameObjectEntity)
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    return commandSystem.factory.HasComponent<T>(entity);
                case GameObjectEntityStatus.Created:
                    return commandSystem.HasComponent<T>(entity);
                default:
                    throw new InvalidOperationException();
            }
        }

        private static bool __TryGetComponentData<TValue, TScheduler>(
            in TScheduler entityManager,
            in Entity entity,
            in EntityCommandFactory factory,
            out TValue value)
            where TValue : unmanaged, IComponentData
            where TScheduler : IEntityCommandScheduler
        {
            value = default;

            if (factory.instanceAssigner.TryGetComponentData(entity, ref value))
                return true;

            var instances = factory.instances;
            instances.lookupJobManager.CompleteReadOnlyDependency();

            return instances.reader.TryGetValue(entity, out var instance) ?
                entityManager.TryGetComponentData(instance, out value) :
                factory.HasComponent<TValue>(entity);
        }

        private static bool __TryGetBuffer<TValue, TScheduler>(
            int index,
            in TScheduler scheduler,
            in Entity entity,
            in EntityCommandFactory factory,
            out TValue value)
            where TValue : unmanaged, IBufferElementData
            where TScheduler : IEntityCommandScheduler
        {
            value = default;

            int indexOffset = 0;
            var instances = factory.instances;
            instances.lookupJobManager.CompleteReadOnlyDependency();
            return instances.reader.TryGetValue(entity, out var instance) &&
                scheduler.TryGetBuffer(instance, index, out value, out indexOffset) || 
                factory.instanceAssigner.TryGetBuffer(entity, index, ref value, indexOffset);
        }

        private static bool __TryGetBuffer<TValue, TList, TWrapper, TScheduler>(
            in TScheduler entityManager,
            in Entity entity,
            in EntityCommandFactory factory,
            ref TWrapper wrapper,
            ref TList list)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
            where TScheduler : IEntityCommandScheduler
        {
            if (factory.instanceAssigner.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper))
                return true;

            var instances = factory.instances;
            instances.lookupJobManager.CompleteReadOnlyDependency();

            return instances.reader.TryGetValue(entity, out var instance) ? 
                entityManager.TryGetBuffer<TValue, TList, TWrapper>(instance, ref list, ref wrapper) :
                factory.HasComponent<TValue>(entity);
        }

        private static EntityCommandSharedSystemGroup __GetCommandSystem(IGameObjectEntity gameObjectEntity) => __GetCommandSystem(gameObjectEntity.world);

        private static EntityCommandSharedSystemGroup __GetCommandSystem(World world)
        {
            if (__commander == null || __commander.World != world)
                __commander = world.GetExistingSystemManaged<EntityCommandSharedSystemGroup>();

            return __commander;
        }

    }
}