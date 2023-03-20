using UnityEngine;

namespace ZG
{
    public class FilterEffect : MonoBehaviour
    {
        void OnEnable()
        {
            ++RenderFilterEffect.activeCount;
        }

        void OnDisable()
        {
            --RenderFilterEffect.activeCount;
        }
    }
}