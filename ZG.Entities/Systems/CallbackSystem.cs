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
        public static void InvokeAndUnregister(in CallbackHandle value)
            => value.InvokeAndUnregister();

        [MonoPInvokeCallback(typeof(Delegate))]
        public static void Unregister(in CallbackHandle value)
            => value.Unregister();

        private FunctionPointer<Delegate> __invokeAndUnregister;
        private FunctionPointer<Delegate> __unregister;

        private GCHandle __invokeAndUnregisterHandle;
        private GCHandle __unregisterHandle;

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
            __invokeAndUnregister = FunctionWrapperUtility.CompileManagedFunctionPointer<Delegate>(InvokeAndUnregister, out __invokeAndUnregisterHandle);

            __unregister = FunctionWrapperUtility.CompileManagedFunctionPointer<Delegate>(Unregister, out __unregisterHandle);

            handlesToInvokeAndUnregister = new SharedList<CallbackHandle>(Allocator.Persistent);

            handlesToUnregister = new SharedList<CallbackHandle>(Allocator.Persistent);
        }

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __invokeAndUnregister = default;
            __invokeAndUnregisterHandle.Free();

            __unregister = default;
            __unregisterHandle.Free();

            handlesToInvokeAndUnregister.Dispose();

            handlesToUnregister.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var handlesToInvokeAndUnregister = this.handlesToInvokeAndUnregister;
            handlesToInvokeAndUnregister.lookupJobManager.CompleteReadWriteDependency();

            var handlesToInvokeAndUnregisterWriter = handlesToInvokeAndUnregister.writer;
            int length = handlesToInvokeAndUnregisterWriter.length;
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
            resize.count = group.CalculateChunkCountWithoutFiltering();
            resize.callbackHandles = handles;
            return resize.ScheduleByRef(jobHandle);
        }

    }
}