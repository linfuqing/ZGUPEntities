using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

[assembly: RegisterGenericJobType(typeof(ZG.TimeManager<ZG.CallbackHandle>.Clear))]
[assembly: RegisterGenericJobType(typeof(ZG.TimeManager<ZG.CallbackHandle>.UpdateEvents))]

namespace ZG
{
    public struct TimeEventHandle<T> where T : unmanaged
    {
        internal NativeRBTreeNode<TimeEvent<T>> _node;

        public bool isVail => _node.isVail;
    }

    public struct TimeEventHandle : IEquatable<TimeEventHandle>
    {
        internal CallbackHandle _callbackHandle;
        internal NativeRBTreeNode<TimeEvent<CallbackHandle>> _node;

        public static readonly TimeEventHandle Null = new TimeEventHandle { _callbackHandle = CallbackHandle.Null, _node = default };

        public bool isVail => _node.isVail;

        public CallbackHandle Cannel(ref NativeRBTree<TimeEvent<CallbackHandle>> timeEvents)
        {
            CallbackHandle callbackHandle = isVail ? _node.valueReadOnly.value : CallbackHandle.Null;
            return timeEvents.Remove(_node) ? callbackHandle : CallbackHandle.Null;
        }

        public bool Equals(TimeEventHandle other)
        {
            return _callbackHandle.Equals(other._callbackHandle) && _node.Equals(other._node);
        }
    }

    //[Serializable]
    public struct TimeEvent<T> : IComparable<TimeEvent<T>> where T : unmanaged
    {
        public double time;

        public T value;

        public int CompareTo(TimeEvent<T> other)
        {
            return time < other.time ? 1 : time > other.time ? -1 : 0;
        }

        public static void UpdateTo(
            NativeRBTree<TimeEvent<T>> timeEvents,
            NativeList<T> results,
            double time)
        {
            var node = timeEvents.tail;
            while (!node.isNull)
            {
                ref var timeEvent = ref node.value;
                if (timeEvent.time > time)
                    break;
                
                timeEvents.Remove(node);

                results.Add(timeEvent.value);

                node = timeEvents.tail;
            }

        }
    }

    public struct TimeManager<T> where T : unmanaged
    {
        [BurstCompile]
        public struct Clear : IJob
        {
            public NativeList<T> values;

            public void Execute()
            {
                values.Clear();
            }
        }

        [BurstCompile]
        public struct UpdateEvents : IJob
        {
            public double time;
            public NativeList<T> values;
            public NativeRBTree<TimeEvent<T>> timeEvents;

            public void Execute()
            {
                TimeEvent<T>.UpdateTo(timeEvents, values, time);
            }
        }

        public struct Writer
        {
            private NativeRBTree<TimeEvent<T>> __events;

            internal Writer(NativeRBTree<TimeEvent<T>> events)
            {
                __events = events;
            }

            public TimeEventHandle<T> Invoke(double time, in T value)
            {
                TimeEvent<T> timeEvent;
                timeEvent.time = time;
                timeEvent.value = value;
                TimeEventHandle<T> handle;
                handle._node = __events.Add(timeEvent, true);

                return handle;
            }

            public bool Cannel(in TimeEventHandle<T> handle)
            {
                return __events.Remove(handle._node);
            }
        }

        private NativeListLite<T> __values;
        private NativeRBTreeLite<TimeEvent<T>> __events;

        public NativeArray<T> values => __values.AsDeferredJobArray();

        public Writer writer => new Writer(__events);

        public TimeManager(Allocator allocator)
        {
            BurstUtility.InitializeJob<Clear>();
            BurstUtility.InitializeJob<UpdateEvents>();

            __values = new NativeListLite<T>(allocator);
            __events = new NativeRBTreeLite<TimeEvent<T>>(allocator);
        }

        public void Dispose()
        {
            __values.Dispose();
            __events.Dispose();
        }

        public void Flush()
        {
            __values.Clear();
        }

        public JobHandle Flush(in JobHandle inputDeps)
        {
            Clear clear;
            clear.values = __values;
            return clear.Schedule(inputDeps);
        }

        public JobHandle Schedule(double time, in JobHandle inputDeps)
        {
            UpdateEvents updateEvents;
            updateEvents.time = time;
            updateEvents.values = __values;
            updateEvents.timeEvents = __events;

            return updateEvents.Schedule(inputDeps);
        }

        public JobHandle ScheduleParallel<U>(in U job, int innerloopBatchCount, in JobHandle inputDeps) where U : struct, IJobParalledForDeferBurstSchedulable
        {
            return job.ScheduleParallel<U, T>(__values, innerloopBatchCount, inputDeps);
        }
    }

    public struct SharedTimeManager
    {
        public readonly AllocatorManager.AllocatorHandle Allocator;

        private unsafe LookupJobManager* __lookupJobManager;
        private TimeManager<CallbackHandle> __instance;

        public unsafe ref LookupJobManager lookupJobManager => ref *__lookupJobManager;

        public unsafe SharedTimeManager(Allocator allocator)
        {
            Allocator = allocator;

            __lookupJobManager = AllocatorManager.Allocate<LookupJobManager>(Allocator);
            *__lookupJobManager = new LookupJobManager();

            __instance = new TimeManager<CallbackHandle>(allocator);
        }

        public unsafe void Dispose()
        {
            AllocatorManager.Free(Allocator, __lookupJobManager);
        }

        public TimeEventHandle<CallbackHandle> Invoke(Action handler, double time)
        {
            var callbackHandle = handler.Register();

            lookupJobManager.CompleteReadWriteDependency();

            return __instance.writer.Invoke(time, callbackHandle);
        }

