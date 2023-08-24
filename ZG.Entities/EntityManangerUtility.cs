using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst.Intrinsics;

//[assembly: ZG.CollectSystems(typeof(ZG.EntityObjects), "CollectSystemTypes")]

namespace ZG
{
    internal interface IEntityObject
    {
        void SetTo(Entity entity, EntityManager entityManager);
    }

    public struct EntityObjects : IComponentData
    {
        /*public struct Types
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
        }*/

        internal static void Set(in Entity entity, EntityComponentAssigner assigner, Type type, object value, World world)
        {
            var target = new EntityObject(value);
            //target.Retain();

            var compoentType = EntityObjectUtility.GetType(type);
            if (compoentType == null)
                world.EntityManager.SetComponentObject(entity, type, value);
            else
            {
                var typeIndex = TypeManager.GetTypeIndex(compoentType);

                assigner.SetComponentData(typeIndex, entity, target);
            }

            /*var systemType = GetTypes(type).systemType;
            System system = systemType == null ? null : world.GetExistingSystemManaged(systemType) as System;
            if (system == null)
                world.EntityManager.SetComponentObject(entity, type, value);
            else
                system.SetObject(entity, assigner, value);*/
        }
    }

    public struct EntityObject : IEquatable<EntityObject>
    {
        private struct Instance
        {
            public object value;
        }

        private unsafe void* __ptr;

        private ulong __gcHandle;

        private int __hashCode;

        public unsafe bool isCreated
        {
            get => __ptr != null;
        }

        public unsafe object value
        {
            get
            {
                UnityEngine.Assertions.Assert.IsTrue(isCreated);

                return UnsafeUtility.AsRef<Instance>(__ptr).value;
            }
        }

        public unsafe EntityObject(object value)
        {
            UnityEngine.Assertions.Assert.AreNotEqual(default, value);

            Instance instance;
            instance.value = value;

            __ptr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(instance, out __gcHandle) + TypeManager.ObjectOffset;

            __hashCode = value.GetHashCode();
        }

        public void Dispose()
        {
            UnsafeUtility.ReleaseGCObject(__gcHandle);
        }

        public unsafe bool Equals(EntityObject other)
        {
            return UnsafeUtility.MemCmp(__ptr, other.__ptr, UnsafeUtility.SizeOf<Instance>()) == 0;
        }

        public override int GetHashCode()
        {
            return __hashCode;
        }
    }

    public struct EntityObject<T> : ICleanupComponentData, IEntityObject, IDisposable, IEquatable<EntityObject<T>>
    {
        /*[UpdateInGroup(typeof(EntityObjectSystemGroup)), RequireMatchingQueriesForUpdate, AlwaysSynchronizeSystem]
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
        }*/

        public static readonly EntityObject<T> Null = default;

        private EntityObject __instance;

        /*internal static Type systemType
        {
            get
            {
                return typeof(System);
            }
        }*/

        public bool isCreated
        {
            get => __instance.isCreated;
        }

        public T value
        {
            get
            {
                return (T)__instance.value;
            }
        }

        public unsafe EntityObject(T value)
        {
            __instance = new EntityObject(value);
        }

        public void Dispose()
        {
            __instance.Dispose();
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
            return __instance.Equals(other.__instance);
        }

        public override int GetHashCode()
        {
            return __instance.GetHashCode();
        }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class RegisterEntityObjectAttribute : RegisterGenericComponentTypeAttribute
    {
        public Type objectType;
        //public Type systemType;

        public RegisterEntityObjectAttribute(Type type) : base(typeof(EntityObject<>).MakeGenericType(type))
        {
            objectType = type;
            //systemType = (Type)ConcreteType.GetProperty("systemType", BindingFlags.Static | BindingFlags.NonPublic).GetMethod.Invoke(null, null);
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))/*, UpdateAfter(typeof(BeginFrameEntityCommandSystem))*/]
    public partial class EntityObjectSystemGroup : ComponentSystemGroup
    {
        public EntityObjectSystemGroup()
        {
            //UseLegacySortOrder = false;
        }
    }

    [BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
    public struct EntityObjectSystem : ISystem
    {
        [BurstCompile]
        private struct DisposeAll : IJobChunk
        {
            public DynamicComponentTypeHandle instanceType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var targets = chunk.GetDynamicComponentDataArrayReinterpret<EntityObject>(ref instanceType, UnsafeUtility.SizeOf<EntityObject>());
                foreach (var target in targets)
                    target.Dispose();
            }
        }

        private struct Group
        {
            private EntityQuery __entityQuery;

            private DynamicComponentTypeHandle __instanceType;

            public Group(in TypeIndex typeIndex, ref SystemState state)
            {
                ComponentType componentType;
                componentType.AccessModeType = ComponentType.AccessMode.ReadOnly;
                componentType.TypeIndex = typeIndex;
                var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
                {
                    componentType
                };

                using (var builder = new EntityQueryBuilder(Allocator.Temp))
                    __entityQuery = builder
                        .WithAll(ref componentTypes)
                        .WithNone<EntityObjects>()
                        .Build(ref state);

                __instanceType = state.GetDynamicComponentTypeHandle(componentType);
            }

            public void Apply(ref SystemState state)
            {
                if (__entityQuery.IsEmpty)
                    return;

                DisposeAll dispose;
                dispose.instanceType = __instanceType.UpdateAsRef(ref state);
                dispose.RunByRef(__entityQuery);

                ComponentType componentType;
                componentType.AccessModeType = ComponentType.AccessMode.ReadWrite;
                componentType.TypeIndex = __instanceType.GetTypeIndex();
                state.EntityManager.RemoveComponent(__entityQuery, componentType);
            }
        }

        private UnsafeHashMap<TypeIndex, Group> __groups;

        public void OnCreate(ref SystemState state)
        {
            var types = EntityObjectUtility.types;
            __groups = new UnsafeHashMap<TypeIndex, Group>(types.Count, Allocator.Persistent);

            TypeIndex typeIndex;
            foreach(var pair in EntityObjectUtility.types)
            {
                typeIndex = TypeManager.GetTypeIndex(pair.Value);

                __groups[typeIndex] = new Group(typeIndex, ref state);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            foreach(var group in __groups)
            {
                group.Value.Apply(ref state);
            }
        }
    }

    public static class EntityObjectUtility
    {
        private static Dictionary<Type, Type> __types;

        public static IReadOnlyDictionary<Type, Type> types
        {
            get
            {
                if (__types == null)
                    __types = CollectTypes();

                return __types;
            }
        }

        public static Dictionary<Type, Type> CollectTypes()
        {
            var results = new Dictionary<Type, Type>();

            IEnumerable<RegisterEntityObjectAttribute> registerEntityObjectAttributes;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                registerEntityObjectAttributes = assembly.GetCustomAttributes<RegisterEntityObjectAttribute>();
                if (registerEntityObjectAttributes != null)
                {
                    foreach (var registerEntityObjectAttribute in registerEntityObjectAttributes)
                        results[registerEntityObjectAttribute.objectType] = registerEntityObjectAttribute.ConcreteType;
                }
            }

            return results;
        }

        public static Type GetType(Type type)
        {
            if (__types == null)
                __types = CollectTypes();

            if (__types.TryGetValue(type, out var result))
                return result;

            return null;
        }
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