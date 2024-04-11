using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    public interface IEntityComponentRoot
    {
        string worldName { get; }
        
        void CreateEntity(
            ref Entity entity,
            ref EntityCommandFactory factory,
            out EntityComponentAssigner assigner,
            params ComponentType[] componentTypes);
    }

    public interface IEntityComponent
    {
        void Init(in Entity entity, EntityComponentAssigner assigner);
    }

    public interface IEntitySystemStateComponent
    {
        void Init(in Entity entity, EntityComponentAssigner assigner);
    }

    public interface IEntityRuntimeComponentDefinition
    {
        ComponentType[] runtimeComponentTypes { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class EntityComponentAttribute : Attribute
    {
        public Type type;

        public EntityComponentAttribute(Type type = null)
        {
            this.type = type;
        }
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public class EntityComponentsAttribute : Attribute
    {
        public EntityComponentsAttribute()
        {
        }
    }

    public class GameObjectEntityData : ScriptableObject
    {
        private struct ComponentCache
        {
            public int index;
            public int depth;
            public Component component;
        }

        private static Dictionary<Type, ComponentCache> __componentCaches = null;
        private static List<Component> __components = null;

        private List<int> __entityComponents = null;
        private List<int> __entitySystemStateComponents = null;
        private List<int> __entityRuntimeComponentDefinitions = null;
        private Dictionary<Type, int> __componentMap = null;

        public class Info : ScriptableObject
        {
            private static HashSet<ComponentType> __componentTypes = null;
            private static HashSet<ComponentType> __systemStateComponentTypes = null;

            public bool isPrefab
            {
                get;

                private set;
            }

            public bool isValid => version > 0 && entityArchetype.Valid && world != null;

            public bool isValidPrefab => version == prefabVersion;

            public int version
            {
                get;

                private set;
            }

            public int prefabVersion
            {
                get;

                private set;
            }

            public int instanceID
            {
                get;

                private set;
            }

            public int componentHash
            {
                get;

                private set;
            }

            public EntityArchetype entityArchetype
            {
                get;

                private set;
            }

            public ComponentType[] systemStateComponentTypes
            {
                get;

                private set;
            }

            public Entity prefab
            {
                get;

                private set;
            }

            public World world
            {
                get;

                private set;
            }

            public static World CreateOrGetWorld(string name)
            {
                World world;
                if (string.IsNullOrEmpty(name))
                {
                    world = World.DefaultGameObjectInjectionWorld;
                    if (world == null)
                    {
                        DefaultWorldInitialization.DefaultLazyEditModeInitialize();

                        world = World.DefaultGameObjectInjectionWorld;
                    }
                }
                else
                {
                    world = WorldUtility.GetWorld(name);
                    if (world == null)
                        world = WorldUtility.Create(name, name);
                }

                UnityEngine.Assertions.Assert.IsNotNull(world);

                return world;
            }

            public static T Create<T>(int instanceID, int componentHash, string worldName) where T : Info
            {
                T info = CreateInstance<T>();
                info.version = 0;
                info.prefabVersion = 0;
                info.instanceID = instanceID;
                info.componentHash = componentHash;
                info.entityArchetype = default;
                info.systemStateComponentTypes = default;
                info.prefab = Entity.Null;
                info.world = CreateOrGetWorld(worldName);

                return info;
            }

            public void SetComponents(in Entity entity, EntityComponentAssigner assigner, GameObjectEntityData data, IReadOnlyList<Component> objects)
            {
                int index;
                Component component;
                World world = this.world;
                foreach (var pair in data.__componentMap)
                {
                    index = pair.Value;
                    if (index == -1)
                    {
                        UnityEngine.Assertions.Assert.IsFalse(pair.Key.IsSubclassOf(typeof(Component)));

                        continue;
                    }

                    component = objects[index];
                    if (component == null)
                        continue;

                    EntityObjects.Set(entity, assigner, pair.Key, component, world);
                }

                data.SetSystemStateComponents(entity, assigner, objects);
            }

            /*public void Dispose(GameObjectEntityData data, IReadOnlyList<Component> objects)
            {
                if (data.__entityComponents != null)
                {
                    Component component;
                    foreach (var index in data.__entityComponents)
                    {
                        component = objects[index];
                        if (component == null)
                            continue;

                        ((IEntityComponent)component).Dispose();
                    }
                }
            }*/

            public void SetPrefab(in Entity entity)
            {
                prefab = entity;

                prefabVersion = entity == Entity.Null ? 0 : version;
            }

            public void Rebuild(bool isPrefab, GameObjectEntityData data, params ComponentType[] componentTypes)
            {
                this.isPrefab = isPrefab;

                ++version;

                if (__componentTypes == null)
                    __componentTypes = new HashSet<ComponentType>();
                else
                    __componentTypes.Clear();

                if (isPrefab)
                    __componentTypes.Add(ComponentType.ReadOnly<Prefab>());

                __componentTypes.Add(ComponentType.ReadOnly<EntityObjects>());

                if (__systemStateComponentTypes != null)
                    __systemStateComponentTypes.Clear();

                if (componentTypes != null)
                {
                    foreach (var componentType in componentTypes)
                    {
                        if (isPrefab && componentType.IsCleanupComponent)
                        {
                            if (__systemStateComponentTypes == null)
                                __systemStateComponentTypes = new HashSet<ComponentType>();

                            __systemStateComponentTypes.Add(componentType);
                        }
                        else
                            __componentTypes.Add(componentType);
                    }
                }

                if (data.__componentMap != null)
                {
                    ComponentType componentType;
                    foreach (var type in data.__componentMap.Keys)
                    {
                        componentType = EntityObjectUtility.GetType(type) ?? type;
                        if (isPrefab && componentType.IsCleanupComponent)
                        {
                            if (__systemStateComponentTypes == null)
                                __systemStateComponentTypes = new HashSet<ComponentType>();

                            __systemStateComponentTypes.Add(componentType);
                        }
                        else
                            __componentTypes.Add(componentType);
                    }
                }

                int numSystemStateComponentTypes = __systemStateComponentTypes == null ? 0 : __systemStateComponentTypes.Count;
                if (numSystemStateComponentTypes > 0)
                {
                    var systemStateComponentTypes = new ComponentType[numSystemStateComponentTypes];
                    __systemStateComponentTypes.CopyTo(systemStateComponentTypes);
                    this.systemStateComponentTypes = systemStateComponentTypes;
                }

                if (__componentTypes.Count > 256)
                        Debug.LogError($"{name} ComponentType Count Out Of Range {__componentTypes.Count} / 256");

                var entityComponentTypes = new ComponentType[__componentTypes.Count];
                __componentTypes.CopyTo(entityComponentTypes);

                entityArchetype = world.EntityManager.CreateArchetype(entityComponentTypes);
            }
        }

        public bool isBuild => __componentMap != null;

        public ICollection<Type> types => __componentMap == null ? null : __componentMap.Keys;

        public bool Contains(Type type)
        {
            return __componentMap != null && __componentMap.ContainsKey(type);
        }

        public void Rebuild(Transform root, Func<Component, int, int> components)
        {
            if (__componentCaches != null)
                __componentCaches.Clear();

            if (__entityComponents != null)
                __entityComponents.Clear();

            if (__componentMap == null)
                __componentMap = new Dictionary<Type, int>();
            else
                __componentMap.Clear();

            //__componentMap.Add(typeof(Transform), components(target));

            __Build(root, components);
        }

        public void SetComponents(
            in Entity entity, 
            EntityComponentAssigner assigner, 
            IReadOnlyList<Component> objects)
        {
            if (__entityComponents != null)
            {
                Component component;
                foreach (var index in __entityComponents)
                {
                    component = objects[index];
                    if (component == null)
                        continue;

                    ((IEntityComponent)component).Init(entity, assigner);
                }
            }
        }

        public void SetSystemStateComponents(
            in Entity entity, 
            EntityComponentAssigner assigner, 
            IReadOnlyList<Component> objects)
        {
            if (__entitySystemStateComponents != null)
            {
                Component component;
                foreach (var index in __entitySystemStateComponents)
                {
                    component = objects[index];
                    if (component == null)
                        continue;

                    ((IEntitySystemStateComponent)component).Init(entity, assigner);
                }
            }
        }

        public void GetRuntimeComponentTypes(IReadOnlyList<Component> objects, List<ComponentType> outComponentTypes)
        {
            if (__entityRuntimeComponentDefinitions != null)
            {
                bool isDuplicate;
                int numComponentTypes = outComponentTypes.Count, i;
                ComponentType componentType;
                IEnumerable<ComponentType> runtimeComponentTypes;
                Component component;
                foreach (var index in __entityRuntimeComponentDefinitions)
                {
                    component = objects[index];
                    if (component == null)
                        continue;

                    runtimeComponentTypes = ((IEntityRuntimeComponentDefinition)component).runtimeComponentTypes;
                    if (runtimeComponentTypes == null)
                        continue;

                    for (i = 0; i < numComponentTypes; ++i)
                    {
                        componentType = outComponentTypes[i];

                        isDuplicate = false;
                        foreach (var runtimeComponentType in runtimeComponentTypes)
                        {
                            if (runtimeComponentType.TypeIndex == componentType.TypeIndex)
                            {
                                Debug.LogError($"Same component {runtimeComponentType} in {this}", this);
                                isDuplicate = true;

                                break;
                            }
                        }

                        if(isDuplicate)
                        {
                            outComponentTypes.RemoveAtSwapBack(i--);

                            --numComponentTypes;

                            break;
                        }
                    }

                    outComponentTypes.AddRange(runtimeComponentTypes);
                }
            }
        }

        private void __Build(Transform root, Func<Component, int, int> components)
        {
            if (__components == null)
                __components = new List<Component>();

            root.GetComponents(__components);

            int componentIndex;
            Type type;
            PropertyInfo[] propertyInfos;
            IEnumerable<EntityComponentAttribute> entityComponentAttributes;
            IEnumerable<Type> types;
            foreach (var component in __components)
            {
                if (component == null)
                    continue;

                componentIndex = -1;
                if (component is IEntityComponent)
                {
                    componentIndex = __GetComponentIndex(component, components);

                    if (__entityComponents == null)
                        __entityComponents = new List<int>();

                    __entityComponents.Add(componentIndex);
                }

                if (component is IEntitySystemStateComponent)
                {
                    if (componentIndex == -1)
                        componentIndex = __GetComponentIndex(component, components);

                    if (__entitySystemStateComponents == null)
                        __entitySystemStateComponents = new List<int>();

                    __entitySystemStateComponents.Add(componentIndex);
                }

                if (component is IEntityRuntimeComponentDefinition)
                {
                    if (componentIndex == -1)
                        componentIndex = __GetComponentIndex(component, components);

                    if (__entityRuntimeComponentDefinitions == null)
                        __entityRuntimeComponentDefinitions = new List<int>();

                    __entityRuntimeComponentDefinitions.Add(componentIndex);
                }

                type = component.GetType();
                while (type != null && type != typeof(MonoBehaviour) && type != typeof(Behaviour))
                {
                    entityComponentAttributes = type.GetCustomAttributes<EntityComponentAttribute>(false);
                    if (entityComponentAttributes != null)
                    {
                        foreach (var attribute in entityComponentAttributes)
                            __SetType(attribute.type, type, component, components, ref componentIndex);
                    }

                    propertyInfos = type.GetProperties();
                    if (propertyInfos != null)
                    {
                        foreach (var propertyInfo in propertyInfos)
                        {
                            if (propertyInfo.IsDefined(typeof(EntityComponentsAttribute), false))
                            {
                                try
                                {
                                    types = propertyInfo.GetGetMethod().Invoke(component, null) as IEnumerable<Type>;
                                }
                                catch (Exception e)
                                {
                                    Debug.LogError(e.InnerException ?? e, component);

                                    continue;
                                }

                                if (types != null)
                                {
                                    foreach (var typeTemp in types)
                                        __SetType(typeTemp, type, component, components, ref componentIndex);
                                }
                            }
                        }
                    }

                    type = type.BaseType;
                }
            }

            foreach(Transform transform in root)
            {
                if (transform.TryGetComponent<IEntityComponentRoot>(out _))
                    continue;

                __Build(transform, components);
            }
        }

        private int __GetComponentIndex(Component component, Func<Component, int, int> components)
        {
            Type type = component.GetType();

            if (__componentCaches == null)
                __componentCaches = new Dictionary<Type, ComponentCache>();

            if (__componentCaches.TryGetValue(type, out var componentCache))
            {
                if (componentCache.component != component)
                {
                    bool isDirty = componentCache.depth == -1;
                    if (isDirty)
                        componentCache.depth = __DepthOf(componentCache.component.transform);

                    int depth = __DepthOf(component.transform);
                    if(depth < componentCache.depth)
                    {
                        isDirty = true;

                        componentCache.index = components(component, componentCache.index);
                        componentCache.depth = depth;
                        componentCache.component = component;
                    }

                    if (isDirty)
                        __componentCaches[type] = componentCache;
                }
            }
            else
            {
                componentCache.index = components(component, -1);
                componentCache.depth = -1;
                componentCache.component = component;

                __componentCaches[type] = componentCache;
            }

            return componentCache.index;
        }

        private void __SetType(
            Type attributeType, 
            Type componentType, 
            Component component,
            Func<Component, int, int> components, 
            ref int componentIndex)
        {
            if (attributeType == null)
            {
                if (componentIndex == -1)
                    componentIndex = __GetComponentIndex(component, components);

                __componentMap[componentType] = componentIndex;
            }
            else if (attributeType.IsInterface || attributeType.IsSubclassOf(typeof(Component)))
            {
                var tempComponent = component.GetComponentInChildren(attributeType);
                if (tempComponent != null)
                    __componentMap[attributeType] = __GetComponentIndex(tempComponent, components);
            }
            else if (!__componentMap.ContainsKey(attributeType))
                __componentMap[attributeType] = -1;
        }


        private static int __DepthOf(Transform transform)
        {
            var parent = transform.parent;
            if (parent == null)
                return 0;

            return 1 + __DepthOf(parent);
        }
    }
}