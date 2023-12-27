using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

[assembly: RegisterGenericJobType(typeof(ZG.TimeManager<ZG.CallbackHandle>.UpdateEvents))]

namespace ZG
{
    public struct TimeEventHandle<T> where T : unmanaged
    {
        internal NativeRBTreeNode<TimeEvent<T>> _node;

        public bool isVail => _node.isVail;

        public bool Equals(TimeEventHandle<T> other)
        {
            return _node.Equals(other._node);
        }
    }

    public struct TimeEventHandle : IEquatable<TimeEventHandle>
    {
        //internal CallbackHandle _callbackHandle;
        internal TimeEventHandle<CallbackHandle> _value;

        public static readonly TimeEventHandle Null = default;//new TimeEventHandle { _callbackHandle = CallbackHandle.Null, _value = default };

        public bool isVail => _value.isVail;

        public CallbackHandle callbackHandle => isVail ? _value._node.valueReadOnly.value : CallbackHandle.Null;

        public CallbackHandle Cannel(ref TimeManager<CallbackHandle>.Writer timeManager)
        {
            var callbackHandle = this.callbackHandle;
            return timeManager.Cannel(_value) ? callbackHandle : CallbackHandle.Null;
        }

        public bool Equals(TimeEventHandle other)
        {
            return /*_callbackHandle.Equals(other._callbackHandle) &&*/ _value.Equals(other._value);
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
            ref NativeRBTree<TimeEvent<T>> timeEvents,
            ref NativeList<T> results,
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
        public struct UpdateEvents : IJob
        {
            public double time;
            public NativeList<T> values;
            public NativeRBTree<TimeEvent<T>> timeEvents;

            public void Execute()
            {
                TimeEvent<T>.UpdateTo(ref timeEvents, ref values, time);
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

        //private NativeList<T> __values;
        private NativeRBTree<TimeEvent<T>> __events;

        //public NativeArray<T> values => __values.AsDeferredJobArray();

        public bool isCreated => __events.isCreated;

        public AllocatorManager.AllocatorHandle allocator => __events.allocator;

        public Writer writer => new Writer(__events);

        public TimeManager(in AllocatorManager.AllocatorHandle allocator)
        {
            //__values = new NativeList<T>(allocator);
            __events = new NativeRBTree<TimeEvent<T>>(allocator);
        }

        public void Dispose()
        {
            //__values.Dispose();
            __events.Dispose();
        }

        /*public void Flush()
        {
            __values.Clear();
        }

        public JobHandle Flush(in JobHandle inputDeps)
        {
            Clear clear;
            clear.values = __values;
            return clear.Schedule(inputDeps);
        }*/

        public JobHandle Schedule(double time, ref NativeList<T> values, in JobHandle inputDeps)
        {
            UpdateEvents updateEvents;
            updateEvents.time = time;
            updateEvents.values = values;
            updateEvents.timeEvents = __events;

            return updateEvents.ScheduleByRef(inputDeps);
        }

        public NativeRBTreeEnumerator<TimeEvent<T>> GetEnumerator()
        {
            return __events.GetEnumerator();
        }

        /*public JobHandle ScheduleParallel<U>(ref U job, int innerloopBatchCount, in JobHandle inputDeps) where U : struct, IJobParallelForDefer
        {
            return job.ScheduleByRef(__values, innerloopBatchCount, inputDeps);
        }*/
    }

    public struct SharedTimeManager<T> where T : unmanaged
    {
        private unsafe LookupJobManager* __lookupJobManager;

        public unsafe bool isCreated => __lookupJobManager != null;

        public unsafe ref LookupJobManager lookupJobManager => ref *__lookupJobManager;

        public TimeManager<T> value
        {
            get;

            private set;
        }

        public unsafe SharedTimeManager(in AllocatorManager.AllocatorHandle allocator)
        {
            __lookupJobManager = AllocatorManager.Allocate<LookupJobManager>(allocator);
            *__lookupJobManager = new LookupJobManager();

            value = new TimeManager<T>(allocator);
        }

        public unsafe void Dispose()
        {
            AllocatorManager.Free(value.allocator, __lookupJobManager);

            __lookupJobManager = null;

            value.Dispose();
        }

        /*public void Playback()
        {
            lookupJobManager.CompleteReadWriteDependency();

            var values = __instance.values;
            foreach (var value in values)
                value.InvokeAndUnregister();

            __instance.Flush();
        }*/

        public JobHandle Update(double time, ref NativeList<T> values, in JobHandle inputDeps)
        {
            var jobHandle = value.Schedule(time, ref values, JobHandle.CombineDependencies(lookupJobManager.readWriteJobHandle, inputDeps));

            lookupJobManager.readWriteJobHandle = jobHandle;

            return jobHandle;
        }
    }

    [BurstCompile, CreateAfter(typeof(CallbackSystem))]//[UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct TimeEventSystem : ISystem
    {
        private SharedList<CallbackHandle> __callbackHandles;

        public SharedTimeManager<CallbackHandle> manager
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __callbackHandles = state.WorldUnmanaged.GetExistingSystemUnmanaged<CallbackSystem>().handlesToInvokeAndUnregister;
            manager = new SharedTimeManager<CallbackHandle>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            manager.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var lookupJobManager = ref __callbackHandles.lookupJobManager;
            NativeList<CallbackHandle> values = __callbackHandles.writer;
            var jobHandle = manager.Update(
                state.WorldUnmanaged.Time.ElapsedTime, 
                ref values, 
                JobHandle.CombineDependencies(lookupJobManager.readWriteJobHandle, state.Dependency));

            lookupJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    public static class TimeUtility
    {
        
        public static TimeEventHandle Invoke(this SharedTimeManager<CallbackHandle> timeManager, Action handler, double time)
        {
            var callbackHandle = handler.Register();

            timeManager.lookupJobManager.CompleteReadWriteDependency();

            TimeEventHandle result;
            result._value = timeManager.value.writer.Invoke(time, callbackHandle);

            return result;
        }

        public static bool Cannel(this SharedTimeManager<CallbackHandle> timeManager, in TimeEventHandle handle)
        {
            timeManager.lookupJobManager.CompleteReadWriteDependency();

            var callbackHandle = handle.callbackHandle;

            return timeManager.value.writer.Cannel(handle._value) && callbackHandle.Unregister();
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