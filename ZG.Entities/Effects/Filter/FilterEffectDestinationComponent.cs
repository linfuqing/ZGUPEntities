using System;

namespace ZG
{
    [Serializable]
    public struct FilterEffectDestination : Unity.Entities.IComponentData
    {
        [Flags]
        public enum Type
        {
            Enable = 0x01,
            Active = 0x02
        }

        public Type type;
    }
    
    [EntityComponent(typeof(UnityEngine.Transform))]
    public class FilterEffectDestinationComponent : ComponentDataProxy<FilterEffectDestination>
    {
        public bool isActive
        {
            get
            {
                return (base.value.type & FilterEffectDestination.Type.Enable) == FilterEffectDestination.Type.Enable;
            }

            set
            {
                FilterEffectDestination instance = base.value;
                if (value)
                {
                    if ((instance.type & FilterEffectDestination.Type.Enable) == FilterEffectDestination.Type.Enable)
                        return;

                    instance.type |= FilterEffectDestination.Type.Enable;
                }
                else
                {
                    if ((instance.type & FilterEffectDestination.Type.Enable) != FilterEffectDestination.Type.Enable)
                        return;

                    instance.type &= ~FilterEffectDestination.Type.Enable;
                }

                base.value = instance;

                /*GameObjectEntity gameObjectEntity = GetComponent<GameObjectEntity>();
                if (gameObjectEntity != null && gameObjectEntity.EntityManager != null)
                    gameObjectEntity.EntityManager.SetComponentData(gameObjectEntity.Entity, instance);*/
            }
        }
    }
}