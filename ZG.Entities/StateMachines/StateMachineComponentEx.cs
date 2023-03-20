using UnityEngine;

namespace ZG
{
    public class StateMachineComponentEx : StateMachineComponent
    {
        private bool __isActive;

        [SerializeField]
        internal string _groupName;

        [SerializeField]
        internal StateMachineGraph _graph;

        public string groupName
        {
            get
            {
                return _groupName;
            }

            set
            {

                if (_groupName == value)
                    return;
                
                Break();

                if (__isActive && _graph != null)
                {
                    _graph.Disable(this, _groupName);
                    _graph.Enable(this, value);
                }

                _groupName = value;
            }
        }

        public StateMachineGraph graph
        {
            get
            {
                return _graph;
            }

            set
            {
                if (_graph == value)
                    return;

                Break();

                if (__isActive)
                {
                    if (_graph != null)
                        _graph.Disable(this, _groupName);

                    if (value != null)
                        value.Enable(this, _groupName);
                }

                _graph = value;
            }
        }

        private void Awake()
        {
            gameObjectEntity.onCreated += __OnCreated;
        }

        protected void OnEnable()
        {
            //base.OnEnable();

            if (!__isActive && gameObjectEntity.isCreated)
            {
                if (_graph != null)
                    _graph.Enable(this, _groupName);
                
                __isActive = true;
            }
        }

        protected void OnDisable()
        {
            if (__isActive)
            {
                if (gameObjectEntity.isCreated)
                {
                    if (_graph != null)
                        _graph.Disable(this, _groupName);
                }

                __isActive = false;
            }

            //base.OnDisable();
        }

        private void __OnCreated()
        {
            if(!__isActive && isActiveAndEnabled)
            {
                if (_graph != null)
                    _graph.Enable(this, _groupName);

                __isActive = true;
            }
        }
    }
}