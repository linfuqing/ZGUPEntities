/*using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;

namespace ZG
{
    [BurstCompile, UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ScrollRectSystem : ISystem
    {
        private struct ComputeNodes
        {
            public float deltaTime;
            [ReadOnly]
            public NativeArray<Entity> entityArray;
            [ReadOnly]
            public NativeArray<ScrollRectData> instances;
            [ReadOnly]
            public NativeArray<ScrollRectInfo> infos;
            public NativeArray<ScrollRectNode> nodes;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<ScrollRectEvent> events;

            public void Execute(int index)
            {
                var instance = instances[index];
                var node = nodes[index];

                bool isExistsInfo = index < infos.Length;
                int2 source = (int2)math.round(node.index), destination = isExistsInfo ? infos[index].index : source;
                float2 length = instance.length,
                         cellLength = instance.cellLength,
                         offset = instance.GetOffset(cellLength),
                         distance = node.normalizedPosition * length - destination * cellLength + offset;

                float t = math.pow(instance.decelerationRate, deltaTime);
                //t = t * t* (3.0f - (2.0f * t));
                node.velocity = math.lerp(node.velocity, distance / instance.elasticity, t);

                //velocity *= math.pow(instance.decelerationRate, deltaTime);

                //node.velocity = velocity;

                //velocity += distance / instance.elasticity;

                node.normalizedPosition -= math.select(float2.zero, node.velocity / length, length > math.FLT_MIN_NORMAL) * deltaTime;

                node.index = instance.GetIndex(node.normalizedPosition, length, cellLength, offset);

                int2 target = (int2)math.round(node.index);

                ScrollRectEvent.Flag flag = 0;
                if (!math.all(source == target))
                    flag |= ScrollRectEvent.Flag.Changed;

                if (isExistsInfo && math.all(destination == target))
                {
                    flag |= ScrollRectEvent.Flag.SameAsInfo;

                    node.velocity = float2.zero;
                }

                nodes[index] = node;

                if (flag != 0)
                {
                    Entity entity = entityArray[index];

                    ScrollRectEvent result = events[entity];
                    ++result.version;
                    result.flag = flag;
                    result.index = node.index;

                    events[entity] = result;
                }
            }
        }

        [BurstCompile]
        private struct ComputeNodesEx : IJobChunk
        {
            public float deltaTime;
            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public ComponentTypeHandle<ScrollRectData> instanceType;
            [ReadOnly]
            public ComponentTypeHandle<ScrollRectInfo> infoType;
            public ComponentTypeHandle<ScrollRectNode> nodeType;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<ScrollRectEvent> events;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                ComputeNodes computeNodes;
                computeNodes.deltaTime = deltaTime;
                computeNodes.entityArray = chunk.GetNativeArray(entityType);
                computeNodes.instances = chunk.GetNativeArray(ref instanceType);
                computeNodes.infos = chunk.GetNativeArray(ref infoType);
                computeNodes.nodes = chunk.GetNativeArray(ref nodeType);
                computeNodes.events = events;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    computeNodes.Execute(i);
            }
        }

        private EntityQuery __group;

        private EntityTypeHandle __entityType;
        private ComponentTypeHandle<ScrollRectData> __instanceType;
        private ComponentTypeHandle<ScrollRectInfo> __infoType;
        private ComponentTypeHandle<ScrollRectNode> __nodeType;
        private ComponentLookup<ScrollRectEvent> __events;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<ScrollRectData, ScrollRectNode>()
                    .Build(ref state);

            __entityType = state.GetEntityTypeHandle();
            __instanceType = state.GetComponentTypeHandle<ScrollRectData>(true);
            __infoType = state.GetComponentTypeHandle<ScrollRectInfo>(true);
            __nodeType = state.GetComponentTypeHandle<ScrollRectNode>();
            __events = state.GetComponentLookup<ScrollRectEvent>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ComputeNodesEx computeNodes;
            computeNodes.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
            computeNodes.entityType = __entityType.UpdateAsRef(ref state);
            computeNodes.instanceType = __instanceType.UpdateAsRef(ref state);
            computeNodes.infoType = __infoType.UpdateAsRef(ref state);
            computeNodes.nodeType = __nodeType.UpdateAsRef(ref state);
            computeNodes.events = __events.UpdateAsRef(ref state);
            state.Dependency = computeNodes.ScheduleParallelByRef(__group, state.Dependency);
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ScrollRectSyncSystem : SystemBase
    {
        private EntityQuery __nodeGroup;
        private EntityQuery __eventGroup;

        private struct Move
        {
            [ReadOnly]
            public NativeArray<ScrollRectNode> nodes;
            [ReadOnly]
            public NativeArray<EntityObject<ScrollRectComponent>> instances;

            public void Execute(int index)
            {
                var instance = instances[index].value;
                if (instance != null)
                    instance.scrollRect.normalizedPosition = nodes[index].normalizedPosition;
            }
        }

        private struct MoveEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<ScrollRectNode> nodeType;
            [ReadOnly]
            public ComponentTypeHandle<EntityObject<ScrollRectComponent>> instanceType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Move move;
                move.nodes = chunk.GetNativeArray(ref nodeType);
                move.instances = chunk.GetNativeArray(ref instanceType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    move.Execute(i);
            }
        }

        private struct Invoke
        {
            [ReadOnly]
            public NativeArray<ScrollRectEvent> events;
            [ReadOnly]
            public NativeArray<EntityObject<ScrollRectComponent>> instances;

            public void Execute(int index)
            {
                instances[index].value._Set(events[index]);
            }
        }

        private struct InvokeEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<ScrollRectEvent> eventType;
            [ReadOnly]
            public ComponentTypeHandle<EntityObject<ScrollRectComponent>> instanceType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Invoke invoke;
                invoke.events = chunk.GetNativeArray(ref eventType);
                invoke.instances = chunk.GetNativeArray(ref instanceType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    invoke.Execute(i);
            }
        }

        private ComponentTypeHandle<EntityObject<ScrollRectComponent>> __instanceType;

        private ComponentTypeHandle<ScrollRectNode> __nodeType;
        private ComponentTypeHandle<ScrollRectEvent> __eventType;

        protected override void OnCreate()
        {
            base.OnCreate();

            __nodeGroup = GetEntityQuery(ComponentType.ReadOnly<ScrollRectNode>(), ComponentType.ReadOnly<EntityObject<ScrollRectComponent>>());
            __eventGroup = GetEntityQuery(ComponentType.ReadOnly<ScrollRectEvent>(), ComponentType.ReadOnly<EntityObject<ScrollRectComponent>>());
            __eventGroup.SetChangedVersionFilter(typeof(ScrollRectEvent));

            __instanceType = GetComponentTypeHandle<EntityObject<ScrollRectComponent>>(true);
            __nodeType = GetComponentTypeHandle<ScrollRectNode>(true);
            __eventType = GetComponentTypeHandle<ScrollRectEvent>(true);
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            ref var state = ref this.GetState();
            var instanceType = __instanceType.UpdateAsRef(ref state);

            MoveEx move;
            move.instanceType = instanceType;
            move.nodeType = __nodeType.UpdateAsRef(ref state);
            move.RunByRefWithoutJobs(__nodeGroup);

            InvokeEx invoke;
            invoke.instanceType = instanceType;
            invoke.eventType = __eventType.UpdateAsRef(ref state);
            invoke.RunByRefWithoutJobs(__eventGroup);
        }
    }
}*/