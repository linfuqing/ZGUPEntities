using Unity.Entities;

namespace ZG
{
    //[Serializable, InternalBufferCapacity(1)]
    public struct StateMachine : IBufferElementData
    {
        public SystemHandle systemHandle;
        public SystemHandle executorSystemHandle;
    }

    public struct StateMachineInfo : IComponentData
    {
        public int count;
        public SystemHandle systemHandle;
    }

    public struct StateMachineStatus : IComponentData
    {
        public int value;
        public SystemHandle systemHandle;
    }
}