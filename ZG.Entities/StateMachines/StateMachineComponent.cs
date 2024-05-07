using System;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    [EntityComponent(typeof(StateMachineResult))]
    [EntityComponent(typeof(StateMachineStatus))]
    public class StateMachineComponent : EntityProxyComponent, IEntityComponent
    {
        public void Break()
        {
            var result = this.GetComponentData<StateMachineResult>();
            result.systemHandle = SystemHandle.Null;
            this.SetComponentData(result);
        }

        public virtual void Init(in Entity entity, EntityComponentAssigner assigner)
        {
            StateMachineResult result;
            //result.count = 0;
            result.systemHandle = SystemHandle.Null;
            assigner.SetComponentData(entity, result);

            StateMachineStatus status;
            status.value = 0;
            status.systemHandle = SystemHandle.Null;
            assigner.SetComponentData(entity, status);
        }
    }
}