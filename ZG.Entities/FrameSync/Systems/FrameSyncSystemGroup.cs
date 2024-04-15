using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;

namespace ZG
{
    public struct FrameSyncFlag : IComponentData
    {
        [Flags]
        public enum Value
        {
            Active = 0x01,
            Rollback = 0x02,
            Clear = 0x04
        }

        public Value value;

        public bool isRollback
        {
            get
            {
                Value value = Value.Active | Value.Rollback;

                return (this.value & value) == value;
            }
        }

        public bool isClear
        {
            get
            {
                Value value = Value.Active | Value.Rollback | Value.Clear;

                return (this.value & value) == value;
            }
        }
    }

    public struct FrameSync : IComponentData
    {
        public uint index;
    }

    public struct FrameSyncReal : IComponentData
    {
        public uint index;
    }

    public struct FrameSyncClear : IComponentData
    {
        public uint index;
    }

    /*[DisableAutoCreation, UpdateInGroup(typeof(FrameSyncSystemGroup), OrderFirst = true)/*, UpdateAfter(typeof(RollbackCommandSystem))/]
    public partial class BeginFrameSyncSystemGroupEntityCommandSystem : EntityCommandSystemHybrid
    {
    }*/

    /*[DisableAutoCreation, UpdateInGroup(typeof(FrameSyncSystemGroup), OrderLast = true)]
    public partial class EndFrameSyncSystemGroupEntityCommandSystemGroup : ComponentSystemGroup
    {

    }*/

    [BurstCompile, /*UpdateInGroup(typeof(EndFrameSyncSystemGroupEntityCommandSystemGroup))*/UpdateInGroup(typeof(FrameSyncSystemGroup), OrderLast = true)]
    public partial struct EndFrameSyncSystemGroupStructChangeSystem : ISystem
    {
        public EntityCommandStructChangeManager manager
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            manager = new EntityCommandStructChangeManager(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            manager.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            manager.Playback(ref state);
        }
    }

    public struct FrameSyncSystemGroup
    {
        private SystemGroup __systemGroup;

        private EntityQuery __group;

#if ENABLE_PROFILER
        internal ProfilerMarker __rollback;
        internal ProfilerMarker __setFrames;
#endif

        public bool isClear => __group.GetSingleton<FrameSyncFlag>().isClear;

        public FrameSyncFlag.Value flag
        {
            get => __group.GetSingleton<FrameSyncFlag>().value;

            set
            {
                /*if ((value & FrameSyncFlag.Value.Active) == FrameSyncFlag.Value.Active && World.Name.Contains("Client"))
                    UnityEngine.Debug.Log($"{value} : {frameIndex} : {realFrameIndex}");*/

                FrameSyncFlag flag;
                flag.value = value;
                __group.SetSingleton(flag);
            }
        }

        //public bool isRestore => __rollbackSystemGroup.isActive;

        public uint realFrameIndex
        {
            get => __group.GetSingleton<FrameSyncReal>().index;

            private set
            {
                FrameSyncReal data;
                data.index = value;
                __group.SetSingleton(data);
            }
        }// { get; private set; }

        public uint clearFrameIndex
        {
            get => __group.GetSingleton<FrameSyncClear>().index; //{ get; private set; }

            private set
            {
                FrameSyncClear data;
                data.index = value;
                __group.SetSingleton(data);
            }
        }

        public uint syncFrameIndex
        {
            get => __group.GetSingleton<FrameSync>().index; // { get; private set; }

            private set
            {
                FrameSync data;
                data.index = value;
                __group.SetSingleton(data);
            }
        }

        public uint frameIndex
        {
            get => __group.GetSingleton<RollbackFrame>().index;//{ get; private set; }

            private set
            {
                RollbackFrame data;
                data.index = value;
                __group.SetSingleton(data);
            }
        }

#if ZG_LEGACY_ROLLBACK
        public uint maxFrameCount
        {
            get => __rollbackCommandSystem.maxFrameCount;

            set
            {
                __rollbackCommandSystem.maxFrameCount = value;
            }
        }

        private RollbackCommandManagedSystem __rollbackCommandSystem;
#else
        public RollbackCommanderManaged commander
        {
            get;

            private set;
        }

        public uint maxFrameCount;
#endif

#if ZG_LEGACY_ROLLBACK
        public void Move(long type, uint frameIndex, uint syncFrameIndex)
        {
            __rollbackCommandSystem.Move(type, frameIndex, syncFrameIndex);
        }

        public void Invoke(uint frameIndex, long type, Action value, Action clear)
        {
            __rollbackCommandSystem.Invoke(frameIndex, type, value, clear);
        }

        public void Restore(uint frameIndex)
        {
            __rollbackSystemGroup.Restore(frameIndex);
        }
#endif

