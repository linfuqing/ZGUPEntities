using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Scripting;
#if !UNITY_DOTSRUNTIME
using UnityEngine.PlayerLoop;
using UnityEngine.LowLevel;
#endif

namespace ZG
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public class AutoCreateExceptAttribute : Attribute
    {
        public string name { get; private set; }

        public AutoCreateExceptAttribute(string name)
        {
            this.name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public class AutoCreateInAttribute : Attribute
    {
        public string name { get; private set; }

        public AutoCreateInAttribute(string name)
        {
            this.name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class ForceUpdateInGroupAttribute : Attribute
    {
        public Type systemType { get; private set; }

        public Type groupType { get; private set; }

        public ForceUpdateInGroupAttribute(Type systemType, Type groupType)
        {
            this.systemType = systemType;
            this.groupType = groupType;
        }
    }

    /*[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class CollectSystemsAttribute : Attribute
    {
        public MethodInfo method;
        
        public CollectSystemsAttribute(Type type, string methodName)
        {
            method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (method == null)
                throw new InvalidOperationException();

            var parameters = method.GetParameters();
            if (parameters != null && parameters.Length > 0)
                throw new InvalidOperationException();

            Type returnType = method.ReturnType;
            if(returnType == null)
                throw new InvalidOperationException();

            Type[] types = returnType.GetInterfaces();
            if (Array.IndexOf(types, typeof(IEnumerable<Type>)) == -1)
                throw new InvalidOperationException();
        }
    }*/

    /*[UpdateInGroup(typeof(TimeSystemGroup), OrderFirst = true)]
    public partial class BeginTimeSystemGroupEntityCommandSystem : EntityCommandSystemHybrid
    {
        [Preserve]
        public BeginTimeSystemGroupEntityCommandSystem() { }
    }*/

    /*[UpdateInGroup(typeof(TimeSystemGroup), OrderLast = false)]
    public partial class EndTimeSystemGroupEntityCommandSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public EndTimeSystemGroupEntityCommandSystemGroup() { }
    }

    [DisableAutoCreation, UpdateInGroup(typeof(TimeSystemGroup))]
    public partial class TimeInitializationSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public TimeInitializationSystemGroup() { }
    }

    [DisableAutoCreation, UpdateInGroup(typeof(TimeSystemGroup))]
    public partial class TimePresentationSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public TimePresentationSystemGroup() { }
    }*/

    //[DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true), UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    public partial class TimeSystemGroup : ComponentSystemGroup
    {
        /*[Preserve]
        public TimeSystemGroup()
        {
            SetRateManagerCreateAllocator(new RateUtils.FixedRateCatchUpManager(UnityEngine.Time.fixedDeltaTime));
            //UseLegacySortOrder = false;
        }*/
    }

    public partial class ManagedSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public ManagedSystemGroup()
        {
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            Enabled = false;
        }
    }

    public class WorldInstance : World
    {
        public Entity TimeSingletonEntity => base.TimeSingleton;

        public WorldInstance(
            string name,
            WorldFlags flags) : base(name, flags)
        {

        }
    }

    public static class WorldUtility
    {
        private struct RootGroups : DefaultWorldInitialization.IIdentifyRootGroups
        {
            public bool IsRootGroup(Type type) =>
                type == typeof(InitializationSystemGroup) ||
                //type == typeof(TimeSystemGroup) ||
                type == typeof(SimulationSystemGroup) ||
                type == typeof(PresentationSystemGroup) || 
                type == typeof(ManagedSystemGroup);
        }

        private static Dictionary<string, WorldInstance> __worlds;

        public static Type UpdateInGroup(this Type systemType, Type groupType)
        {
            UpdateInGroupAttribute updateInGroupAttribute = systemType.GetCustomAttribute<UpdateInGroupAttribute>();
            Type type = updateInGroupAttribute == null ? null : updateInGroupAttribute.GroupType;
            if (type == null)
                return null;

            if (type == groupType || type.IsSubclassOf(groupType))
                return type;

            return UpdateInGroup(type, groupType);
        }

        public static bool IsAutoCreateIn(this Type systemType, string[] names)
        {
            bool result = true;
            var autoCreateInAttributes = systemType.GetCustomAttributes<AutoCreateInAttribute>();
            if (autoCreateInAttributes != null)
            {
                foreach (AutoCreateInAttribute autoCreateInAttribute in autoCreateInAttributes)
                {
                    result = false;
                    if (Array.IndexOf(names, autoCreateInAttribute.name) != -1)
                    {
                        result = true;

                        break;
                    }
                }
            }

            return result;
        }

        public static List<Type> FilterSystemTypes(List<Type> systems, string[] names)
        {
            bool isContains;
            int count = systems.Count;
            Type system;
            IEnumerable<AutoCreateInAttribute> autoCreateInAttributes;
            IEnumerable<AutoCreateExceptAttribute> autoCreateExceptAttributes;
            for (int i = 0; i < count; ++i)
            {
                system = systems[i];

                isContains = true;
                autoCreateExceptAttributes = system.GetCustomAttributes<AutoCreateExceptAttribute>();
                if (autoCreateExceptAttributes != null)
                {
                    foreach (var autoCreateExceptAttribute in autoCreateExceptAttributes)
                    {
                        if (Array.IndexOf(names, autoCreateExceptAttribute.name) != -1)
                        {
                            isContains = false;

                            break;
                        }
                    }
                }

                if (isContains)
                {
                    autoCreateInAttributes = system.GetCustomAttributes<AutoCreateInAttribute>();
                    if (autoCreateInAttributes != null)
                    {
                        foreach (var autoCreateInAttribute in autoCreateInAttributes)
                        {
                            if (Array.IndexOf(names, autoCreateInAttribute.name) != -1)
                            {
                                isContains = true;

                                break;
                            }
                            else
                                isContains = false;
                        }
                    }
                }

                if (!isContains)
                {
                    systems.RemoveAt(i--);

                    --count;
                }
            }

            return systems;
        }

        public static void Initialize(
            this World world, 
            WorldSystemFilterFlags systemFilterFlags = WorldSystemFilterFlags.Default, 
            Type[] maskSystemTypes = null, 
            params string[] names)
        {
            var systemTypes = new List<Type>(DefaultWorldInitialization.GetAllSystems(systemFilterFlags));

            Dictionary<Type, List<Type>> updateInGroupTypes = null;

            List<Type> groupTypes;
            IEnumerable<ForceUpdateInGroupAttribute> forceUpdateInGroupAttributes;
            //IEnumerable<CollectSystemsAttribute> collectSystemsAttributes;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                forceUpdateInGroupAttributes = assembly.GetCustomAttributes<ForceUpdateInGroupAttribute>();
                if (forceUpdateInGroupAttributes != null)
                {
                    foreach (var forceUpdateInGroupAttribute in forceUpdateInGroupAttributes)
                    {
                        if (updateInGroupTypes == null)
                            updateInGroupTypes = new Dictionary<Type, List<Type>>();

                        if(!updateInGroupTypes.TryGetValue(forceUpdateInGroupAttribute.systemType, out groupTypes) || groupTypes == null)
                        {
                            groupTypes = new List<Type>();

                            updateInGroupTypes[forceUpdateInGroupAttribute.systemType] = groupTypes;
                        }

                        groupTypes.Add(forceUpdateInGroupAttribute.groupType);
                    }
                }

                /*collectSystemsAttributes = assembly.GetCustomAttributes<CollectSystemsAttribute>();
                if (collectSystemsAttributes != null)
                {
                    foreach (var collectSystemsAttribute in collectSystemsAttributes)
                        systemTypes.AddRange((IEnumerable<Type>)collectSystemsAttribute.method.Invoke(null, null));
                }*/

            }

            //string worldName = world.Name;
            systemTypes = FilterSystemTypes(systemTypes, names);

            var types = new List<Type>();
            //var unmanagedTypes = new List<Type>();

            bool isMask;
            foreach (var system in systemTypes)
            {
                if (maskSystemTypes != null)
                {
                    if(Array.IndexOf(maskSystemTypes, system) != -1)
                        continue;

                    isMask = false;
                    foreach (var maskSystemType in maskSystemTypes)
                    {
                        if (!TypeManager.IsSystemAGroup(maskSystemType))
                            continue;

                        isMask = UpdateInGroup(system, maskSystemType) != null;
                        if (isMask)
                            break;
                    }

                    if (isMask)
                        continue;
                }

                if (typeof(ComponentSystemBase).IsAssignableFrom(system))
                    //managedTypes.Add(system);
                    types.Add(system);
                else if (typeof(ISystem).IsAssignableFrom(system))
                {
                    types.Add(system);

                    //unmanagedTypes.Add(system);
                }
                else
                    throw new InvalidOperationException("Bad type");
            }

            var initializationSystemGroup = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
            var simulationSystemGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            var presentationSystemGroup = world.GetOrCreateSystemManaged<PresentationSystemGroup>();
            //var timeSystemGroup = world.GetOrCreateSystemManaged<TimeSystemGroup>();

            __AddSystemToRootLevelSystemGroupsInternal(world, types, simulationSystemGroup, new RootGroups());

            SystemGroupUtility.SortAllSystemGroups(world.Unmanaged);

            // Update player loop
            initializationSystemGroup.SortSystems();
            //timeSystemGroup.SortSystems();
            simulationSystemGroup.SortSystems();
            presentationSystemGroup.SortSystems();

#if !UNITY_DOTSRUNTIME
            //ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);

            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(initializationSystemGroup, ref playerLoop, typeof(Initialization));

            //ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(timeSystemGroup, ref playerLoop, typeof(TimeUpdate));

            ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(simulationSystemGroup, ref playerLoop, typeof(Update));

            ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(presentationSystemGroup, ref playerLoop, typeof(PreLateUpdate));

            PlayerLoop.SetPlayerLoop(playerLoop);
#endif

        }

        public static WorldInstance Create(
            //string name,
            string worldName, 
            WorldFlags worldFlags = WorldFlags.Simulation,
            WorldSystemFilterFlags worldSystemFilterFlags = WorldSystemFilterFlags.Default,
            params string[] names)
        {
            var world = new WorldInstance(worldName, worldFlags);

            Initialize(world, worldSystemFilterFlags, null, names);

            if (names != null)
            {
                if (__worlds == null)
                    __worlds = new Dictionary<string, WorldInstance>();

                foreach (var name in names)
                    __worlds.Add(name, world);
            }

            return world;
        }

        public static WorldInstance Create(
            string worldName,
            params string[] names)
        {
            return Create(worldName, WorldFlags.Simulation, WorldSystemFilterFlags.Default, names);
        }

        /*public static World GetOrCreateWorld(
            string name, 
            WorldFlags flags = WorldFlags.Simulation, 
            WorldSystemFilterFlags systemFilterFlags = WorldSystemFilterFlags.Default, 
            params Type[] maskSystemTypes)
        {
            World result = null;
            foreach (World world in World.All)
            {
                if (world.Name == name)
                {
                    result = world;

                    break;
                }
            }

            if (result == null)
            {
                result = new World(name, flags);

                Initialize(result, systemFilterFlags, maskSystemTypes);
            }

            return result;
        }*/

        public static WorldInstance GetWorld(string name)
        {
            return __worlds == null || !__worlds.TryGetValue(name, out var world) ? null : world;
            /*World result = null;
            foreach (World world in World.All)
            {
                if (world.Name == name)
                {
                    result = world;

                    break;
                }
            }
            
            return result;*/
        }

        public static ref T GetExistingSystemUnmanaged<T>(in this WorldUnmanaged world) where T : unmanaged, ISystem
        {
            var handle = world.GetExistingUnmanagedSystem<T>();

            return ref world.GetUnsafeSystemRef<T>(handle);
        }

        public static ref T GetExistingSystemUnmanaged<T>(this World world) where T : unmanaged, ISystem
        {
            var handle = world.Unmanaged.GetExistingUnmanagedSystem<T>();

            return ref world.Unmanaged.GetUnsafeSystemRef<T>(handle);
        }

        public static ref T GetOrCreateSystemUnmanaged<T>(this World world) where T : unmanaged, ISystem
        {
            var handle = world.Unmanaged.GetOrCreateUnmanagedSystem<T>();

            return ref world.Unmanaged.GetUnsafeSystemRef<T>(handle);
        }

        private static unsafe void __AddSystemToRootLevelSystemGroupsInternal<T>(World world, IReadOnlyCollection<Type> systemTypes, ComponentSystemGroup defaultGroup, T rootGroups)
            where T : DefaultWorldInitialization.IIdentifyRootGroups
        {
            //var allSystemHandlesToAdd = world.GetOrCreateSystemsAndLogException(systemTypes, systemTypes.Count, Allocator.Temp);
            var allSystemHandlesToAdd = new NativeArray<SystemHandle>(systemTypes.Count, Allocator.Temp);

            int index = 0;
            foreach (var systemType in systemTypes)
                allSystemHandlesToAdd[index++] = world.GetOrCreateSystem(systemType);

            index = 0;
            // Add systems to their groups, based on the [UpdateInGroup] attribute.
            foreach (var systemType in systemTypes)
            {
                SystemHandle system = allSystemHandlesToAdd[index++];
                // Skip the built-in root-level system groups
                if (rootGroups.IsRootGroup(systemType))
                    continue;

                var updateInGroupAttributes = TypeManager.GetSystemAttributes(systemType, typeof(UpdateInGroupAttribute));
                if (updateInGroupAttributes.Length == 0)
                    defaultGroup.AddSystemToUpdateList(system);

                foreach (var attr in updateInGroupAttributes)
                {
                    var group = __FindGroup(world, systemType, attr);
                    if (group != null)
                        group.AddSystemToUpdateList(system);
                }
            }

            allSystemHandlesToAdd.Dispose();
        }

        private static ISystemGroup __FindGroup(World world, Type systemType, Attribute attr)
        {
            var uga = attr as UpdateInGroupAttribute;

            if (uga == null)
                return null;

            if (uga.OrderFirst && uga.OrderLast)
            {
                throw new InvalidOperationException($"The system {systemType} can not specify both OrderFirst=true and OrderLast=true in its [UpdateInGroup] attribute.");
            }

            var groupSys = world.GetExistingSystemManaged(uga.GroupType) as ComponentSystemGroup;
            if (groupSys == null)
            {
                var systemGroup = world.GetSystemGroup(uga.GroupType);
                if (systemGroup.isCreated)
                    return systemGroup;

                if (!TypeManager.IsSystemType(uga.GroupType) || !TypeManager.IsSystemAGroup(uga.GroupType))
                    throw new InvalidOperationException($"Invalid [UpdateInGroup] attribute for {systemType}: {uga.GroupType} must be derived from ComponentSystemGroup Or SystemGroup.");

                // Warn against unexpected behaviour combining DisableAutoCreation and UpdateInGroup
                var parentDisableAutoCreation = TypeManager.GetSystemAttributes(uga.GroupType, typeof(DisableAutoCreationAttribute)).Length > 0;
                if (parentDisableAutoCreation)
                {
                    Debug.LogWarning($"A system {systemType} wants to execute in {uga.GroupType} but this group has [DisableAutoCreation] and {systemType} does not. The system will not be added to any group and thus not update.");
                }
                else
                {
                    Debug.LogWarning(
                        $"A system {systemType} could not be added to group {uga.GroupType}, because the group was not created. Fix these errors before continuing. The system will not be added to any group and thus not update.");
                }
            }

            return groupSys;
        }
    }
}