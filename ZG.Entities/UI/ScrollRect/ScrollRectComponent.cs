using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;

[assembly: ZG.RegisterEntityObject(typeof(ZG.ScrollRectComponent))]

namespace ZG
{
    [Serializable]
    public struct ScrollRectData : IComponentData
    {
        public float decelerationRate;
        public float elasticity;

        public int2 count;
        public float2 contentLength;
        public float2 viewportLength;

        public float2 length
        {
            get
            {
                return contentLength - viewportLength;
            }
        }

        public float2 cellLength
        {
            get
            {
                return new float2(count.x > 0 ? contentLength.x / count.x : contentLength.x, count.y > 0 ? contentLength.y / count.y : contentLength.y);
            }
        }

        public float2 offset
        {
            get
            {
                return GetOffset(cellLength);
            }
        }

        public float2 GetOffset(float2 cellLength)
        {
            return (cellLength - viewportLength) * 0.5f;
        }

        public float2 GetIndex(float2 position, float2 length, float2 cellLength, float2 offset)
        {
            return math.clamp((position * length - offset) / cellLength, 0.0f, new float2(count.x > 0 ? count.x - 1 : 0, count.y > 0 ? count.y - 1 : 0));
        }

        public float2 GetIndex(float2 position, float2 length)
        {
            float2 cellLength = this.cellLength;
            return GetIndex(position, length, cellLength, GetOffset(cellLength));
        }

        public float2 GetIndex(float2 position)
        {
            return GetIndex(position, length);
        }
    }

    [Serializable]
    public struct ScrollRectInfo : IComponentData
    {
        public int2 index;
    }
    
    [Serializable]
    public struct ScrollRectNode : IComponentData
    {
        public float2 velocity;
        public float2 normalizedPosition;
        public float2 index;
    }

    [Serializable]
    public struct ScrollRectEvent : IComponentData
    {
        [Flags]
        public enum Flag
        {
            Changed = 0x01,
            SameAsInfo = 0x02
        }

        public int version;
        public Flag flag;
        public float2 index;
    }

    [EntityComponent]
    [EntityComponent(typeof(ScrollRectData))]
    [EntityComponent(typeof(ScrollRectInfo))]
    [EntityComponent(typeof(ScrollRectEvent))]
    public class ScrollRectComponent : EntityProxyComponent, IBeginDragHandler, IEndDragHandler, IDragHandler, IEntityComponent
    {
        public event Action<float2> onChanged;

        private int __version = 0;

        private ScrollRectData __data;

        private ScrollRect __scrollRect;
        
        public virtual int2 count
        {
            get
            {
                ScrollRect scrollRect = this.scrollRect;
                RectTransform content = scrollRect == null ? null : scrollRect.content;
                if (content == null)
                    return int2.zero;

                int count = 0;
                GameObject gameObject;
                foreach (Transform child in content)
                {
                    gameObject = child.gameObject;
                    if (gameObject != null && gameObject.activeSelf)
                        ++count;
                }

                RectTransform.Axis axis = scrollRect.horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;
                int2 result = int2.zero;
                result[(int)axis] = count;

                return result;
            }
        }

        public int2 index
        {
            get;

            private set;
        } = new int2(-1, -1);

        public ScrollRectData data
        {
            get
            {
                ScrollRect scrollRect = this.scrollRect;
                if (scrollRect == null)
                    return default;

                ScrollRectData result;
                result.decelerationRate = scrollRect.decelerationRate;
                result.elasticity = scrollRect.elasticity;

                result.count = count;

                Canvas.ForceUpdateCanvases();

                RectTransform content = scrollRect.content;
                result.contentLength = content == null ? float2.zero : (float2)content.rect.size;

                RectTransform viewport = scrollRect.viewport;
                result.viewportLength = viewport == null ? float2.zero : (float2)viewport.rect.size;

                return result;
            }
        }

        public ScrollRect scrollRect
        {
            get
            {
                if (__scrollRect == null)
                    __scrollRect = GetComponent<ScrollRect>();

                return __scrollRect;
            }
        }
        
        public static Vector2 GetSize(RectTransform rectTransform, bool isHorizontal, bool isVertical)
        {
            /*if (rectTransform == null)
                return Vector2.zero;

            Vector2 min = rectTransform.anchorMin, max = rectTransform.anchorMax;
            if (Mathf.Approximately(min.x, max.x))
            {
                LayoutGroup layoutGroup = rectTransform.GetComponent<LayoutGroup>();
                if (layoutGroup != null)
                    layoutGroup.SetLayoutHorizontal();

                ContentSizeFitter contentSizeFitter = rectTransform.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                    contentSizeFitter.SetLayoutHorizontal();

                //return rectTransform.sizeDelta;
            }

            if (Mathf.Approximately(min.y, max.y))
            {
                LayoutGroup layoutGroup = rectTransform.GetComponent<LayoutGroup>();
                if (layoutGroup != null)
                    layoutGroup.SetLayoutVertical();

                ContentSizeFitter contentSizeFitter = rectTransform.GetComponent<ContentSizeFitter>();
                if (contentSizeFitter != null)
                    contentSizeFitter.SetLayoutVertical();

                //return rectTransform.sizeDelta;
            }

            Vector2 size = GetSize(rectTransform.parent as RectTransform);

            return size * max + rectTransform.offsetMax - size * min - rectTransform.offsetMin;*/
            if (rectTransform == null)
                return Vector2.zero;

            LayoutGroup layoutGroup = rectTransform.GetComponentInParent<LayoutGroup>();
            if (layoutGroup != null)
            {
                if (isHorizontal)
                    layoutGroup.SetLayoutHorizontal();

                if (isVertical)
                    layoutGroup.SetLayoutVertical();
            }

            ContentSizeFitter contentSizeFitter = rectTransform.GetComponentInChildren<ContentSizeFitter>();
            if (contentSizeFitter != null)
            {
                if (isHorizontal)
                    contentSizeFitter.SetLayoutHorizontal();

                if (isVertical)
                    contentSizeFitter.SetLayoutVertical();
            }

            rectTransform.ForceUpdateRectTransforms();

            Canvas.ForceUpdateCanvases();

            return rectTransform.rect.size;
        }

