using Unity.Entities;

namespace ZG
{
    public interface IStateMachine : IBufferElementData
    {
        int executorIndex
        {
            get;

            set;
        }
    }

    public struct StateMachineInfo : IComponentData
    {
        public int count;
        public int systemIndex;
    }

    public struct StateMachineStatus : IComponentData
    {
        public int value;
        public int systemIndex;
    }
}