using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Events;

namespace ZG
{
    public class TimerComponent : MonoBehaviour
    {
        [Serializable]
        public struct TimeEvent
        {
            public float change;
            public UnityEvent handler;
        }

        public static LayerMask layerMaskSettings = ~0;

        public bool isPlayOnEnable;
        public bool isDelay;
        public bool isRepeat;
        public float time;

        public string worldName = "Client";

        public LayerMask layerMask;

        [UnityEngine.Serialization.FormerlySerializedAs("_timeEvents")]
        public TimeEvent[] timeEvents;
        private TimeEventSystem __timeEventSystem;
        private TimeEventHandle __timeEventHandle;

        public TimeEventSystem timeEventSystem
        {
            get
            {
                if (__timeEventSystem == null)
                {
                    var world = WorldUtility.GetWorld(worldName);
                    __timeEventSystem = world.GetExistingSystemManaged<TimeEventSystem>();
                }

                return __timeEventSystem;
            }
        }

        public void Stop()
        {
            World world = __timeEventSystem == null ? null : __timeEventSystem.World;
            if (world == null || !world.IsCreated)
                return;

            if (__timeEventHandle.isVail)
            {
                __timeEventSystem.Cannel(__timeEventHandle);

                __timeEventHandle = TimeEventHandle.Null;
            }
        }

        public void Play()
        {
            if(__timeEventHandle.isVail)
                __timeEventSystem.Cannel(__timeEventHandle);

            __Play(isDelay);
        }

        private void __Play(bool isDelay)
        {
            if (!isDelay && timeEvents != null && (layerMask == 0 || (layerMaskSettings & layerMask) != 0))
            {
                float random = UnityEngine.Random.value;
                foreach (TimeEvent timeEvent in timeEvents)
                {
                    if (timeEvent.change > random)
                    {
                        if (timeEvent.handler != null)
                            timeEvent.handler.Invoke();

                        break;
                    }

                    random -= timeEvent.change;
                }
            }

            var timeEventSystem = this.timeEventSystem;
            if(timeEventSystem != null)
                __timeEventHandle = isDelay || isRepeat ? timeEventSystem.Call(__Play, time) : TimeEventHandle.Null;
        }
        
        private void __Play()
        {
            __Play(false);
        }

        void OnEnable()
        {
            if (isPlayOnEnable)
                __Play(isDelay);
        }

        void OnDisable()
        {
            Stop();
        }

    }
}