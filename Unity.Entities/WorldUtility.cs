using Unity.Core;
using Unity.Entities;

namespace ZG
{
    public struct WorldTimeWrapper
    {
        private WorldUnmanaged __world;
        private EntityQuery __group;

        public static WorldTimeWrapper GetOrCreate(ref SystemState state)
        {
            var worldTimeWrapper = new WorldTimeWrapper(ref state);
            if (worldTimeWrapper.__group.IsEmpty)
                state.EntityManager.CreateEntity(ComponentType.ReadWrite<WorldTime>(), ComponentType.ReadWrite<WorldTimeQueue>());

            return worldTimeWrapper;
        }

        public WorldTimeWrapper(ref SystemState state)
        {
            __world = state.WorldUnmanaged;
            __group = state.GetEntityQuery(ComponentType.ReadWrite<WorldTime>(), ComponentType.ReadWrite<WorldTimeQueue>());
        }

        public void SetTime(in TimeData newTimeData)
        {
            __group.SetSingleton(new WorldTime() { Time = newTimeData });

            __world.Time = newTimeData;
        }

        public void PushTime(in TimeData newTimeData)
        {
            var queue = __world.EntityManager.GetBuffer<WorldTimeQueue>(__group.GetSingletonEntity());
            queue.Add(new WorldTimeQueue() { Time = __world.Time });
            SetTime(newTimeData);
        }

        public void PopTime()
        {
            var queue = __world.EntityManager.GetBuffer<WorldTimeQueue>(__group.GetSingletonEntity());

            UnityEngine.Assertions.Assert.IsTrue(queue.Length > 0, "PopTime without a matching PushTime");

            var prevTime = queue[queue.Length - 1];
            queue.RemoveAt(queue.Length - 1);
            SetTime(prevTime.Time);
        }

    }
}