using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;

[assembly: ZG.CollectSystems(typeof(ZG.EntityObjects), "CollectSystemTypes")]

namespace ZG
{
    internal interface IEntityObject
    {
        void SetTo(Entity entity, EntityManager entityManager);
    }

    public struct EntityObjects : IComponentData
    {
        public struct Types
        {
            public Type concreteType;
            public Type systemType;
        }

        internal abstract partial class System : SystemBase
        {
            public abstract void SetObject(in Entity entity, EntityComponentAssigner assigner, object target);
        }

        private static Dictionary<Type, Types> __types;

        [UnityEngine.Scripting.Preserve]
        public static List<Type> CollectSystemTypes()
        {
            List<Type> types = new List<Type>();

            IEnumerable<RegisterEntityObjectAttribute> registerEntityObjectAttributes;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                registerEntityObjectAttributes = assembly.GetCustomAttributes<RegisterEntityObjectAttribute>();
                if (registerEntityObjectAttributes != null)
                {
                    foreach (var registerEntityObjectAttribute in registerEntityObjectAttributes)
                        types.Add(registerEntityObjectAttribute.systemType);
                }
            }

            return types;
        }

        public static Dictionary<Type, Types> CollectTypes()
        {
            var results = new Dictionary<Type, Types>();

            Types types;
            IEnumerable<RegisterEntityObjectAttribute> registerEntityObjectAttributes;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                registerEntityObjectAttributes = assembly.GetCustomAttributes<RegisterEntityObjectAttribute>();
                if (registerEntityObjectAttributes != null)
                {
                    foreach (var registerEntityObjectAttribute in registerEntityObjectAttributes)
                    {
                        types.concreteType = registerEntityObjectAttribute.ConcreteType;
                        types.systemType = registerEntityObjectAttribute.systemType;

                        results[registerEntityObjectAttribute.objectType] = types;
                    }
                }
            }

