using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZG
{
    public abstract class Selectable : MonoBehaviour, IPointerClickHandler
    {
        [Flags]
        public enum Flag
        {
            Update = 0x01, 
            Hold = 0x02
        }

        private partial class SelectionSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                if (__selection == null)
                    return;

                EventSystem eventSystem = EventSystem.current;
                GameObject gameObject = eventSystem == null ? null : eventSystem.currentSelectedGameObject;
                Transform temp = gameObject == null ? null : gameObject.transform;
                if (temp != null && temp != __selection.transform && (!(temp is RectTransform) || temp.parent == null))
                    __selection.isSelected = false;
                else if (__selection.isHold && 
                    ((__selection.__flag & Flag.Update) != Flag.Update || __selection._Update()) &&
                    (__selection.__handler == null || !__selection.__handler()))
                    __selection.__flag = 0;
            }
        }

        //public event Action onUpdate;

        public static event Action<Selectable> onChanged;

        public event Action onDeselected;

        public Func<bool> onSelected;

        public Predicate<PointerEventData> onSelect;
        
        private Flag __flag;

        private Func<bool> __handler;

        private static Selectable __selection;

        public static Selectable selection
        {
            get
            {
                return __selection;
            }
        }

        public virtual bool isSelected
        {
            get
            {
                return this == __selection;
            }

            set
            {
                if (value == isSelected)
                    return;

                __flag = 0;

                if (value)
                {
                    if (!isActiveAndEnabled)
                        return;

                    if (__selection != null)
                        __selection.isSelected = false;

                    UnityUtility.TouchFeedback(TouchFeedbackType.Selection);

                    if (onChanged != null)
                        onChanged(this);

                    __selection = this;

                    EventSystem eventSystem = EventSystem.current;
                    if (eventSystem != null)
                        eventSystem.SetSelectedGameObject(gameObject);
                }
                else if (__selection == this)
                {
                    if (onDeselected != null)
                        onDeselected();

                    if (onChanged != null)
                        onChanged(null);

                    __selection = null;
                }
            }
        }

        public bool isHold
        {
            get => (__flag & Flag.Hold) == Flag.Hold;

            set
            {
                if (value)
                    __flag |= Flag.Hold;
                else
                    __flag = 0;
            }
        }
        
        public void Select(Func<bool> handler, Flag flag)
        {
            isSelected = true;

            __flag = flag;

            __handler = handler == null ? onSelected : handler;

            if ((flag & Flag.Update) == Flag.Update)
                _Update();
        }

        public void Select(Func<bool> handler)
        {
            Select(handler, Flag.Update | Flag.Hold);
        }

        public void Select()
        {
            Select(onSelected);
        }

        /*public void Awake()
        {
            onUpdate += __Update;
        }

        public void OnEnable()
        {
            FrameObjectManager.onUpdate += __OnUpdate;
        }*/

        public void OnDisable()
        {
            //FrameObjectManager.onUpdate -= __OnUpdate;
            
            isSelected = false;
        }

        /*private void __OnUpdate()
        {
            if (!isActiveAndEnabled)
                return;

            if (onUpdate != null)
                onUpdate();
        }

        private void __Update()
        {
            if (__isSelected)
            {
                EventSystem eventSystem = EventSystem.current;
                GameObject gameObject = eventSystem == null ? null : eventSystem.currentSelectedGameObject;
                Transform temp = gameObject == null ? null : gameObject.transform;
                if (temp != null && temp != transform && (!(temp is RectTransform) || temp.parent == null))
                {
                    if (__isHold)
                    {
                        __isHold = false;

                        //GameClientPlayer.SetDirection(Vector3.zero);
                    }

                    isSelected = false;
                }
                else if (isHold)
                {
                    if (__isCheckRange)
                    {
                        Vector3 position = targetPosition;
                        if (Test(ref position))
                            __isHold = __handler != null && __handler();
                        else if (transform.hasChanged)
                        {
                            transform.hasChanged = false;

                            targetPosition = position;
                        }
                    }
                    else
                        __isHold = __handler != null && __handler();
                }
            }
        }*/

        protected virtual bool _Update()
        {
            return true;
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            /*if (Input.touchCount > 1 || eventData == null || eventData.button != PointerEventData.InputButton.Left || eventData.dragging)
            {
                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.currentSelectedGameObject == gameObject)
                    eventSystem.SetSelectedGameObject(null);
            }
            else if (onSelect == null || onSelect(eventData))
                Select();*/

            isSelected = true;
        }
    }
}