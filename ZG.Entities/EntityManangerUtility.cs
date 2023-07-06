using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

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

    public struct EntityObject<T> : ICleanupComponentData, IEntityObject, IDisposable, IEquatable<EntityObject<T>>
    {
        private struct Instance
        {
            public static readonly SharedStatic<long> Size = SharedStatic<long>.GetOrCreate<Instance>();

            public T value;
        }

        [UpdateInGroup(typeof(EntityObjectSystemGroup)), RequireMatchingQueriesForUpdate, AlwaysSynchronizeSystem]
        private class System : EntityObjects.System
        {
            private EntityQuery __group;

            public override void SetObject(in Entity entity, EntityComponentAssigner assigner, object value)
            {
                var target = new EntityObject<T>((T)value);
                //target.Retain();

                assigner.SetComponentData(entity, target);
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
                        instance.Dispose();
                }

                EntityManager.RemoveComponent<EntityObject<T>>(__group);
            }
        }

        public static readonly EntityObject<T> Null = default;

        private unsafe void* __ptr;

        private ulong __gcHandle;

        private int __hashCode;

        internal static Type systemType
        {
            get
            {
                return typeof(System);
            }
        }

        public unsafe bool isCreated
        {
            get => __ptr != null;
        }

        public unsafe T value
        {
            get
            {
                return UnsafeUtility.AsRef<Instance>(__ptr).value;
            }
        }

        public unsafe EntityObject(T value)
        {
            UnityEngine.Assertions.Assert.AreNotEqual(default, value);

            Instance.Size.Data = UnsafeUtility.SizeOf<Instance>();

            Instance instance;
            instance.value = value;

            __ptr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(instance, out __gcHandle) + TypeManager.ObjectOffset;

            __hashCode = value.GetHashCode();
        }

        public void Dispose()
        {
            UnsafeUtility.ReleaseGCObject(__gcHandle);
        }

        public void SetTo(Entity entity, EntityManager entityManager)
        {
            //UnityEngine.Profiling.Profiler.BeginSample("EntityObject.Retain");

            //Retain();

            //UnityEngine.Profiling.Profiler.EndSample();

            //UnityEngine.Profiling.Profiler.BeginSample("EntityObject.SetComponentData");

            entityManager.SetComponentData(entity, this);

            //UnityEngine.Profiling.Profiler.EndSample();
        }

        public unsafe bool Equals(EntityObject<T> other)
        {
            return UnsafeUtility.MemCmp(__ptr, other.__ptr, Instance.Size.Data) == 0;
        }

        public override int GetHashCode()
        {
            return __hashCode;
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