        public void Clear()
        {
#if ZG_LEGACY_ROLLBACK
            __rollbackCommandSystem.Clear();
#else
            commander.Clear();
#endif

            frameIndex = 0;
            realFrameIndex = 0;
            syncFrameIndex = 0;
            clearFrameIndex = maxFrameCount;

            flag &= ~FrameSyncFlag.Value.Active;
        }

        /*public override void SortSystemUpdateList()
        {
            var toSort = new List<ComponentSystemBase>(m_systemsToUpdate.Count - 3);
            foreach (var s in m_systemsToUpdate)
            {
                if (s is RollbackSystemGroup || 
                    s is BeginFrameSyncSystemGroupEntityCommandSystem ||
                    s is EndFrameSyncSystemGroupEntityCommandSystem)
                    continue;

                toSort.Add(s);
            }

            m_systemsToUpdate = toSort;
            base.SortSystemUpdateList();
            // Re-insert built-in systems to construct the final list
            var finalSystemList = new List<ComponentSystemBase>(2 + m_systemsToUpdate.Count + 1);
            finalSystemList.Add(__rollbackSystemGroup);
            finalSystemList.Add(__beginEntityCommandSystem);

            foreach (var s in m_systemsToUpdate)
                finalSystemList.Add(s);

            finalSystemList.Add(__endEntityCommandSystem);
            m_systemsToUpdate = finalSystemList;
        }*/

        public FrameSyncSystemGroup(ref SystemState state, uint maxFrameCount = 256)
        {
            var entityManager = state.EntityManager;

            var systemHandle = state.SystemHandle;

            var types = new FixedList128Bytes<ComponentType>
            {
                ComponentType.ReadWrite<FrameSyncFlag>(),
                ComponentType.ReadWrite<FrameSync>(),
                ComponentType.ReadWrite<FrameSyncReal>(),
                ComponentType.ReadWrite<FrameSyncClear>(),
                ComponentType.ReadWrite<RollbackFrame>(),
                ComponentType.ReadWrite<RollbackFrameRestore>(),
                ComponentType.ReadWrite<RollbackFrameSave>(),
                ComponentType.ReadWrite<RollbackFrameClear>()
            };
            entityManager.AddComponent(systemHandle, new ComponentTypeSet(types));

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAllRW<FrameSyncFlag>()
                        .WithAllRW<FrameSync>()
                        .WithAllRW<FrameSyncReal>()
                        .WithAllRW<FrameSyncClear>()
                        .WithAllRW<RollbackFrame>()
                        .WithAllRW<RollbackFrameRestore>()
                        .WithAllRW<RollbackFrameSave>()
                        .WithAllRW<RollbackFrameClear>()
                        .WithOptions(EntityQueryOptions.IncludeSystems)
                        .Build(ref state);

            FrameSyncFlag flag;
            flag.value = FrameSyncFlag.Value.Active | FrameSyncFlag.Value.Rollback | FrameSyncFlag.Value.Clear;
            entityManager.SetComponentData(systemHandle, flag);

            var world = state.World;
            __systemGroup = SystemGroupUtility.GetOrCreateSystemGroup(world, typeof(FrameSyncSystemGroup));

            //__rollbackSystemGroup = world.GetOrCreateSystem<RollbackSystemGroup>();
            //__rollbackSystemGroup.frameSyncSystemGroup = this;

#if ZG_LEGACY_ROLLBACK
            __rollbackCommandSystem = world.GetOrCreateSystem<RollbackCommandManagedSystem>();
            __rollbackCommandSystem.frameSyncSystemGroup = this;
            AddSystemToUpdateList(__rollbackCommandSystem);
            AddSystemToUpdateList(world.GetOrCreateSystem<BeginRollbackSystemGroupEntityCommandSystem>());
#else
            commander = world.GetExistingSystemUnmanaged<RollbackCommandSystem>().commander;
#endif

            //AddSystemToUpdateList(__rollbackSystemGroup);
            //AddSystemToUpdateList(world.GetOrCreateSystem<BeginFrameSyncSystemGroupEntityCommandSystem>());
            //AddSystemToUpdateList(world.GetOrCreateSystem<EndFrameSyncSystemGroupEntityCommandSystemGroup>());

#if ENABLE_PROFILER
            __rollback = new ProfilerMarker("Rollback");
            __setFrames = new ProfilerMarker("Set Frames");
#endif

            this.maxFrameCount = maxFrameCount;

            clearFrameIndex = maxFrameCount;
        }

        public void Update(ref WorldUnmanaged world)
        {
            FrameSyncUtility.Update(maxFrameCount, commander.restoreFrameIndex, __group, ref world, ref __systemGroup);
        }

