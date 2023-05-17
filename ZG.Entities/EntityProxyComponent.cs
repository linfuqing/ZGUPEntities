using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    public class EntityProxyComponent : MonoBehaviour, IGameObjectEntity
    {
        private GameObjectEntity __gameObjectEntity;

        public Entity entity
        {
            get
            {
                GameObjectEntity gameObjectEntity = this.gameObjectEntity;

                return gameObjectEntity == null ? Entity.Null : gameObjectEntity.entity;
            }
        }

        public World world
        {
            get
            {
                GameObjectEntity gameObjectEntity = this.gameObjectEntity;

                return gameObjectEntity == null ? null : gameObjectEntity.world;
            }
        }

        public EntityManager entityManager
        {
            get
            {
                return gameObjectEntity.entityManager;
            }
        }

        public GameObjectEntity gameObjectEntity
        {
            get
            {
                if (__gameObjectEntity == null)
                    __gameObjectEntity = this == null ? null : transform.GetComponentInParent<GameObjectEntity>(true);
                
                return __gameObjectEntity;
            }
        }

        GameObjectEntityStatus IGameObjectEntity.status
        {
            get
            {
                GameObjectEntity gameObjectEntity = this.gameObjectEntity;

                return gameObjectEntity == null ? GameObjectEntityStatus.Invalid : gameObjectEntity.status;
            }
        }

    }

    public class ComponentDataProxy<T> : EntityProxyComponent, IEntityComponent where T : struct, IComponentData
    {
        [SerializeField]
        [UnityEngine.Serialization.FormerlySerializedAs("m_SerializedData")]
        protected internal T _value;

        public T value
        {
            get
            {
                return _value;
            }

            set
            {
                _value = value;

                var gameObjectEntity = base.gameObjectEntity;
                if (gameObjectEntity != null && gameObjectEntity.isAssigned)
                    this.SetComponentData(value);
            }
        }

        [EntityComponents]
        public Type[] entityComponentTypes
        {
            get => new Type[] { typeof(T) };
        }
        
        protected void OnValidate()
        {
            var gameObjectEntity = base.gameObjectEntity;
            if (gameObjectEntity != null && gameObjectEntity.isAssigned)
                this.SetComponentData(_value);
        }
        
        public virtual void Init(in Entity entity, EntityComponentAssigner assigner)
        {
            assigner.SetComponentData(entity, _value);
        }
    }

    public class SystemStateComponentDataProxy<T> : EntityProxyComponent, IEntitySystemStateComponent where T : struct, IComponentData
    {
        [SerializeField]
        [UnityEngine.Serialization.FormerlySerializedAs("m_SerializedData")]
        protected internal T _value;

        public T value
        {
            get
            {
                return _value;
            }

            set
            {
                _value = value;

                var gameObjectEntity = base.gameObjectEntity;
                if (gameObjectEntity != null && gameObjectEntity.isAssigned)
                    this.SetComponentData(value);
            }
        }

        [EntityComponents]
        public Type[] entityComponentTypes
        {
            get => new Type[] { typeof(T) };
        }

        protected void OnValidate()
        {
            var gameObjectEntity = base.gameObjectEntity;
            if (gameObjectEntity != null && gameObjectEntity.isAssigned)
                this.SetComponentData(_value);
        }

        public virtual void Init(in Entity entity, EntityComponentAssigner assigner)
        {
            assigner.SetComponentData(entity, _value);
        }
    }
}