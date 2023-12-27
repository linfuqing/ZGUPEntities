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

        public LayerMask layerMask;

        [UnityEngine.Serialization.FormerlySerializedAs("_timeEvents")]
        public TimeEvent[] timeEvents;

        [SerializeField]
        internal string _worldName = "Client";

        private World __world;

        private SharedTimeManager<CallbackHandle> __timeManager;
        private TimeEventHandle __timeEventHandle;

        public SharedTimeManager<CallbackHandle> timeManager
        {
            get
            {
                if (!__timeManager.isCreated)
                {
                    __world = WorldUtility.GetWorld(_worldName);
                    __timeManager = __world.GetExistingSystemUnmanaged<TimeEventSystem>().manager;
                }

                return __timeManager;
            }
        }

        public void Stop()
        {
            if (__world != null && __world.IsCreated && __timeEventHandle.isVail)
            {
                __timeManager.Cannel(__timeEventHandle);

                __timeEventHandle = TimeEventHandle.Null;
            }
        }

        public void Play()
        {
            if(__timeEventHandle.isVail)
                __timeManager.Cannel(__timeEventHandle);

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

            var timeManager = this.timeManager;
            if(timeManager.isCreated)
                __timeEventHandle = isDelay || isRepeat ? timeManager.Invoke(__Play, __world.Time.ElapsedTime + time) : TimeEventHandle.Null;
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