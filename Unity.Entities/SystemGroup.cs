using System;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Entities;
#if !NET_DOTS
using System.Linq;
#endif
using UpdateOrderSystemSorter = Unity.Entities.ComponentSystemSorter<Unity.Entities.UpdateBeforeAttribute, Unity.Entities.UpdateAfterAttribute>;

namespace Unity.Entities
{
    internal interface ISystemGroup
    {
        void AddSystemToUpdateList(SystemHandle sysHandle);

        void RemoveSystemFromUpdateList(SystemHandle sys);
    }

    public partial class ComponentSystemGroup : ISystemGroup
    {

    }
}

namespace ZG
{
    public unsafe struct SystemGroup : ISystemGroup
    {
        private struct Data
        {
            //internal delegate bool UnmanagedUpdateSignature(IntPtr pSystemState, out SystemDependencySafetyUtility.SafetyErrorDetails errorDetails);

            //internal static readonly UnmanagedUpdateSignature UnmanagedUpdateFn = BurstCompiler.CompileFunctionPointer<UnmanagedUpdateSignature>(SystemBase.UnmanagedUpdate).Invoke;

            public bool isSystemSortDirty;

            // If true (the default), calling SortSystems() will sort the system update list, respecting the constraints
            // imposed by [UpdateBefore] and [UpdateAfter] attributes. SortSystems() is called automatically during
            // DefaultWorldInitialization, as well as at the beginning of ComponentSystemGroup.OnUpdate(), but may also be
            // called manually.
            //
            // If false, calls to SortSystems() on this system group will have no effect on update order of systems in this
            // group (though SortSystems() will still be called recursively on any child system groups). The group's systems
            // will update in the order of the most recent sort operation, with any newly-added systems updating in
            // insertion order at the end of the list.
            //
            // Setting this value to false is not recommended unless you know exactly what you're doing, and you have full
            // control over the systems which will be updated in this group.
            public bool isEnableSystemSorting;

            public bool isRunning;

            public UnsafeList<int> masterUpdateList;
            public UnsafeList<SystemHandle> systemsToUpdate;
            public UnsafeList<SystemHandle> systemsToRemove;
        }

        private int __systemIndex;
        private Data* __data;

        public bool isCreated => __data != null;

        internal UnsafeList<SystemHandle> systems => __data->systemsToUpdate;

        private static int __ComputeSystemOrdering(Type sysType, Type ourType)
        {
            foreach (var uga in TypeManager.GetSystemAttributes(sysType, typeof(UpdateInGroupAttribute)))
            {
                var updateInGroupAttribute = (UpdateInGroupAttribute)uga;

                if (updateInGroupAttribute.GroupType.IsAssignableFrom(ourType))
                {
                    if (updateInGroupAttribute.OrderFirst)
                    {
                        return 0;
                    }

                    if (updateInGroupAttribute.OrderLast)
                    {
                        return 2;
                    }
                }
            }

            return 1;
        }

        [BurstDiscard]
        internal unsafe static void UpdateSystem(ref bool isBurst, ref WorldUnmanagedImpl impl, SystemState* sys)
        {
            isBurst = false;

            var previousState = new WorldUnmanagedImpl.PreviousSystemGlobalState(ref impl, sys);

            try
            {
                WorldUnmanagedImpl.UnmanagedUpdate(sys);
            }
            catch
            {
                sys->AfterOnUpdate();

                previousState.Restore(ref impl, sys);
#if ENABLE_PROFILER
                if (sys->WasUsingBurstProfilerMarker())
                    sys->m_ProfilerMarkerBurst.Begin();
                else
                    sys->m_ProfilerMarker.Begin();
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Limit follow up errors if we arrived here due to a job related exception by syncing all jobs
                sys->m_DependencyManager->Safety.PanicSyncAll();
#endif

                throw;
            }

            previousState.Restore(ref impl, sys);
        }

        internal unsafe static void UpdateSystem(ref WorldUnmanaged world, SystemHandle sh)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (sh == SystemHandle.Null)
                throw new NullReferenceException("The system couldn't be updated. The SystemHandle is default/null, so was never assigned.");
#endif

            var sys = world.ResolveSystemStateChecked(sh);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (sys == null)
                throw new NullReferenceException("The system couldn't be resolved. The System has been destroyed.");
#endif

            ref var impl = ref world.GetImpl();

