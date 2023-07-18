using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace ZG
{
    [BurstCompile, UpdateInGroup(typeof(EndFrameEntityCommandSystemGroup))]
    public partial struct EndFrameStructChangeSystem : ISystem
    {
        /*private struct Assigner : EntityCommandStructChangeManager.IAssigner
        {
            public EntityComponentAssigner instance;

            public void Playback(ref SystemState systemState)
            {
                instance.Playback(ref systemState);
            }
        }*/

        //private EntityQuery __group;

        public EntityCommandStructChangeManager manager
        {
            get;

            private set;
        }

        public EntityComponentAssigner assigner
        {
            get;

            private set;
        }

        public EntityAddDataPool addDataPool => new EntityAddDataPool(manager.addComponentPool, assigner);

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            /*__group = state.GetEntityQuery(
                   new EntityQueryDesc()
                   {
                       All = new ComponentType[]
                       {
                        ComponentType.ReadOnly<EntityObjects>()
                       },
                       Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
                   });*/

            manager = new EntityCommandStructChangeManager(Allocator.Persistent);

            assigner = new EntityComponentAssigner(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            manager.Dispose();

            assigner.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            /*Assigner assigner;
            assigner.instance = this.assigner;*/
            manager.Playback(ref state/*, ref assigner*/);

            assigner.Playback(ref state);
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public sealed class EndFrameEntityCommandSystemGroup : ComponentSystemGroup
    {

    }
}