            return results;
        }

        public static Types GetTypes(Type type)
        {
            if (__types == null)
                __types = CollectTypes();

            if (__types.TryGetValue(type, out var types))
                return types;

            return default;
        }

        internal static void Set(in Entity entity, EntityComponentAssigner assigner, Type type, object value, World world)
        {
            var systemType = GetTypes(type).systemType;
            System system = systemType == null ? null : world.GetExistingSystemManaged(systemType) as System;
            if (system == null)
                world.EntityManager.SetComponentObject(entity, type, value);
            else
                system.SetObject(entity, assigner, value);
        }
    }

    [Serializable]
    public struct EntityObject<T> : ICleanupComponentData, IEntityObject, IEquatable<EntityObject<T>>
    {
        private struct Instance
        {
            public int count;
            public int version;
            public T value;
            public Action<T> onDispose;
        }

        [UpdateInGroup(typeof(EntityObjectSystemGroup))]
        private class System : EntityObjects.System
        {
            /*private struct Remove
            {
                public NativeArray<EntityObject<T>> instances;

                public void Execute(int index)
                {
                    EntityObject<T> source = instances[index];
                    if (source.__version < 1)
                        return;

                    source.Release();
                }
            }

            private struct RemoveEx : IJobChunk
            {
                [ReadOnly]
                public ComponentTypeHandle<EntityObject<T>> instanceType;

                public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
                {
                    Remove remove;
                    remove.instances = chunk.GetNativeArray(instanceType);
                    int count = chunk.Count;
                    for (int i = 0; i < count; ++i)
                        remove.Execute(i);
                }
            }*/

            private EntityQuery __group;

            public override void SetObject(in Entity entity, EntityComponentAssigner assigner, object value)
            {
                var target = new EntityObject<T>((T)value);
                target.Retain();

                assigner.SetComponentData(entity, target);
                //new EntityObject<T>((T)value).SetTo(entity, EntityManager);
            }

            protected override void OnCreate()
            {
                base.OnCreate();

                __group = GetEntityQuery(
                    ComponentType.ReadOnly<EntityObject<T>>(),
                    ComponentType.Exclude<EntityObjects>());
            }

            protected override void OnUpdate()
            {
                //TODO: 
                CompleteDependency();

                using(var instances = __group.ToComponentDataArray<EntityObject<T>>(Allocator.TempJob))
                {
                    foreach(var instance in instances)
                    {
                        if (instance.__version < 1)
                            continue;

                        instance.Release();
                    }
                }

                /*var iterator = __group.GetArchetypeChunkIterator();
                RemoveEx remove;
                remove.instanceType = GetComponentTypeHandle<EntityObject<T>>(true);
                remove.RunWithoutJobs(ref iterator);*/

                EntityManager.RemoveComponent<EntityObject<T>>(__group);
            }
        }

        private static Pool<Instance> __instances;

        public static readonly EntityObject<T> Null = default;

        private int __index;
        private int __version;

        internal static Type systemType
        {
            get
            {
                return typeof(System);
            }
        }

        public bool isCreated
        {
            get => __version > 0;
        }

        public T value
        {
            get
            {
                if (__instances != null && __instances.TryGetValue(__index, out var instance) && instance.version == __version)
                    return instance.value;

                return default;
            }
        }

        public EntityObject(T value)
        {
            UnityEngine.Assertions.Assert.AreNotEqual(default, value);

            if (__instances == null)
                __instances = new Pool<Instance>();

            __index = __instances.nextIndex;

            bool result = __instances.TryGetValue(__index, out var instance);
            UnityEngine.Assertions.Assert.IsFalse(result);

            instance.count = 0;

            __version = ++instance.version;

            UnityEngine.Profiling.Profiler.BeginSample("EntityObject.Insert");

            instance.value = value;
            instance.onDispose = null;
            __instances.Insert(__index, instance);

            UnityEngine.Profiling.Profiler.EndSample();
        }

        public int Retain()
        {
            var instance = __instances[__index];
            UnityEngine.Assertions.Assert.AreEqual(instance.version, __version);
            ++instance.count;
            __instances[__index] = instance;

            return instance.count;
        }

        public int Release()
        {
            var instance = __instances[__index];
            UnityEngine.Assertions.Assert.AreEqual(instance.version, __version);
            if (--instance.count > 0)
                __instances[__index] = instance;
            else
            {
                if (instance.onDispose != null)
                    instance.onDispose(instance.value);

                __instances.RemoveAt(__index);
            }

            return instance.count;
        }

        public void SetTo(Entity entity, EntityManager entityManager)
        {
            UnityEngine.Profiling.Profiler.BeginSample("EntityObject.Retain");

            Retain();

            UnityEngine.Profiling.Profiler.EndSample();

            UnityEngine.Profiling.Profiler.BeginSample("EntityObject.SetComponentData");

            entityManager.SetComponentData(entity, this);

            UnityEngine.Profiling.Profiler.EndSample();
        }

        public bool Equals(EntityObject<T> other)
        {
            return __index == other.__index && __version == other.__version;
        }

        public override int GetHashCode()
        {
            return __index ^ __version;
        }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class RegisterEntityObjectAttribute : RegisterGenericComponentTypeAttribute
    {
        public Type objectType;
        public Type systemType;

        public RegisterEntityObjectAttribute(Type type) : base(typeof(EntityObject<>).MakeGenericType(type))
        {
            objectType = type;
            systemType = (Type)ConcreteType.GetProperty("systemType", BindingFlags.Static | BindingFlags.NonPublic).GetMethod.Invoke(null, null);
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(BeginFrameEntityCommandSystem))]
    public partial class EntityObjectSystemGroup : ComponentSystemGroup
    {
        public EntityObjectSystemGroup()
        {
            //UseLegacySortOrder = false;
        }
    }

    [UpdateInGroup(typeof(EntityObjectSystemGroup), OrderLast = true)]
    public partial class EndEntityObjectSystemGroupEntityCommandSystem : EntityCommandSystem
    {

    }

    public static class EntityManangerUtility
    {
        private static object[] __parameters = null;

        private static MethodInfo __entityManagerSetComponentObject = null;

        public static bool TryGetComponentData<T>(this EntityManager entityManager, in Entity entity, out T value) where T : unmanaged, IComponentData
        {
            if(!entityManager.HasComponent<T>(entity))
            {
                value = default;

                return false;
            }

            value = entityManager.GetComponentData<T>(entity);

            return true;
        }

        public static void SetComponentObject(this EntityManager entityManager, Entity entity, ComponentType type, object value)
        {
            UnityEngine.Assertions.Assert.IsTrue(value.GetType() == TypeManager.GetType(type.TypeIndex) || value.GetType().IsSubclassOf(TypeManager.GetType(type.TypeIndex)),
                value + " is not a " + TypeManager.GetType(type.TypeIndex));

            if (__entityManagerSetComponentObject == null)
                __entityManagerSetComponentObject = typeof(EntityManager).GetMethod("SetComponentObject", BindingFlags.Instance | BindingFlags.NonPublic);

            if (__parameters == null)
                __parameters = new object[3];

            __parameters[0] = entity;
            __parameters[1] = type;
            __parameters[2] = value;
            __entityManagerSetComponentObject.Invoke(entityManager, __parameters);
        }

        public static bool SetComponentDataIfExists<T>(this EntityManager entityManager, Entity entity, T componentData) where T : unmanaged, IComponentData
        {
            if (entityManager.HasComponent<T>(entity))
            {
                entityManager.SetComponentData(entity, componentData);

                return true;
            }

            return false;
        }

        public static void AddOrSetComponentData<T>(this EntityManager entityManager, Entity entity, T componentData) where T : unmanaged, IComponentData
        {
            if (entityManager.HasComponent<T>(entity))
                entityManager.SetComponentData(entity, componentData);
            else
                entityManager.AddComponentData(entity, componentData);
        }

        public static void AddOrSetComponentData<T>(this ExclusiveEntityTransaction entityManager, Entity entity, T componentData) where T : unmanaged, IComponentData
        {
            if (!entityManager.HasComponent(entity, typeof(T)))
                entityManager.AddComponent(entity, typeof(T));

            entityManager.SetComponentData(entity, componentData);
        }

        public static void AddOrSetSharedComponentData<T>(this EntityManager entityManager, Entity entity, T componentData) where T : unmanaged, ISharedComponentData
        {
            if (entityManager.HasComponent<T>(entity))
                entityManager.SetSharedComponentManaged(entity, componentData);
            else
                entityManager.AddSharedComponentManaged(entity, componentData);
        }

        public static DynamicBuffer<T> AddBufferIfNotExists<T>(this EntityManager entityManager, Entity entity) where T : unmanaged, IBufferElementData
        {
            if (entityManager.HasComponent<T>(entity))
                return entityManager.GetBuffer<T>(entity);

            return entityManager.AddBuffer<T>(entity);
        }

        public static DynamicBuffer<T> AddBufferIfNotExists<T>(this ExclusiveEntityTransaction entityManager, Entity entity) where T : unmanaged, IBufferElementData
        {
            if (entityManager.HasComponent(entity, typeof(T)))
                return entityManager.GetBuffer<T>(entity);

            return entityManager.AddBuffer<T>(entity);
        }

        public static bool RemoveComponentIfExists<T>(this EntityManager entityManager, Entity entity)
        {
            if (entityManager.HasComponent<T>(entity))
            {
                entityManager.RemoveComponent<T>(entity);

                return true;
            }

            return false;
        }

        public static bool RemoveComponentIfExists<T>(this ExclusiveEntityTransaction entityManager, Entity entity)
        {
            if (entityManager.HasComponent(entity, typeof(T)))
            {
                entityManager.RemoveComponent(entity, typeof(T));

                return true;
            }

            return false;
        }

        public static void DestroyEntityIfExists(this EntityManager entityManager, Entity entity)
        {
            if (entityManager.Exists(entity))
                entityManager.DestroyEntity(entity);
        }
    }
}