using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true), UpdateBefore(typeof(BeginFrameEntityCommandSystemGroup))]
    public partial class CallbackSystemGroup : ComponentSystemGroup
    {

    }

    [BurstCompile, UpdateInGroup(typeof(CallbackSystemGroup))]
    public partial struct CallbackSystem : ISystem
    {
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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            handlesToInvokeAndUnregister = new SharedList<CallbackHandle>(Allocator.Persistent);

            handlesToUnregister = new SharedList<CallbackHandle>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            handlesToInvokeAndUnregister.Dispose();

            handlesToUnregister.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var handlesToInvokeAndUnregister = this.handlesToInvokeAndUnregister;
            handlesToInvokeAndUnregister.lookupJobManager.CompleteReadWriteDependency();

            var handlesToInvokeAndUnregisterWriter = handlesToInvokeAndUnregister.writer;
            int length = handlesToInvokeAndUnregisterWriter.length;
            for(int i = 0; i < length; ++i)
                CallbackManager.InvokeAndUnregister(handlesToInvokeAndUnregisterWriter[i]);

            handlesToInvokeAndUnregisterWriter.Clear();

            var handlesToUnregister = this.handlesToUnregister;
            handlesToUnregister.lookupJobManager.CompleteReadWriteDependency();

            var handlesToUnregisterWriter = handlesToUnregister.writer;
            length = handlesToUnregisterWriter.length;
            for (int i = 0; i < length; ++i)
                CallbackManager.Unregister(handlesToUnregisterWriter[i]);

            handlesToUnregisterWriter.Clear();
        }
    }
}