            bool isBurst = true;
            UpdateSystem(ref isBurst, ref impl, sys);
            if(isBurst)
            {
                var previousState = new WorldUnmanagedImpl.PreviousSystemGlobalState(ref impl, sys);
                try
                {
                    WorldUnmanagedImpl.UnmanagedUpdate(sys);
                }
                finally
                {
                    previousState.Restore(ref impl, sys);
                }
            }
        }

        public bool isEnableSystemSorting
        {
            get => __data->isEnableSystemSorting;

            private set
            {
                if (value && !__data->isEnableSystemSorting)
                    __data->isSystemSortDirty = true; // force a sort after re-enabling sorting

                __data->isEnableSystemSorting = value;
            }
        }

        public SystemGroup(int systemIndex, Allocator allocator)
        {
            __systemIndex = systemIndex;

            __data = AllocatorManager.Allocate<Data>(allocator);
            __data->isSystemSortDirty = false;
            __data->isEnableSystemSorting = true;

            __data->isRunning = true;

            __data->systemsToUpdate = new UnsafeList<SystemHandle>(0, allocator);
            __data->systemsToRemove = new UnsafeList<SystemHandle>(0, allocator);
            __data->masterUpdateList = new UnsafeList<int>(0, allocator);
        }

        public void Dispose()
        {
            var allocator = __data->masterUpdateList.Allocator;

            __data->masterUpdateList.Dispose();
            __data->systemsToRemove.Dispose();
            __data->systemsToUpdate.Dispose();

            AllocatorManager.Free<Data>(allocator, __data);

            __data = null;
        }

        public NativeList<SystemHandle> GetAllSystems(Allocator allocator = Allocator.Temp)
        {
            var ret = new NativeList<SystemHandle>(__data->systemsToUpdate.Length, allocator);
            for (int i = 0; i < __data->systemsToUpdate.Length; i++)
                ret.Add(__data->systemsToUpdate[i]);

            return ret;
        }

        public void SetActive(bool value, in WorldUnmanaged world)
        {
            if (value == __data->isRunning)
                return;

            if (!value)
            {
                for (int i = 0; i < __data->systemsToUpdate.Length; ++i)
                {
                    var sys = world.ResolveSystemState(__data->systemsToUpdate[i]);

                    if (sys == null || !sys->PreviouslyEnabled)
                        continue;

                    sys->PreviouslyEnabled = false;

                    // Optional callback here
                }
            }

            __data->isRunning = value;
        }

        public void Update(ref WorldUnmanaged world)
        {
            __CheckCreated();

            __UpdateAllSystems(ref world);
        }

        /// <summary>
        /// Update the component system's sort order.
        /// </summary>
        public void SortSystems(in WorldUnmanaged world)
        {
            __CheckCreated();

            __RecurseUpdate(world);
        }

        public void AddSystemToUpdateList(SystemHandle sysHandle)
        {
            __CheckCreated();

            if (-1 != __GetSystemIndex(sysHandle))
            {
                int index = __data->systemsToRemove.IndexOf(sysHandle);
                if (-1 != index)
                    __data->systemsToRemove.RemoveAt(index);

                return;
            }

            __data->masterUpdateList.Add(__data->systemsToUpdate.Length);
            __data->systemsToUpdate.Add(sysHandle);

            __data->isSystemSortDirty = true;
        }

        public void RemoveSystemFromUpdateList(SystemHandle sys)
        {
            __CheckCreated();

            if (__data->systemsToUpdate.Contains(sys) && !__data->systemsToRemove.Contains(sys))
            {
                __data->isSystemSortDirty = true;

                __data->systemsToRemove.Add(sys);
            }
        }

