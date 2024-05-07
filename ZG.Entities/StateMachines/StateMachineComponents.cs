using Unity.Entities;

namespace ZG
{
    public struct StateMachineResult : IComponentData
    {
        public SystemHandle systemHandle;
    }

    public struct StateMachineStatus : IComponentData
    {
        public int value;
        public SystemHandle systemHandle;
    }
}