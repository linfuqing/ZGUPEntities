using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    [EntityComponent(typeof(StateMachineInfo))]
    [EntityComponent(typeof(StateMachineStatus))]
    public class StateMachineComponent : EntityProxyComponent, IEntityComponent
    {
        public void Break()
        {
            StateMachineInfo info = this.GetComponentData<StateMachineInfo>();
            info.systemIndex = -1;
            this.SetComponentData(info);
        }

        public virtual void Init(in Entity entity, EntityComponentAssigner assigner)
        {
            StateMachineInfo info;
            info.count = 0;
            info.systemIndex = -1;
            assigner.SetComponentData(entity, info);

            StateMachineStatus status;
            status.value = 0;
            status.systemIndex = -1;
            assigner.SetComponentData(entity, status);
        }
    }
}