        internal void RemoveSystemsFromUnsortedUpdateList(in WorldUnmanaged world)
        {
            if (__data->systemsToRemove.Length <= 0)
                return;

            int largestID = 0;

            //determine the size of the lookup table used for looking up system information; whether a system is due to be removed
            //and/or the new update index of the system

            foreach (var system in __data->systemsToUpdate)
            {
                largestID = math.max(largestID, world.ResolveSystemState(system)->m_SystemID);
            }

            var newListIndices = new NativeArray<int>(largestID + 1, Allocator.Temp);
            var systemIsRemoved = new NativeArray<byte>(largestID + 1, Allocator.Temp, NativeArrayOptions.ClearMemory);

            //update removed system lookup table
            foreach (var system in __data->systemsToRemove)
            {
                systemIsRemoved[world.ResolveSystemState(system)->m_SystemID] = 1;
            }

            var newUnmanagedUpdateList = new UnsafeList<SystemHandle>(__data->systemsToUpdate.Length, __data->systemsToUpdate.Allocator);

            //use removed lookup table to determine which systems will be in the new update
            foreach (var system in __data->systemsToUpdate)
            {
                var systemID = world.ResolveSystemState(system)->m_SystemID;
                if (systemIsRemoved[systemID] == 0)
                {
                    newListIndices[systemID] = newUnmanagedUpdateList.Length;
                    newUnmanagedUpdateList.Add(system);
                }
            }

            var newMasterUpdateList = new UnsafeList<int>(newUnmanagedUpdateList.Length, __data->masterUpdateList.Allocator);

            foreach (var updateIndex in __data->masterUpdateList)
            {
                var system = __data->systemsToUpdate[updateIndex];
                var systemID = world.ResolveSystemState(system)->m_SystemID;
                if (systemIsRemoved[systemID] == 0)
                    newMasterUpdateList.Add(newListIndices[systemID]);
            }

            newListIndices.Dispose();
            systemIsRemoved.Dispose();

            __data->systemsToUpdate.Dispose();
            __data->systemsToUpdate = newUnmanagedUpdateList;
            __data->systemsToRemove.Clear();

            __data->masterUpdateList.Dispose();
            __data->masterUpdateList = newMasterUpdateList;
        }

        private void __UpdateAllSystems(ref WorldUnmanaged world)
        {
            if (!__data->isRunning)
                return;

            /*if (__data->isSystemSortDirty)
                SortSystems(world);*/

            // Update all unmanaged and managed systems together, in the correct sort order.
            // The master update list contains indices for both managed and unmanaged systems.
            // Negative values indicate an index in the unmanaged system list.
            // Positive values indicate an index in the managed system list.


            var previouslyExecutingSystem = world.ExecutingSystem;
            // Cache the update list length before updating; any new systems added mid-loop will change the length and
            // should not be processed until the subsequent group update, to give SortSystems() a chance to run.
            int updateListLength = __data->masterUpdateList.Length;
            for (int i = 0; i < updateListLength; ++i)
            {
                var index = __data->masterUpdateList[i];

                // Update unmanaged (burstable) code.
                var handle = __data->systemsToUpdate[index];
                world.ExecutingSystem = handle;
                UpdateSystem(ref world, __data->systemsToUpdate[index]);

                world.ExecutingSystem = previouslyExecutingSystem;

                /*if (world.QuitUpdate)
                    break;*/
            }

            //world.DestroyPendingSystems();
        }

        private void __RecurseUpdate(in WorldUnmanaged world)
        {
            if (__data->isSystemSortDirty)
            {
                __data->isSystemSortDirty = false;

                __GenerateMasterUpdateList(world);
            }
        }

        private void __GenerateMasterUpdateList(in WorldUnmanaged world)
        {
            __RemovePending();

            var groupType = SystemGroupUtility.GetSystemGroupType(__systemIndex);
            var allElems = new UpdateOrderSystemSorter.SystemElement[__data->systemsToUpdate.Length];
            var systemsPerBucket = new int[3];
            for (int i = 0; i < __data->systemsToUpdate.Length; ++i)
            {
                var sysType = world.GetTypeOfSystem(__data->systemsToUpdate[i]);
                int orderingBucket = __ComputeSystemOrdering(sysType, groupType);
                allElems[i] = new UpdateOrderSystemSorter.SystemElement
                {
                    Type = sysType,
                    Index = new UpdateIndex(i, false),
                    OrderingBucket = orderingBucket,
                    updateBefore = new List<Type>(),
                    nAfter = 0,
                };
                systemsPerBucket[orderingBucket]++;
            }

            // Find & validate constraints between systems in the group
            UpdateOrderSystemSorter.FindConstraints(groupType, allElems);

            // Build three lists of systems
            var elemBuckets = new[]
            {
                new UpdateOrderSystemSorter.SystemElement[systemsPerBucket[0]],
                new UpdateOrderSystemSorter.SystemElement[systemsPerBucket[1]],
                new UpdateOrderSystemSorter.SystemElement[systemsPerBucket[2]],
            };
            var nextBucketIndex = new int[3];

            for (int i = 0; i < allElems.Length; ++i)
            {
                int bucket = allElems[i].OrderingBucket;
                int index = nextBucketIndex[bucket]++;
                elemBuckets[bucket][index] = allElems[i];
            }
            // Perform the sort for each bucket.
            for (int i = 0; i < 3; ++i)
            {
                if (elemBuckets[i].Length > 0)
                {
                    UpdateOrderSystemSorter.Sort(elemBuckets[i]);
                }
            }

            // Commit results to master update list
            __data->masterUpdateList.Clear();
            __data->masterUpdateList.SetCapacity(allElems.Length);

            // Append buckets in order, but replace managed indices with incrementing indices
            // into the newly sorted m_systemsToUpdate list
            for (int i = 0; i < 3; ++i)
            {
                foreach (var e in elemBuckets[i])
                    __data->masterUpdateList.Add(e.Index.Index);
            }
        }