        /*protected override void OnUpdate()
        {
            uint realFrameIndex = ++this.realFrameIndex;

            RollbackFrameRestore frameRestore;
            RollbackFrameSave frameSave;
            RollbackFrameClear frameClear;
            var flag = this.flag;
            if ((flag & FrameSyncFlag.Value.Active) == FrameSyncFlag.Value.Active)
            {
#if ZG_LEGACY_ROLLBACK
                if ((flag & FrameSyncFlag.Value.Rollback) == FrameSyncFlag.Value.Rollback &&
                    __rollbackSystemGroup.isActive)
                {
                    frameRestore.index = __rollbackSystemGroup.restoreFrameIndex;

                    UnityEngine.Assertions.Assert.AreNotEqual(realFrameIndex, frameRestore.index);

#if DEBUG
                    if (World.Name.Contains("Server"))
                        UnityEngine.Debug.LogError("F Y MT");

                    if (realFrameIndex > frameRestore.index + 256)
                        UnityEngine.Debug.Log($"Rollback From Frame Index {frameRestore.index} To {realFrameIndex}");
#endif

                    if (frameRestore.index <= frameIndex)
                        frameIndex = frameRestore.index - 1;
                }
                else
                    frameRestore.index = realFrameIndex + 1;
#else
                if ((flag & FrameSyncFlag.Value.Rollback) == FrameSyncFlag.Value.Rollback)
                {
                    frameRestore.index = commander.restoreFrameIndex;

                    if (frameRestore.index > 0 && frameRestore.index < frameIndex)
                    {
#if DEBUG
                        if (World.Name.Contains("Server"))
                            UnityEngine.Debug.LogError("F Y MT");

                        if (realFrameIndex > frameRestore.index + 256)
                            UnityEngine.Debug.Log($"Rollback From Frame Index {frameRestore.index} To {realFrameIndex}");
#endif

                        frameIndex = frameRestore.index - 1;
                    }
                    else
                        frameRestore.index = realFrameIndex + 1; 
                }
                else
                    frameRestore.index = realFrameIndex + 1;
#endif

                SetSingleton(frameRestore);

                if (isClear)
                {
                    uint clearFrameIndex = this.clearFrameIndex;
                    if (clearFrameIndex < frameIndex)
                    {
                        frameSave.minIndex = realFrameIndex - 1;

                        uint maxFrameCount = this.maxFrameCount;
                        frameClear.maxIndex = realFrameIndex - (maxFrameCount >> 1);

                        this.clearFrameIndex = frameClear.maxIndex + maxFrameCount;
                    }
                    else
                    {
                        frameSave.minIndex = clearFrameIndex;
                        frameClear.maxIndex = 0;
                    }
                }
                else
                {
                    frameSave.minIndex = realFrameIndex;
                    frameClear.maxIndex = 0;
                }

                SetSingleton(frameSave);
                SetSingleton(frameClear);

#if ENABLE_PROFILER
                using (__rollback.Auto())
#endif
                {
                    RollbackFrame frame;
                    do
                    {
#if ENABLE_PROFILER
                        using (__setFrames.Auto())
#endif
                        {
                            frame.index = ++frameIndex;

                            SetSingleton(frame);
                        }

                        base.OnUpdate();

                        if(frameRestore.index < realFrameIndex)
                        {
                            frameRestore.index = realFrameIndex + 1;

                            SetSingleton(frameRestore);
                        }

                    } while (frameIndex < realFrameIndex);
                }
            }
            else
            {
                frameRestore.index = realFrameIndex + 1; 
                frameSave.minIndex = realFrameIndex;
                frameClear.maxIndex = 0;

                SetSingleton(frameRestore);
                SetSingleton(frameSave);
                SetSingleton(frameClear);
            }

            syncFrameIndex = realFrameIndex;
        }*/
    }
    
    #if FRAME_SYNC_SYSTEM_GROUP_INSTANCE
    public struct FrameSyncSystemGroupInstance : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            new FrameSyncSystemGroup(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            
        }