        public virtual bool UpdateData()
        {
            __data = data;

            this.SetComponentData(__data);

            __EnableNode(float2.zero);

            return true;
        }

        public virtual bool MoveTo(int2 index)
        {
            ScrollRectInfo info;
            info.index = index;
            this.AddComponentData(info);

            return true;
        }

        protected void OnEnable()
        {
            if (gameObjectEntity.isCreated)
                Canvas.willRenderCanvases += __UpdateData;
        }
        
        internal void _Set(ScrollRectEvent result)
        {
            if (__version == result.version)
                return;

            __version = result.version;

            if ((result.flag & ScrollRectEvent.Flag.Changed) == ScrollRectEvent.Flag.Changed)
            {
                if (onChanged != null)
                    onChanged.Invoke(result.index);

                index = (int2)math.round(result.index);
            }

            if ((result.flag & ScrollRectEvent.Flag.SameAsInfo) == ScrollRectEvent.Flag.SameAsInfo)
                this.RemoveComponent<ScrollRectInfo>();
        }

        private bool __EnableNode(float2 normalizedPosition)
        {
            ScrollRect scrollRect = this.scrollRect;
            if (scrollRect == null)
                return false;

            ScrollRectData data = this.data;
            ScrollRectNode node;
            node.velocity = scrollRect.velocity;
            node.normalizedPosition = normalizedPosition;// scrollRect.normalizedPosition;
            node.index = data.GetIndex(normalizedPosition);

            this.AddComponentData(node);

            int2 index = (int2)math.round(node.index);
            if (math.any(index != this.index))
            {
                if (onChanged != null)
                    onChanged.Invoke(node.index);

                this.index = index;
            }

            return true;
        }

        private void __DisableNode()
        {
            this.RemoveComponent<ScrollRectNode>();
        }

        private void __UpdateData()
        {
            Canvas.willRenderCanvases -= __UpdateData;

            if(this != null)
                UpdateData();
        }

        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
        {
            __DisableNode();
        }

        void IEndDragHandler.OnEndDrag(PointerEventData eventData)
        {
            __EnableNode(scrollRect.normalizedPosition);
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            ScrollRect scrollRect = this.scrollRect;
            if (scrollRect == null)
                return;

            float2 source = __data.GetIndex(scrollRect.normalizedPosition);
            int2 destination = (int2)math.round(source);
            if(math.any(destination != this.index))
            {
                if (onChanged != null)
                    onChanged.Invoke(source);

                this.index = destination;
            }
        }

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            ScrollRectEvent result;
            result.version = 0;
            result.flag = 0;
            result.index = math.int2(-1, -1);
            assigner.SetComponentData(entity, result);
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ScrollRectSystem : SystemBase
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

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(
                ComponentType.ReadOnly<ScrollRectData>(),
                ComponentType.ReadOnly<ScrollRectNode>());
        }

        protected override void OnUpdate()
        {
            ComputeNodesEx computeNodes;
            computeNodes.deltaTime = World.Time.DeltaTime;
            computeNodes.entityType = GetEntityTypeHandle();
            computeNodes.instanceType = GetComponentTypeHandle<ScrollRectData>(true);
            computeNodes.infoType = GetComponentTypeHandle<ScrollRectInfo>(true);
            computeNodes.nodeType = GetComponentTypeHandle<ScrollRectNode>();
            computeNodes.events = GetComponentLookup<ScrollRectEvent>();
            Dependency = computeNodes.ScheduleParallel(__group, Dependency);
        }
    }
    
    [UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup))]
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
                var scrollRect = instances[index].value.scrollRect;
                if(scrollRect != null)
                    scrollRect.normalizedPosition = nodes[index].normalizedPosition;
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

        protected override void OnCreate()
        {
            base.OnCreate();

            __nodeGroup = GetEntityQuery(ComponentType.ReadOnly<ScrollRectNode>(), ComponentType.ReadOnly<EntityObject<ScrollRectComponent>>());
            __eventGroup = GetEntityQuery(ComponentType.ReadOnly<ScrollRectEvent>(), ComponentType.ReadOnly<EntityObject<ScrollRectComponent>>());
            __eventGroup.SetChangedVersionFilter(typeof(ScrollRectEvent));
        }

        protected override void OnUpdate()
        {
            var instanceType = GetComponentTypeHandle<EntityObject<ScrollRectComponent>>(true);

            MoveEx move;
            move.instanceType = instanceType;
            move.nodeType = GetComponentTypeHandle<ScrollRectNode>(true);
            move.RunByRefWithoutJobs(__nodeGroup);

            InvokeEx invoke;
            invoke.instanceType = instanceType;
            invoke.eventType = GetComponentTypeHandle<ScrollRectEvent>(true);
            invoke.RunByRefWithoutJobs(__eventGroup);
        }
    }
}