        private void __RemovePending()
        {
            for (int i = 0; i < __data->systemsToRemove.Length; ++i)
                __data->systemsToUpdate.RemoveAt(__data->systemsToUpdate.IndexOf(__data->systemsToRemove[i]));

            __data->systemsToRemove.Clear();
        }

        private int __GetSystemIndex(SystemHandle sysHandle)
        {
            int length = __data->systemsToUpdate.Length;
            for (int i = 0; i < length; ++i)
            {
                if (__data->systemsToUpdate[i] == sysHandle)
                    return i;
            }

            return -1;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckCreated()
        {
            if (__data == null/* || !__data->systemsToUpdate.IsCreated*/)
                throw new InvalidOperationException($"System Group has not been created, either the derived class forgot to call base.OnCreate(), or it has been destroyed");
        }
    }
    
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public class SystemGroupInheritAttribute : Attribute
    {
        public Type type;

        public SystemGroupInheritAttribute(Type type)
        {
            this.type = type;
        }
    }

    public static class SystemGroupUtility
    {
        private static UnsafeList<SystemGroup> __systemGroups;

        private static List<Type> __systemGroupTypes;

        private static Dictionary<(World, Type), int> __typeIndices;

        public static void Dispose()
        {
            if (__systemGroups.IsCreated)
            {
                foreach (var systemGroup in __systemGroups)
                    systemGroup.Dispose();

                __systemGroups.Dispose();
            }
        }

        public static bool TryGetSystemGroupIndex(World world, Type type, out int systemGroupIndex)
        {
            if (__typeIndices != null)
            {
                Type baseType = type;
                IEnumerable<SystemGroupInheritAttribute> attributes;
                do
                {
                    if (__typeIndices.TryGetValue((world, baseType), out systemGroupIndex))
                        return true;

                    attributes = baseType.GetCustomAttributes<SystemGroupInheritAttribute>();
                    if (attributes != null)
                    {
                        foreach (var attribute in attributes)
                        {
                            if (TryGetSystemGroupIndex(world, attribute.type, out systemGroupIndex))
                                return true;
                        }
                    }

                    baseType = baseType.BaseType;

                } while (baseType != null);
            }

            systemGroupIndex = -1;

            return false;
        }

        public static Type GetSystemGroupType(int systemGroupIndex)
        {
            return __systemGroupTypes[systemGroupIndex];
        }

        public static SystemGroup GetOrCreateSystemGroup(this World world, Type type)
        {
            if (__typeIndices == null)
                __typeIndices = new Dictionary<(World, Type), int>();

            if (TryGetSystemGroupIndex(world, type, out int systemGroupIndex))
                return __systemGroups[systemGroupIndex];

            if (!__systemGroups.IsCreated)
                __systemGroups = new UnsafeList<SystemGroup>(1, Allocator.Persistent);

            systemGroupIndex = __systemGroups.Length;
            var systemGroup = new SystemGroup(systemGroupIndex, Allocator.Persistent);
            __systemGroups.Add(systemGroup);

            __typeIndices[(world, type)] = systemGroupIndex;

            if (__systemGroupTypes == null)
                __systemGroupTypes = new List<Type>();

            __systemGroupTypes.Add(type);

            return systemGroup;
        }

        public static SystemGroup GetSystemGroup(this World world, Type type)
        {
            if (TryGetSystemGroupIndex(world, type, out int systemGroupIndex))
                return __systemGroups[systemGroupIndex];

            return default;
        }

        public static void SortAllSystemGroups(in WorldUnmanaged world)
        {
            if (!__systemGroups.IsCreated)
                return;

            foreach (var systemGroup in __systemGroups)
                systemGroup.SortSystems(world);
        }
    }
}