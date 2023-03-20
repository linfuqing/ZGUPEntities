using UnityEngine;

namespace ZG
{
    public class TimerSettings : MonoBehaviour
    {
        public void Enable(int layer)
        {
            TimerComponent.layerMaskSettings |= 1 << layer; 
        }

        public void Disable(int layer)
        {
            TimerComponent.layerMaskSettings &= ~(1 << layer);
        }

        void Awake()
        {
            TimerComponent.layerMaskSettings = 0;
        }
    }
}
