using UnityEngine;

namespace ZG
{
    public class ScrollRectToggle : MonoBehaviour
    {
        public UnityEngine.Events.UnityEvent onSelected;

        public ScrollRectComponentEx handler
        {
            get;

            internal set;
        }

        public int index
        {
            get;

            internal set;
        }

        public void Select()
        {
            ScrollRectComponentEx handler = this.handler;
            if (handler != null)
                handler.MoveTo(index);
        }
    }
}