        public void OnUpdate(ref SystemState state)
        {
            
        }
    }
    #endif

    //[BurstCompile]
    public static class FrameSyncUtility
    {
        /*public delegate void UpdateDelegate(
            uint maxFrameCount,
            uint restoreFrameIndex,
            in Entity entity,
            ref WorldUnmanaged world,
            ref SystemGroup systemGroup);*/

        //public static readonly UpdateDelegate UpdateFunction = BurstCompiler.CompileFunctionPointer<UpdateDelegate>(Update).Invoke;

#if ENABLE_PROFILER
        private static readonly ProfilerMarker Rollback = new ProfilerMarker("Rollback");
        //private static readonly ProfilerMarker __setFrames = new ProfilerMarker("Set Frames");
#endif

        /*[BurstCompile(CompileSynchronously = true)]
        [MonoPInvokeCallback(typeof(UpdateDelegate))]*/
        public static void Update(
            uint maxFrameCount, 
            uint restoreFrameIndex, 
            in EntityQuery group, 
            ref WorldUnmanaged world, 
            ref SystemGroup systemGroup)
        {
            //var entityManager = world.EntityManager;

            var realFrame = group.GetSingleton<FrameSyncReal>();
             
            uint realFrameIndex = ++realFrame.index;

            group.SetSingleton(realFrame);

            RollbackFrameRestore frameRestore;
            RollbackFrameSave frameSave;
            RollbackFrameClear frameClear;
            var flag = group.GetSingleton<FrameSyncFlag>();
            if ((flag.value & FrameSyncFlag.Value.Active) == FrameSyncFlag.Value.Active)
            {
                var frame = group.GetSingleton<RollbackFrame>();
#if ZG_LEGACY_ROLLBACK
                if ((flag & FrameSyncFlag.Value.Rollback) == FrameSyncFlag.Value.Rollback &&
                    __rollbackSystemGroup.isActive)
                {
                    frameRestore.index = __rollbackSystemGroup.restoreFrameIndex;

                    UnityEngine.Assertions.Assert.AreNotEqual(realFrameIndex, frameRestore.index);

#if DEBUG
                    if (World.Name.Contains("Server"))
                        UnityEngine.Debug.LogError("F Y MT");

                    if (realFrameIndex > frameRestore.index + 256)
                        UnityEngine.Debug.Log($"Rollback From Frame Index {frameRestore.index} To {realFrameIndex}");
#endif

                    if (frameRestore.index <= frameIndex)
                        frameIndex = frameRestore.index - 1;
                }
                else
                    frameRestore.index = realFrameIndex + 1;
#else
                if ((flag.value & FrameSyncFlag.Value.Rollback) == FrameSyncFlag.Value.Rollback)
                {
                    frameRestore.index = restoreFrameIndex; //commander.restoreFrameIndex;

                    if (frameRestore.index > 0 && frameRestore.index <= frame.index)
                    {
#if DEBUG
                        /*var worldName = world.Name;
                        if (worldName.Contains(new FixedString128Bytes("Server")))
                            UnityEngine.Debug.LogError("F Y MT");*/

                        if (realFrameIndex > frameRestore.index + 256)
                            UnityEngine.Debug.LogError($"Rollback From Frame Index {frameRestore.index} To {realFrameIndex}");
#endif

                        frame.index = frameRestore.index - 1;
                    }
                    else
                        frameRestore.index = realFrameIndex + 1;
                }
                else
                    frameRestore.index = realFrameIndex + 1;
#endif

                /*if(World.Name.Contains("Client"))
                    UnityEngine.Debug.Log($"Sync From Frame Index {frameIndex} To {realFrameIndex}");*/

                group.SetSingleton(frameRestore);

                if (flag.isClear)
                {
                    var clearFrame = group.GetSingleton<FrameSyncClear>();
                     
                    if (clearFrame.index < frame.index/*realFrameIndex*/)
                    {
                        frameSave.minIndex = realFrameIndex - 1;

                        frameClear.maxIndex = realFrameIndex - (maxFrameCount >> 1);

                        clearFrame.index = frameClear.maxIndex + maxFrameCount;

                        group.SetSingleton(clearFrame);
                    }
                    else
                    {
                        frameSave.minIndex = clearFrame.index;
                        frameClear.maxIndex = 0;
                    }
                }
                else
                {
                    frameSave.minIndex = realFrameIndex;
                    frameClear.maxIndex = 0;
                }

                group.SetSingleton(frameSave);
                group.SetSingleton(frameClear);

#if ENABLE_PROFILER
                using (Rollback.Auto())
#endif
                {
                    do
                    {
                        ++frame.index;

                        group.SetSingleton(frame);

                        systemGroup.Update(ref world);

                        if (frameRestore.index < realFrameIndex)
                        {
                            frameRestore.index = realFrameIndex + 1;

                            group.SetSingleton(frameRestore);
                        }

                    } while (frame.index < realFrameIndex);
                }
            }
            else
            {
                frameRestore.index = realFrameIndex + 1;
                frameSave.minIndex = realFrameIndex;
                frameClear.maxIndex = 0;

                group.SetSingleton(frameRestore);
                group.SetSingleton(frameSave);
                group.SetSingleton(frameClear);
            }

            FrameSync syncFrame;
            syncFrame.index = realFrameIndex;
            group.SetSingleton(syncFrame);
        }
    }
}