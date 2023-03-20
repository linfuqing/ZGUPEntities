using System;

namespace ZG
{
    [Serializable]
    public struct FilterEffectSource : Unity.Entities.IComponentData
    {
        public float radius;
    }

    [EntityComponent(typeof(UnityEngine.Transform))]
    public class FilterEffectSourceComponent : ComponentDataProxy<FilterEffectSource>
    {
    }
}