        public bool Cannel(TimeEventHandle<CallbackHandle> handle)
        {
            lookupJobManager.CompleteReadWriteDependency();

            var callbackHandle = handle._node.isVail ? handle._node.valueReadOnly.value : CallbackHandle.Null;

            return __instance.writer.Cannel(handle) && callbackHandle.Unregister();
        }

        public void Execute()
        {
            lookupJobManager.CompleteReadWriteDependency();

            var values = __instance.values;
            foreach (var value in values)
                value.InvokeAndUnregister();

            __instance.Flush();
        }

        public JobHandle Update(double time, in JobHandle inputDeps)
        {
            lookupJobManager.CompleteReadWriteDependency();

            var jobHandle = __instance.Schedule(time, inputDeps);

            lookupJobManager.readWriteJobHandle = jobHandle;

            return jobHandle;
        }
    }

    public abstract partial class TimeSystem<T> : SystemBase 
        where T : unmanaged
    {
        [BurstCompile]
        private struct UpdateEvents : IJob
        {
            public double time;
            public NativeList<T> values;
            public NativeRBTree<TimeEvent<T>> timeEvents;

            public void Execute()
            {
                TimeEvent<T>.UpdateTo(timeEvents, values, time);
            }
        }
        
        protected NativeList<T> _values;
        protected NativeRBTree<TimeEvent<T>> _events;
        
        public abstract double now { get; }
        
        protected override void OnCreate()
        {
            base.OnCreate();

            _values = new NativeList<T>(Allocator.Persistent);

            _events = new NativeRBTree<TimeEvent<T>>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            _values.Dispose();

            _events.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            UpdateEvents updateEvents;
            updateEvents.time = now;
            updateEvents.values = _values;
            updateEvents.timeEvents = _events;

            Dependency = updateEvents.Schedule(Dependency);
        }
    }

    //[UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TimeEventSystem : TimeSystem<CallbackHandle>
    {
        private double __now;
        
        //public float delta => __timeSystemGroup.delta;

        public override double now => __now;

        public JobHandle jobHandle
        {
            get;

            protected set;
        }

        public NativeList<CallbackHandle> values => _values;

        public NativeRBTree<TimeEvent<CallbackHandle>> events => _events;

        public void AddDependency(JobHandle jobHandle)
        {
            this.jobHandle = JobHandle.CombineDependencies(this.jobHandle, jobHandle);
        }

        public TimeEventHandle Call(Action handler, double time)
        {
            if (!_events.isCreated)
                return TimeEventHandle.Null;

            jobHandle.Complete();

            var callbackHandle = handler.Register();

            TimeEvent<CallbackHandle> timeEvent;
            timeEvent.time = time;
            timeEvent.value = callbackHandle;
            TimeEventHandle handle;
            handle._callbackHandle = callbackHandle;
            handle._node = _events.Add(timeEvent, true);
            
            return handle;
        }

        public TimeEventHandle Call(Action handler, float time)
        {
            return Call(handler, now + time);
        }

        public bool Cannel(TimeEventHandle handle)
        {
            if (!_events.isCreated)
                return false;

            jobHandle.Complete();
            
            return _events.Remove(handle._node) && handle._callbackHandle.Unregister();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
        }

        protected override void OnUpdate()
        {
            //__now = __timeSystemGroup.now + __timeSystemGroup.delta;
            __now += UnityEngine.Time.deltaTime;

            Dependency = JobHandle.CombineDependencies(Dependency, jobHandle);

            base.OnUpdate();

            jobHandle = Dependency;
        }
    }

    //[UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TimeCallbackSystem : SystemBase
    {
        private TimeEventSystem __eventSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            __eventSystem = World.GetOrCreateSystemManaged<TimeEventSystem>();
        }

        protected override void OnUpdate()
        {
            __eventSystem.jobHandle.Complete();

            NativeList<CallbackHandle> values = __eventSystem.values;
            foreach (var value in values)
                CallbackManager.InvokeAndUnregister(value);

            values.Clear();
        }
    }
    
    /*public static class Time
    {
        private static TimeEventSystem __timeEventSystem;
        private static TimeSystemGroup __timeSystemGroup;

        public static float delta
        {
            get
            {
                TimeSystemGroup systemGroup = Time.systemGroup;

                return systemGroup.delta;
            }
        }

        public static double now
        {
            get
            {
                TimeSystemGroup systemGroup = Time.systemGroup;

                return systemGroup == null ? 0.0f : systemGroup.time;
            }
        }

        public static TimeEventSystem eventSystem
        {
            get
            {
                if (__timeEventSystem == null)
                    __timeEventSystem = World.Active.GetExistingSystem<TimeEventSystem>();

                return __timeEventSystem;
            }
        }

        public static TimeSystemGroup systemGroup
        {
            get
            {
                if (__timeSystemGroup == null)
                    __timeSystemGroup = World.Active.GetExistingSystem<TimeSystemGroup>();

                return __timeSystemGroup;
            }
        }
        
        public static TimeEventHandle Call(Action handler, float time)
        {
            return Call(handler, now + time);
        }

        public static TimeEventHandle Call(Action handler, double time)
        {
            TimeEventSystem eventSystem = Time.eventSystem;
            return eventSystem.Call(handler, time);
        }

        public static bool Cannel(TimeEventHandle handle)
        {
            TimeEventSystem eventSystem = Time.eventSystem;
            eventSystem.jobHandle.Complete();

            return eventSystem.events.Remove(handle.node) && CallbackManager.Unregister(handle.callbackHandle);
        }
    }*/
}