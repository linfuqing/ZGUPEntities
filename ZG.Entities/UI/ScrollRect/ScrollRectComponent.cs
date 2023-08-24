using System;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZG
{
    public struct ScrollRectData
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

    public struct ScrollRectInfo
    {
        public bool isVail;
        public int2 index;
    }
    
    public struct ScrollRectNode
    {
        public float2 velocity;
        public float2 normalizedPosition;
        public float2 index;
    }

    public struct ScrollRectEvent
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

    public class ScrollRectComponent : MonoBehaviour, IBeginDragHandler, IEndDragHandler, IDragHandler
    {
        public event Action<float2> onChanged;

        private int __version = 0;

        private ScrollRectData __data;
        private ScrollRectInfo __info;
        private ScrollRectNode? __node;
        private ScrollRectEvent __event;

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

            //this.SetComponentData(__data);

            __EnableNode(float2.zero);

            return true;
        }

        public virtual bool MoveTo(int2 index)
        {
            ScrollRectInfo info;
            info.isVail = true;
            info.index = index;
            __info = info;
            //this.AddComponentData(info);

            return true;
        }

        protected void Start()
        {
            __event.version = 0;
            __event.flag = 0;
            __event.index = math.int2(-1, -1);
        }

        protected void Update()
        {
            if (__node != null)
            {
                __data = data;

                var node = __node.Value;
                if (ScrollRectUtility.Execute(__version, Time.deltaTime, __data, __info, ref node, ref __event))
                    _Set(__event);

                scrollRect.normalizedPosition = node.normalizedPosition;

                __node = node;
            }
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
                __info.isVail = false;//this.RemoveComponent<ScrollRectInfo>();
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

            __node = node;
            //this.AddComponentData(node);

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
            __node = null;
            //this.RemoveComponent<ScrollRectNode>();
        }

        /*private void __UpdateData()
        {
            Canvas.willRenderCanvases -= __UpdateData;

            if(this != null)
                UpdateData();
        }*/

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

        /*void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            ScrollRectEvent result;
            result.version = 0;
            result.flag = 0;
            result.index = math.int2(-1, -1);
            assigner.SetComponentData(entity, result);
        }*/
    }

    [BurstCompile]
    public static class ScrollRectUtility
    {
        private struct Data
        {
            public ScrollRectData instance;
            public ScrollRectInfo info;
            public ScrollRectNode node;
            public ScrollRectEvent result;

            public Data(
                in ScrollRectData instance,
                in ScrollRectInfo info,
                ref ScrollRectNode node,
                ref ScrollRectEvent result)
            {
                this.instance = instance;
                this.info = info;
                this.node = node;
                this.result = result;
            }
        }

        public static unsafe bool Execute(
            int version, 
            float deltaTime,
            in ScrollRectData instance,
            in ScrollRectInfo info,
            ref ScrollRectNode node,
            ref ScrollRectEvent result)
        {
            var data = new Data(instance, info, ref node, ref result);

            __Execute(deltaTime, (Data*)Unity.Collections.LowLevel.Unsafe.UnsafeUtility.AddressOf(ref data));

            node = data.node;

            if (version != data.result.version)
            {
                result = data.result;

                return true;
            }

            return false;
        }

        [BurstCompile]
        private static unsafe void __Execute(
            float deltaTime,
            Data* data)
        {
            bool isExistsInfo = data->info.isVail;
            int2 source = (int2)math.round(data->node.index), destination = isExistsInfo ? data->info.index : source;
            float2 length = data->instance.length,
                     cellLength = data->instance.cellLength,
                     offset = data->instance.GetOffset(cellLength),
                     distance = data->node.normalizedPosition * length - destination * cellLength + offset;

            float t = math.pow(data->instance.decelerationRate, deltaTime);
            //t = t * t* (3.0f - (2.0f * t));
            data->node.velocity = math.lerp(data->node.velocity, distance / data->instance.elasticity, t);

            //velocity *= math.pow(instance.decelerationRate, deltaTime);

            //node.velocity = velocity;

            //velocity += distance / instance.elasticity;

            data->node.normalizedPosition -= math.select(float2.zero, data->node.velocity / length, length > math.FLT_MIN_NORMAL) * deltaTime;

            data->node.index = data->instance.GetIndex(data->node.normalizedPosition, length, cellLength, offset);

            int2 target = (int2)math.round(data->node.index);

            ScrollRectEvent.Flag flag = 0;
            if (!math.all(source == target))
                flag |= ScrollRectEvent.Flag.Changed;

            if (isExistsInfo && math.all(destination == target))
            {
                flag |= ScrollRectEvent.Flag.SameAsInfo;

                data->node.velocity = float2.zero;
            }

            //nodes[index] = node;

            if (flag != 0)
            {
                ++data->result.version;
                data->result.flag = flag;
                data->result.index = data->node.index;
            }
        }
    }
}