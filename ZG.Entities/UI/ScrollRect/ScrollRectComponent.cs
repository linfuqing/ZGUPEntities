using System;
using System.Collections.Generic;
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
        private List<ISubmitHandler> __submitHandlers;

        public virtual int2 count
        {
            get
            {
                ScrollRect scrollRect = this.scrollRect;
                RectTransform content = scrollRect == null ? null : scrollRect.content;
                if (content == null)
                    return int2.zero;

                if (__submitHandlers == null)
                    __submitHandlers = new List<ISubmitHandler>();

                bool isChanged = false;
                int index = 0;
                ISubmitHandler submitHandler;
                GameObject gameObject;
                foreach (Transform child in content)
                {
                    gameObject = child.gameObject;
                    if (gameObject != null && gameObject.activeSelf)
                    {
                        submitHandler = gameObject.GetComponent<ISubmitHandler>();

                        if (index < __submitHandlers.Count)
                        {
                            if (submitHandler != __submitHandlers[index])
                            {
                                __submitHandlers[index] = submitHandler;

                                isChanged = true;
                            }
                        }
                        else
                        {
                            __submitHandlers.Add(submitHandler);

                            isChanged = true;
                        }

                        ++index;
                    }
                }

                int numSubmitHandlers = __submitHandlers.Count;
                if (index < numSubmitHandlers)
                {
                    __submitHandlers.RemoveRange(index, numSubmitHandlers - index);

                    isChanged = true;
                }

                if (isChanged)
                    ++__version;

                RectTransform.Axis axis = scrollRect.horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;
                int2 result = int2.zero;
                result[(int)axis] = __submitHandlers.Count;

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

                //Canvas.ForceUpdateCanvases();

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

        public virtual int __ToSubmitIndex(in int2 index)
        {
            RectTransform.Axis axis = scrollRect.horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;

            return index[(int)axis];
        }

        public virtual void UpdateData()
        {
            //__data = data;

            //this.SetComponentData(__data);

            __EnableNode(float2.zero);

            __info.index = 0;// math.clamp(__info.index, 0, math.max(1, __data.count) - 1);
        }

        public virtual void MoveTo(in int2 index)
        {
            ScrollRectInfo info;
            info.isVail = true;
            info.index = index;
            __info = info;
            //this.AddComponentData(info);
        }

        protected void Start()
        {
            __event.version = 0;
            __event.flag = 0;
            __event.index = math.int2(-1, -1);

            UpdateData();
        }

        protected void Update()
        {
            if (__node != null)
            {
                __data = data;

                var index = math.clamp(__info.index, 0, math.max(1, __data.count) - 1);
                if (!index.Equals(__info.index))
                {
                    __info.index = index;

                    ++__version;
                }

                var node = __node.Value;
                if (ScrollRectUtility.Execute(__version, Time.deltaTime, __data, __info, ref node, ref __event))
                    _Set(__event);

                //if(!node.normalizedPosition.Equals(__node.Value.normalizedPosition) || !((float2)scrollRect.normalizedPosition).Equals(node.normalizedPosition))
                scrollRect.normalizedPosition = node.normalizedPosition;

                __node = node;
            }
        }

        internal void _Set(in ScrollRectEvent result)
        {
            if (__version == result.version)
                return;

            __version = result.version;

            if ((result.flag & ScrollRectEvent.Flag.Changed) == ScrollRectEvent.Flag.Changed)
            {
                var index = (int2)math.round(result.index);

                __OnChanged(result.index, index);

                this.index = index;
            }

            if ((result.flag & ScrollRectEvent.Flag.SameAsInfo) == ScrollRectEvent.Flag.SameAsInfo)
                __info.isVail = false;//this.RemoveComponent<ScrollRectInfo>();
        }

        private bool __EnableNode(in float2 normalizedPosition)
        {
            ScrollRect scrollRect = this.scrollRect;
            if (scrollRect == null)
                return false;

            __data = data;

            ScrollRectNode node;
            node.velocity = scrollRect.velocity;
            node.normalizedPosition = normalizedPosition;// scrollRect.normalizedPosition;
            node.index = __data.GetIndex(normalizedPosition);

            __node = node;
            //this.AddComponentData(node);

            int2 index = (int2)math.round(node.index);
            if (math.any(index != this.index))
            {
                __OnChanged(node.index, index);

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
            if (math.any(destination != this.index))
            {
                __OnChanged(source, destination);

                this.index = destination;
            }
        }

        private void __OnChanged(in float2 indexFloat, in int2 indexInt)
        {
            if (onChanged != null)
                onChanged.Invoke(indexFloat);

            if (__submitHandlers != null)
            {
                int index = __ToSubmitIndex(indexInt);
                var submitHandler = index >= 0 && index < __submitHandlers.Count ? __submitHandlers[index] : null;
                if (submitHandler != null)
                    submitHandler.OnSubmit(new BaseEventData(EventSystem.current));
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

            public void Execute(float deltaTime)
            {
                int2 source = (int2)math.round(node.index),
                    destination = info.index;// math.clamp(info.index, 0, instance.count - 1);
                float2 length = instance.length,
                         cellLength = instance.cellLength,
                         offset = instance.GetOffset(cellLength),
                         distance = node.normalizedPosition * length - destination * cellLength + offset;

                if (info.isVail)
                {
                    float t = math.pow(instance.decelerationRate, deltaTime);
                    //t = t * t* (3.0f - (2.0f * t));
                    node.velocity = math.lerp(node.velocity, distance / instance.elasticity, t);

                    //velocity *= math.pow(instance.decelerationRate, deltaTime);

                    //node.velocity = velocity;

                    //velocity += distance / instance.elasticity;

                    node.normalizedPosition -= math.select(float2.zero, node.velocity / length, length > math.FLT_MIN_NORMAL) * deltaTime;

                    node.index = instance.GetIndex(node.normalizedPosition, length, cellLength, offset);
                }
                else
                    node.normalizedPosition -= math.select(float2.zero, distance / length, length > math.FLT_MIN_NORMAL);

                int2 target = (int2)math.round(node.index);

                ScrollRectEvent.Flag flag = 0;
                if (!math.all(source == target))
                    flag |= ScrollRectEvent.Flag.Changed;

                if (info.isVail && math.all(destination == target))
                {
                    flag |= ScrollRectEvent.Flag.SameAsInfo;

                    node.velocity = float2.zero;
                }

                //nodes[index] = node;

                if (flag != 0)
                {
                    ++result.version;
                    result.flag = flag;
                    result.index = node.index;
                }
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
        private static unsafe void __Execute(float deltaTime, Data* data)
        {
            data->Execute(deltaTime);
        }
    }
}