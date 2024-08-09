using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Unity.Collections;

namespace ZG
{
    [Serializable]
    public class GameObjectEntityDefinition : IDisposable
    {
        [SerializeField, HideInInspector]
        private int __componentHash;

        [SerializeField, HideInInspector]
        private GameObjectEntityData __data;

        [SerializeField, HideInInspector]
        private List<Component> __components;

        [SerializeField, HideInInspector]
        private byte[] __bytes;

        public int componentHash => __componentHash;

        public void Dispose()
        {
            if (__data == null)
                return;
            
#if UNITY_EDITOR
            if (Application.isPlaying)
#endif
            UnityEngine.Object.Destroy(__data);

            __data = null;
        }

        public bool Contains(Type type)
        {
            return __data.Contains(type);
        }

        public void GetRuntimeComponentTypes(List<ComponentType> outComponentTypes)
        {
            __data.GetRuntimeComponentTypes(__components, outComponentTypes);
        }

        public void CreateEntityDefinition(
            GameObjectEntityInfo info, 
            ref Entity entity, 
            ref EntityCommandFactory factory, 
            out EntityComponentAssigner assigner, 
            IEnumerable<ComponentType> componentTypes = null)
        {
            UnityEngine.Assertions.Assert.IsTrue(info.isValid);

            //var factory = info.world.GetFactory();

            var systemStateComponentTypes = info.systemStateComponentTypes;
            if (entity == Entity.Null || !factory.Exists(entity))
            {
                if (entity == Entity.Null)
                    entity = factory.CreateEntity();

                if (info.isPrefab)
                {
                    Entity prefab = info.prefab;
                    if (!info.isValidPrefab)
                    {
                        if (prefab != Entity.Null)
                            factory.DestroyEntity(prefab);

                        prefab = factory.CreateEntity();

                        factory.CreateEntity(prefab, info.entityArchetype);

                        foreach (var systemStateComponentType in systemStateComponentTypes)
                            factory.AddStateComponent(prefab, systemStateComponentType);

#if UNITY_EDITOR
                        factory.SetName(prefab, $"[Prefab]{info.name}");
#endif

                        __data.SetComponents(prefab, factory.prefabAssigner, __components);

                        info.SetPrefab(prefab);
                    }

                    factory.Instantiate(entity, prefab);

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

                    factory.CreateEntity(entity, info.entityArchetype);

                    foreach (var systemStateComponentType in systemStateComponentTypes)
                        factory.AddComponent(entity, systemStateComponentType);

                    assigner = factory.prefabAssigner;

                    __data.SetComponents(entity, assigner, __components);
                }

#if UNITY_EDITOR
                factory.SetName(entity, info.name);
#endif
            }
            else
            {
                using (var entityArchetypeComponentTypes = info.entityArchetype.GetComponentTypes(Allocator.Temp))
                {
                    foreach (var entityArchetypeComponentType in entityArchetypeComponentTypes)
                    {
                        if (entityArchetypeComponentType.TypeIndex == TypeManager.GetTypeIndex<Prefab>())
                            continue;

                        factory.AddComponent(entity, entityArchetypeComponentType.TypeIndex);
                    }
                }

                foreach(var systemStateComponentType in systemStateComponentTypes)
                    factory.AddComponent(entity, systemStateComponentType);

                assigner = factory.instanceAssigner;

                __data.SetComponents(entity, assigner, __components);
            }

            if (componentTypes != null)
            {
                foreach (var componentType in componentTypes)
                    factory.AddComponent(entity, componentType.TypeIndex);
            }

            info.SetComponents(entity, assigner, __data, __components);
        }

        public GameObjectEntityData Init(Transform transform, bool isForce = false)
        {
            if (__data == null)
            {
                __data = ScriptableObject.CreateInstance<GameObjectEntityData>();

                __data.name = transform.name;
            }

            if (!__data.isCreated)
            {
                if (!isForce && __bytes != null && __bytes.Length > 0)
                {
                    __data.bytes = __bytes;

                    __bytes = null;
                }
                
                if (!__data.isCreated)
                {
                    if (__components != null)
                        __components.Clear();

                    __componentHash = 0;

                    __data.Rebuild(transform, __Set);

                    if (isForce)
                        __bytes = __data.bytes;
                }
            }

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

    [Serializable]
    public class GameObjectEntityInstance : IDisposable
    {
        [SerializeField, HideInInspector]
        private GameObjectEntityInfo __info;

        private int __instanceID;
        
        public bool isValid => __info != null && __info.isValid;

        public World world => __info == null ? null : __info.world;

        public void Dispose()
        {
            if (__info == null)
                return;
            
            if (__info.instanceID == __instanceID)
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
#endif
                    UnityEngine.Object.Destroy(__info);
            }

            __info = null;
        }

        public void Init(int componentHash, string worldName, Transform transform)
        {
            __instanceID = transform.GetInstanceID();
            if (__info != null)
            {
                if (!__info.isValid)
                    __info = null;
                else if (__info.instanceID != __instanceID || __info.componentHash != componentHash)
                {
                    __info.Destroy();

                    __info = null;
                }
            }
            
            if (__info == null)
            {
                __info = GameObjectEntityInfo.Create(__instanceID, componentHash, worldName);
                __info.name = transform.name;
            }
        }

        public void BuildArchetype(
            bool isPrefab, 
            string worldName, 
            Transform transform, 
            GameObjectEntityDefinition definition, 
            params ComponentType[] componentTypes)
        {
            var data = definition.Init(transform);
            
            Init(definition.componentHash, worldName, transform);

            var typeIndices = data.typeIndices;
            if (componentTypes != null)
            {
                foreach (var componentType in componentTypes)
                    typeIndices.Add(componentType.TypeIndex);
            }

            __info.Rebuild(
                    isPrefab, 
                    typeIndices);
        }
        
        public void CreateEntity(
            GameObjectEntityDefinition definition, 
            ref Entity entity, 
            ref EntityCommandFactory factory, 
            out EntityComponentAssigner assigner, 
            params ComponentType[] runtimeComponentTypes)
        {
            definition.CreateEntityDefinition(__info, ref entity, ref factory, out assigner, runtimeComponentTypes);
        }

    }
}