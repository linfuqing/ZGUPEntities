using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZG
{
    public class ScrollRectComponentEx : ScrollRectComponent
    {
        public ActiveEvent onPreviousChanged;
        public ActiveEvent onNextChanged;

        public ScrollRectToggle toggleStyle;

        private bool __isMoving;
        private int2 __selectedIndex = IndexNull;
        private IReadOnlyList<ISubmitHandler> __submitHandlers;
        private Dictionary<ISubmitHandler, ScrollRectToggle> __toggles;

        private static readonly int2 IndexNull = new int2(-1, -1);
        
        public override int2 count
        {
            get
            {
                int version = base.version;
                __submitHandlers = base.submitHandlers;
                int count = __submitHandlers == null ? 0 : __submitHandlers.Count;
                RectTransform.Axis axis = scrollRect.horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical;
                int2 result = int2.zero;
                result[(int)axis] = count;
                if (version == base.version || toggleStyle == null)
                    return result;
                
                ISubmitHandler submitHandler;
                if (count > 0)
                {
                    ScrollRectToggle toggle;
                    if (__toggles == null)
                        __toggles = new Dictionary<ISubmitHandler, ScrollRectToggle>();
                    else
                    {
                        bool isContains;
                        List<ISubmitHandler> submitHandlersToDestroy = null;
                        foreach (var pair in __toggles)
                        {
                            submitHandler = pair.Key;

                            isContains = false;
                            foreach (var submitHandlerToDestroy in __submitHandlers)
                            {
                                if (submitHandlerToDestroy == submitHandler)
                                {
                                    isContains = true;

                                    break;
                                }
                            }
                            
                            if (!isContains)
                            {
                                toggle = pair.Value;
                                if (toggle != null)
                                    Destroy(toggle.gameObject);

                                if (submitHandlersToDestroy == null)
                                    submitHandlersToDestroy = new List<ISubmitHandler>();
                                
                                submitHandlersToDestroy.Add(submitHandler);
                            }
                        }

                        if (submitHandlersToDestroy != null)
                        {
                            foreach (var submitHandlerToDestroy in submitHandlersToDestroy)
                                __toggles.Remove(submitHandlerToDestroy);
                        }
                    }

                    GameObject gameObject;
                    Transform parent = toggleStyle == null ? null : toggleStyle.transform;
                    parent = parent == null ? null : parent.parent;
                    for(int i = 0; i < count; ++i)
                    {
                        submitHandler = __submitHandlers[i];
                        if(submitHandler == null)
                            continue;
                        
                        if (__toggles.TryGetValue(submitHandler, out toggle) && toggle != null)
                        {
                            toggle.transform.SetSiblingIndex(i + 1);

                            continue;
                        }

                        toggle = Instantiate(toggleStyle, parent);
                        if (toggle == null)
                            continue;

                        toggle.handler = this;
                        toggle.index = i;
                        
                        toggle.transform.SetSiblingIndex(i + 1);

                        gameObject = toggle.gameObject;
                        if (gameObject != null)
                            gameObject.SetActive(true);

                        __toggles[submitHandler] = toggle;
                    }

                    int2 temp = selectedIndex;
                    int min = 0, max = count - 1, index = math.clamp(math.max(temp.x, temp.y), min, max);
                    submitHandler = __submitHandlers[index];
                    if (submitHandler != null && 
                        __toggles.TryGetValue(submitHandler, out toggle) && 
                        toggle != null && 
                        toggle.onSelected != null)
                    {
                        __isMoving = true;
                        toggle.onSelected.Invoke();
                        __isMoving = false;
                    }

                    if (onPreviousChanged != null)
                        onPreviousChanged.Invoke(index > min);

                    if (onNextChanged != null)
                        onNextChanged.Invoke(index < max);
                }
                else
                {
                    if (__toggles != null)
                    {
                        foreach (var toogle in __toggles.Values)
                        {
                            if (toogle != null)
                                Destroy(toogle.gameObject);
                        }

                        __toggles.Clear();
                    }

                    if (onPreviousChanged != null)
                        onPreviousChanged.Invoke(false);

                    if (onNextChanged != null)
                        onNextChanged.Invoke(false);
                }

                return result;
            }
        }

        public int2 selectedIndex
        {
            get
            {
                return math.all(__selectedIndex == IndexNull) ? index : math.min(__selectedIndex, __toggles == null ? 0 : __toggles.Count - 1);
            }
        }

        public int axis
        {
            get
            {
                ScrollRect scrollRect = base.scrollRect;
                int axis = scrollRect != null && scrollRect.horizontal ? 0 : 1;
                return axis;
            }
        }

        public ScrollRectComponentEx()
        {
            onChanged += __OnChanged;
        }

        public ScrollRectToggle Get(int index)
        {
            return index < 0 || index >= (__toggles == null ? 0 : __toggles.Count) ? null : __toggles[__submitHandlers[index]];
        }

        public void Next()
        {
            Move(1);
        }

        public void Previous()
        {
            Move(-1);
        }

        public void SetTo(int index) => MoveTo(index);

        public void Move(int offset)
        {
            int2 index = selectedIndex;
            index[axis] += offset;

            MoveTo(index);
        }

        public void MoveTo(int index)
        {
            int2 result = int2.zero;
            result[axis] = index;

            MoveTo(result);
        }

        public override void MoveTo(in int2 destination)
        {
            if (__isMoving)
                return;

            int2 source = selectedIndex;
            /*if (math.all(source == destination))
                return false;*/
            if (source.Equals(destination))
                return;
            
            __Update(math.max(source.x, source.y), math.max(destination.x, destination.y));

            __selectedIndex = destination;
            
            base.MoveTo(destination);
        }

        public override void UpdateData()
        {
            __selectedIndex = IndexNull;

            base.UpdateData();
        }

        private void __OnChanged(float2 index)
        {
            __OnChanged((int2)math.round(index));
        }

        private void __OnChanged(int2 source)
        {
            bool isNull = math.all(__selectedIndex == IndexNull);
            if (!isNull && !math.all(math.min(__selectedIndex, __toggles == null ? 0 : __toggles.Count - 1) == source))
                return;

            if (isNull)
            {
                int length = __toggles == null ? 0 : __toggles.Count, index = math.clamp(math.max(source.x, source.y), 0, length - 1);
                int2 destination = base.index;
                __Update(math.max(destination.x, destination.y), index);
            }
            else
                __selectedIndex = IndexNull;
        }

        private void __Update(int source, int destination)
        {
            int length = __toggles == null ? 0 : __toggles.Count, min = 0, max = length - 1;
            ScrollRectToggle toggle = length > destination ? __toggles[__submitHandlers[destination]] : null;
            if (toggle != null && toggle.onSelected != null)
            {
                __isMoving = true;
                toggle.onSelected.Invoke();
                __isMoving = false;
            }

            if (destination <= min)
            {
                if (onPreviousChanged != null)
                    onPreviousChanged.Invoke(false);
            }
            else if (source <= min)
            {
                if (onPreviousChanged != null)
                    onPreviousChanged.Invoke(true);
            }

            if (destination >= max)
            {
                if (onNextChanged != null)
                    onNextChanged.Invoke(false);
            }
            else if (source >= max)
            {
                if (onNextChanged != null)
                    onNextChanged.Invoke(true);
            }
        }
    }
}