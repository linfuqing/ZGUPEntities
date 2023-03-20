using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    public class GameObjectEntityDefinition : MonoBehaviour, IEntityComponentRoot
    {
        [SerializeField, HideInInspector]
        private int __componentHash;

        [SerializeField, HideInInspector]
        private GameObjectEntityData __data;

        [SerializeField, HideInInspector]
        private List<Component> __components = null;

        public int componentHash
        {
            get => __componentHash;
        }

        public bool Contains(Type type)
        {
            return __data.Contains(type);
        }

        public void GetRuntimeComponentTypes(List<ComponentType> outComponentTypes)
        {
            __data.GetRuntimeComponentTypes(__components, outComponentTypes);
        }

        public Entity CreateEntityDefinition(
            GameObjectEntityInfo info, 
            out EntityComponentAssigner assigner, 
            ComponentTypeSet componentTypes = default)
        {
            UnityEngine.Assertions.Assert.IsTrue(info.isValid);

            var factory = info.world.GetFactory();

            Entity entity;
            var systemStateComponentTypes = info.systemStateComponentTypes;
            if (info.isPrefab)
            {
                Entity prefab = info.prefab;
                if (!info.isValidPrefab)
                {
                    if (prefab != Entity.Null)
                        factory.DestroyEntity(prefab);

                    prefab = factory.CreateEntity(info.entityArchetype, systemStateComponentTypes);

#if UNITY_EDITOR
                    factory.SetName(prefab, $"[Prefab]{info.name}");
#endif

                    __data.SetComponents(prefab, factory.prefabAssigner, __components);

                    info.SetPrefab(prefab);
                }

                entity = factory.Instantiate(prefab, componentTypes);

                assigner = factory.instanceAssigner;
            }
            else
            {
                /*int numComponentTypes = componentTypes.Length;
                if (numComponentTypes > 0)
                {
                    int numSystemStateComponentTypes = systemStateComponentTypes.Length;

                    ComponentType[] prefabComponentTypes = new ComponentType[numSystemStateComponentTypes + numComponentTypes];
                    for (int i = 0; i < numSystemStateComponentTypes; ++i)
                        prefabComponentTypes[i] = systemStateComponentTypes.GetComponentType(i);

                    for (int i = 0; i < numComponentTypes; ++i)
                        prefabComponentTypes[i + numSystemStateComponentTypes] = componentTypes.GetComponentType(i);

                    systemStateComponentTypes = new ComponentTypes(prefabComponentTypes);
                }*/

                entity = factory.CreateEntity(info.entityArchetype, systemStateComponentTypes, componentTypes);

                assigner = factory.prefabAssigner;

                __data.SetComponents(entity, assigner, __components);
            }

#if UNITY_EDITOR
            factory.SetName(entity, info.name);
#endif

            info.SetComponents(entity, assigner, __data, __components);

            return entity;
        }

        public GameObjectEntityData Rebuild()
        {
            if (__data == null)
            {
                __data = ScriptableObject.CreateInstance<GameObjectEntityData>();

                __data.name = name;
            }

            if (__components != null)
                __components.Clear();

            __componentHash = 0;

            __data.Rebuild(transform, __Set);

            return __data;
        }

#if UNITY_EDITOR
        public void Refresh()
        {
            if (__components == null)
                return;

            MethodInfo methodInfo;
            foreach (var component in __components)
            {
                methodInfo = component.GetType().GetMethod("OnRefresh", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (methodInfo == null || methodInfo.GetParameters().Length > 0)
                    continue;

                methodInfo.Invoke(component, null);
            }
        }
#endif

        private int __Set(Component component, int index)
        {
            if (__components == null)
                __components = new List<Component>();

            if (index == -1)
            {
                index = __components.Count;

                __components.Add(component);
            }
            else
                __components[index] = component;

            __componentHash ^= index ^ component.GetType().GetHashCode();

            return index;
        }
    }

    public static partial class GameObjectEntityUtility
    {
        private static EntityCommandSharedSystemGroup __commander = null;

        public static EntityCommandFactory GetFactory(this World world)
        {
            return __GetCommandSystem(world).factory;
        }

        internal static void Destroy(this GameObjectEntityInfo info)
        {
            if (info == null)
                return;

            if (info.isValidPrefab)
            {
                var world = info.world;
                if (world != null && world.IsCreated)
                    __GetCommandSystem(world).factory.DestroyEntity(info.prefab);

                info.SetPrefab(Entity.Null);
            }

            UnityEngine.Object.Destroy(info);
        }

        private static EntityCommandSharedSystemGroup __GetCommandSystem(World world)
        {
            if (__commander == null || __commander.World != world)
                __commander = world.GetOrCreateSystemManaged<EntityCommandSharedSystemGroup>();

            return __commander;
        }
    }
}