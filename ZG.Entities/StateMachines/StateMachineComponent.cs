using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    [EntityComponent(typeof(StateMachine))]
    [EntityComponent(typeof(StateMachineInfo))]
    [EntityComponent(typeof(StateMachineStatus))]
    public class StateMachineComponent : EntityProxyComponent, IEntityComponent
    {
        public void Break()
        {
            StateMachineInfo info = this.GetComponentData<StateMachineInfo>();
            info.systemHandle = SystemHandle.Null;
            this.SetComponentData(info);
        }

        public virtual void Init(in Entity entity, EntityComponentAssigner assigner)
        {
            StateMachineInfo info;
            info.count = 0;
            info.systemHandle = SystemHandle.Null;
            assigner.SetComponentData(entity, info);

            StateMachineStatus status;
            status.value = 0;
            status.systemHandle = SystemHandle.Null;
            assigner.SetComponentData(entity, status);
        }
    }
}