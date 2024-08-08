using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using System.Reflection;

namespace ZG
{
    public static partial class GameObjectEntityUtility
    {
        private static EntityCommandSharedSystemGroup __commander = null;

        public static void BakeToEntity(
            this Transform transform, 
            string worldName, 
            ref EntityCommandFactory factory, 
            ref NativeList<Entity> entities, 
            in Entity parent = default, 
            params ComponentType[] componentTypes)
        {
            Entity entity = Entity.Null;
            
            if (parent != Entity.Null)
            {
                int numComponentTypes = componentTypes == null ? 0 : componentTypes.Length;
                Array.Resize(ref componentTypes, numComponentTypes + 1);

                componentTypes[numComponentTypes] = ComponentType.ReadOnly<EntityParent>();
            }

            var entityComponentRoot = transform.GetComponent<IEntityComponentRoot>();
            if (entityComponentRoot == null || entityComponentRoot.worldName != worldName)
            {
                using (var definition = new GameObjectEntityDefinition())
                using (var instance = new GameObjectEntityInstance())
                {
                    instance.BuildArchetype(false, worldName, transform, definition, componentTypes);
                    
                    if (!factory.isCreated)
                        factory = __GetCommandSystem(instance.world).factory;

                    instance.CreateEntity(definition, ref entity, ref factory, out _);
                }
            }
            else
                entityComponentRoot.CreateEntity(ref entity, ref factory, out _, componentTypes);

            entities.Add(entity);

            if (parent != Entity.Null)
            {
                EntityParent entityParent;
                entityParent.entity = parent;
                factory.instanceAssigner.SetBuffer(EntityComponentAssigner.BufferOption.AppendUnique, entity,
                    entityParent);
            }
            
            var roots = new List<IEntityComponentRoot>();
            foreach (Transform child in transform)
            {
                roots.Clear();
                child.GetComponentsInChildren<IEntityComponentRoot>(
                    true, 
                    roots.Add,
                    typeof(IEntityComponentRoot));

                foreach (var root in roots)
                    BakeToEntity(
                        ((Component)root).transform, 
                        worldName, 
                        ref factory, 
                        ref entities, 
                        entity, 
                        componentTypes);
            }
        }

