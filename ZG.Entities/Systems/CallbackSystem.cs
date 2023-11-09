using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using System.Runtime.InteropServices;
using Unity.Jobs;

namespace ZG
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true), UpdateBefore(typeof(BeginFrameEntityCommandSystemGroup))]
    public partial class CallbackSystemGroup : ComponentSystemGroup
    {

    }

    [BurstCompile, UpdateInGroup(typeof(CallbackSystemGroup))]
    public partial struct CallbackSystem : ISystem
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Delegate(in CallbackHandle value);

        [MonoPInvokeCallback(typeof(Delegate))]
        public static void Invoke(in CallbackHandle value)
            => value.Invoke();

        [MonoPInvokeCallback(typeof(Delegate))]
        public static void InvokeAndUnregister(in CallbackHandle value)
            => value.InvokeAndUnregister();

        [MonoPInvokeCallback(typeof(Delegate))]
        public static void Unregister(in CallbackHandle value)
            => value.Unregister();

        private FunctionPointer<Delegate> __invoke;
        private FunctionPointer<Delegate> __invokeAndUnregister;
        private FunctionPointer<Delegate> __unregister;

        private GCHandle __invokeHandle;
        private GCHandle __invokeAndUnregisterHandle;
        private GCHandle __unregisterHandle;

        public SharedList<CallbackHandle> handlesToInvoke
        {
            get;

            private set;
        }

        public SharedList<CallbackHandle> handlesToInvokeAndUnregister
        {
            get;

            private set;
        }

        public SharedList<CallbackHandle> handlesToUnregister
        {
            get;

            private set;
        }

        //[BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __invoke = FunctionWrapperUtility.CompileManagedFunctionPointer<Delegate>(Invoke, out __invokeHandle);

            __invokeAndUnregister = FunctionWrapperUtility.CompileManagedFunctionPointer<Delegate>(InvokeAndUnregister, out __invokeAndUnregisterHandle);

            __unregister = FunctionWrapperUtility.CompileManagedFunctionPointer<Delegate>(Unregister, out __unregisterHandle);

            handlesToInvoke = new SharedList<CallbackHandle>(Allocator.Persistent);

            handlesToInvokeAndUnregister = new SharedList<CallbackHandle>(Allocator.Persistent);

            handlesToUnregister = new SharedList<CallbackHandle>(Allocator.Persistent);
        }

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __invoke = default;
            __invokeHandle.Free();

            __invokeAndUnregister = default;
            __invokeAndUnregisterHandle.Free();

            __unregister = default;
            __unregisterHandle.Free();

            handlesToInvoke.Dispose();

            handlesToInvokeAndUnregister.Dispose();

            handlesToUnregister.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var handlesToInvoke = this.handlesToInvoke;
            handlesToInvoke.lookupJobManager.CompleteReadWriteDependency();

            var handlesToInvokeWriter = handlesToInvoke.writer;
            int length = handlesToInvokeWriter.length;
            for (int i = 0; i < length; ++i)
                __invoke.Invoke(handlesToInvokeWriter[i]);

            handlesToInvokeWriter.Clear();

            var handlesToInvokeAndUnregister = this.handlesToInvokeAndUnregister;
            handlesToInvokeAndUnregister.lookupJobManager.CompleteReadWriteDependency();

            var handlesToInvokeAndUnregisterWriter = handlesToInvokeAndUnregister.writer;
            length = handlesToInvokeAndUnregisterWriter.length;
            for(int i = 0; i < length; ++i)
                __invokeAndUnregister.Invoke(handlesToInvokeAndUnregisterWriter[i]);

            handlesToInvokeAndUnregisterWriter.Clear();

            var handlesToUnregister = this.handlesToUnregister;
            handlesToUnregister.lookupJobManager.CompleteReadWriteDependency();

            var handlesToUnregisterWriter = handlesToUnregister.writer;
            length = handlesToUnregisterWriter.length;
            for (int i = 0; i < length; ++i)
                __unregister.Invoke(handlesToUnregisterWriter[i]);

            handlesToUnregisterWriter.Clear();
        }
    }

    public static class CallbackUtility
    {
        [BurstCompile]
        private struct ResizeJob : IJob
        {
            public int count;

            public SharedList<CallbackHandle>.Writer callbackHandles;

            public void Execute()
            {
                callbackHandles.capacity = Unity.Mathematics.math.max(callbackHandles.capacity, callbackHandles.length + count);
            }
        }

        public static JobHandle Resize(this ref SharedList<CallbackHandle>.Writer handles, in EntityQuery group, in JobHandle jobHandle)
        {
            ResizeJob resize;
            resize.count = group.CalculateEntityCountWithoutFiltering();
            resize.callbackHandles = handles;
            return resize.ScheduleByRef(jobHandle);
        }

    }
}