        public static void AddComponent<T>(this IGameObjectEntity gameObjectEntity)
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.AddComponent<T>(entity);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.AddComponent<T>(entity);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void AddComponentData<TGameObjectEntity, TValue>(this TGameObjectEntity gameObjectEntity, in TValue value)
            where TGameObjectEntity : IGameObjectEntity
            where TValue : struct, IComponentData
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.AddComponentData(entity, value);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.AddComponentData(entity, value);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void AddBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.AddBuffer(entity, values); 
                                
                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.AddBuffer(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void AddBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.AddBuffer<TValue, TCollection>(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.AddBuffer<TValue, TCollection>(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void AppendBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.AppendBuffer(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.AppendBuffer<T, T[]>(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void AppendBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.AppendBuffer<TValue, TCollection>(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.AppendBuffer<TValue, TCollection>(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void AppendBufferUnique<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.AppendBufferUnique<TValue, TCollection>(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.AppendBufferUnique<TValue, TCollection>(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void AppendBufferUnique<T>(this IGameObjectEntity gameObjectEntity, params T[] values)
            where T : unmanaged, IBufferElementData => AppendBufferUnique<T, T[]>(gameObjectEntity, values);

        public static void RemoveBufferElementSwapBack<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.RemoveBufferElementSwapBack<TValue, TCollection>(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.RemoveBufferElementSwapBack<TValue, TCollection>(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void RemoveBufferElementSwapBack<T>(this IGameObjectEntity gameObjectEntity, params T[] values)
            where T : unmanaged, IBufferElementData =>
            RemoveBufferElementSwapBack<T, T[]>(gameObjectEntity, values);

        public static void RemoveComponent<T>(this IGameObjectEntity gameObjectEntity) where T : struct
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.RemoveComponent<T>(entity);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.RemoveComponent<T>(entity);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void SetComponentData<TGameObjectEntity, TValue>(this TGameObjectEntity gameObjectEntity, in TValue value)
            where TGameObjectEntity : IGameObjectEntity
            where TValue : struct, IComponentData
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.SetComponentData(entity, value);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.SetComponentData(entity, value);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void SetBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values)
            where T : unmanaged, IBufferElementData
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.SetBuffer(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.SetBuffer(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void SetBuffer<TGameObjectEntity, TValue>(this TGameObjectEntity gameObjectEntity, in NativeArray<TValue> values) 
            where TGameObjectEntity : IGameObjectEntity
            where TValue : struct, IBufferElementData
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.SetBuffer(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.SetBuffer(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void SetBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, in TCollection values)
            where TValue : struct, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.SetBuffer<TValue, TCollection>(entity, values);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.SetBuffer<TValue, TCollection>(entity, values);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        public static void SetComponentEnabled<T>(this IGameObjectEntity gameObjectEntity, bool value)
            where T : unmanaged, IEnableableComponent
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                var status = gameObjectEntity.status;
                switch (status)
                {
                    case GameObjectEntityStatus.Creating:
                    case GameObjectEntityStatus.Created:
                        if (status == GameObjectEntityStatus.Creating)
                        {
                            var factory = commandSystem.factory;
                            var instance = factory.GetEntity(entity, true);
                            if (instance == Entity.Null)
                            {
                                factory.SetComponentEnabled<T>(entity, value);

                                break;
                            }

                            entity = instance;
                        }

                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        commandSystem.SetComponentEnabled<T>(entity, value);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
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

        public static bool TryGetComponentData<TGameObjectEntity, TValue>(this TGameObjectEntity gameObjectEntity, out TValue value)
            where TGameObjectEntity : IGameObjectEntity
            where TValue : unmanaged, IComponentData
        {
            //UnityEngine.Profiling.Profiler.BeginSample("TryGetComponentData");
            value = default;

            bool result = false;
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;
                switch (gameObjectEntity.status)
                {
                    case GameObjectEntityStatus.Creating:
                        result = __TryGetComponentData(commandSystem, commandSystem.factory, entity, out value);
                        break;
                    case GameObjectEntityStatus.Created:
                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        value = default;
                        result = commandSystem.TryGetComponentData(entity, ref value);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
            
            //UnityEngine.Profiling.Profiler.EndSample();

            return result;
        }

        public static bool TryGetBuffer<TGameObjectEntity, TValue>(this TGameObjectEntity gameObjectEntity, int index, out TValue value)
            where TGameObjectEntity : IGameObjectEntity
            where TValue : unmanaged, IBufferElementData
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                switch (gameObjectEntity.status)
                {
                    case GameObjectEntityStatus.Creating:
                        return __TryGetBuffer(commandSystem, index, entity, commandSystem.factory, out value);
                    case GameObjectEntityStatus.Created:
                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        value = default;
                        return commandSystem.TryGetBuffer(entity, index, ref value);
                    default:
                        throw new InvalidOperationException();
                }
            }

            value = default;

            return false;
        }

        public static bool TryGetBuffer<TValue, TList, TWrapper>(
            this IGameObjectEntity gameObjectEntity,
            ref TList list,
            ref TWrapper wrapper)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

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
                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        return commandSystem.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);
                    default:
                        throw new InvalidOperationException();
                }
            }

            return false;
        }

        public static bool TryGetComponentObject<TGameObjectEntity, TValue>(this TGameObjectEntity gameObjectEntity, out TValue value)
            where TGameObjectEntity : IGameObjectEntity
        {
            var commandSystem = __GetCommandSystem(gameObjectEntity);
            if (commandSystem != null)
            {
                var entity = gameObjectEntity.entity;

                switch (gameObjectEntity.status)
                {
                    case GameObjectEntityStatus.Creating:
                        if (__TryGetComponentData(
                            commandSystem,
                            commandSystem.factory,
                            entity,
                            out EntityObject<TValue> target))
                        {
                            value = target.value;

                            return true;
                        }

                        value = default;

                        return false;
                    case GameObjectEntityStatus.Created:
                        if(entity.Index < 0)
                            UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                        return commandSystem.TryGetComponentObject(entity, out value);
                    default:
                        throw new InvalidOperationException();
                }
            }

            value = default;

            return false;
        }

        public static T GetComponentData<T>(this IGameObjectEntity gameObjectEntity) where T : unmanaged, IComponentData
        {
            bool result = TryGetComponentData(gameObjectEntity, out T value);

            if(!result)
                UnityEngine.Assertions.Assert.IsTrue(result, gameObjectEntity.ToString());

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
            bool result = TryGetBuffer(gameObjectEntity, index, out T value);

            UnityEngine.Assertions.Assert.IsTrue(result);

            return value;
        }

        public static T GetComponentObject<T>(this IGameObjectEntity gameObjectEntity)
        {
            bool result = TryGetComponentObject(gameObjectEntity, out T value);

            UnityEngine.Assertions.Assert.IsTrue(result);

            return value;
        }

        public static bool IsComponentEnabled<T>(this IGameObjectEntity gameObjectEntity) where T  : IEnableableComponent
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            switch (gameObjectEntity.status)
            {
                case GameObjectEntityStatus.Creating:
                    bool result = commandSystem.factory.IsComponentEnabled<T>(entity, out Entity instance, out bool isOverride);
                    if(instance != Entity.Null)
                    {
                        bool resultInstance = commandSystem.IsComponentEnabled<T>(instance, out bool isOverrideInstance);
                        if (isOverrideInstance || !isOverride)
                            return resultInstance;
                    }

                    return result;
                case GameObjectEntityStatus.Created:
                    if(entity.Index < 0)
                        UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                    return commandSystem.IsComponentEnabled<T>(entity, out _);
                default:
                    throw new InvalidOperationException();
            }
        }

        public static bool HasComponent<T>(this IGameObjectEntity gameObjectEntity)
        {
            var entity = gameObjectEntity.entity;

            var commandSystem = __GetCommandSystem(gameObjectEntity);
            var status = gameObjectEntity.status;
            switch (status)
            {
                case GameObjectEntityStatus.Creating:
                case GameObjectEntityStatus.Created:
                    if (status == GameObjectEntityStatus.Creating)
                    {
                        var factory = commandSystem.factory;
                        var instance = factory.GetEntity(entity, true);
                        if (instance == Entity.Null)
                            return factory.HasComponent<T>(entity);

                        bool result = commandSystem.HasComponent<T>(entity, out bool isOverride);
                        if (isOverride)
                            return result;

                        return result || factory.HasComponent<T>(entity);
                    }

                    if(entity.Index < 0)
                        UnityEngine.Assertions.Assert.IsFalse(entity.Index < 0, $"{gameObjectEntity} : {gameObjectEntity.status} : {entity}");

                    return commandSystem.HasComponent<T>(entity, out _);
                default:
                    throw new InvalidOperationException();
            }
        }

        private static bool __TryGetComponentData<TValue, TScheduler>(
            in TScheduler entityManager,
            in EntityCommandFactory factory,
            in Entity entity,
            out TValue value)
            where TValue : unmanaged, IComponentData
            where TScheduler : IEntityCommandScheduler
        {
            value = default;

            bool result = factory.TryGetComponentData(entity, ref value, out Entity instance);
            if (instance != Entity.Null)
                result = entityManager.TryGetComponentData(instance, ref value, result) || result;

            result |= factory.HasComponent<TValue>(entity);

            return result;
        }

        private static bool __TryGetBuffer<TValue, TScheduler>(
            in TScheduler scheduler,
            int index,
            in Entity entity,
            in EntityCommandFactory factory,
            out TValue value)
            where TValue : unmanaged, IBufferElementData
            where TScheduler : IEntityCommandScheduler
        {
            bool result;
            int indexOffset = 0;
            value = default;

            Entity instance = factory.GetEntity(entity);
            if (instance != Entity.Null)
            {
                var entityManager = scheduler.entityManager;
                if (entityManager.HasComponent<TValue>(instance))
                {
                    var buffer = entityManager.GetBuffer<TValue>(instance, true);
                    indexOffset = buffer.Length;

                    result = indexOffset > index;

                    value = result ? buffer[index] : default;
                }
            }

            result = factory.TryGetBuffer(entity, index, ref value, out _, indexOffset);
            if (instance != Entity.Null)
                result = scheduler.TryGetBuffer(instance, index, ref value, indexOffset) || result;

            return result;
        }

        private static bool __TryGetBuffer<TValue, TList, TWrapper, TScheduler>(
            in TScheduler scheduler,
            in Entity entity,
            in EntityCommandFactory factory,
            ref TWrapper wrapper,
            ref TList list)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
            where TScheduler : IEntityCommandScheduler
        {
            bool result = false;
            Entity instance = factory.GetEntity(entity);
            if (instance != Entity.Null)
            {
                var entityManager = scheduler.entityManager;
                if (entityManager.HasComponent<TValue>(instance))
                {
                    var buffer = entityManager.GetBuffer<TValue>(instance, true);
                    int length = buffer.Length;
                    wrapper.SetCount(ref list, length);
                    for (int i = 0; i < length; ++i)
                        wrapper.Set(ref list, buffer[i], i);

                    result = true;
                }
            }

            result = factory.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper, out _) || result;
            if (instance != Entity.Null)
                result = scheduler.TryGetBuffer<TValue, TList, TWrapper>(instance, ref list, ref wrapper, result) || result;

            return result || factory.HasComponent<TValue>(entity);
        }

        private static EntityCommandSharedSystemGroup __GetCommandSystem<T>(in T gameObjectEntity)
            where T : IGameObjectEntity
        {
            if (gameObjectEntity == null)
                return null;

            return __GetCommandSystem(gameObjectEntity.world);
        }

        private static EntityCommandSharedSystemGroup __GetCommandSystem(World world)
        {
            if (world == null || !world.IsCreated)
                return null;

            if (__commander == null || __commander.World != world)
                __commander = world.GetExistingSystemManaged<EntityCommandSharedSystemGroup>();
            
            return __commander;
        }
    }
}