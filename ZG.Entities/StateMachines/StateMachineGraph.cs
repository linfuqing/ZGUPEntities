using System;
using UnityEngine;

namespace ZG
{
    public interface IStateMachineNode
    {
        void Enable(StateMachineComponentEx instance);

        void Disable(StateMachineComponentEx instance);
    }

    public abstract class StateMachineNode : ScriptableObject
    {
        public abstract void Enable(StateMachineComponentEx instance);

        public abstract void Disable(StateMachineComponentEx instance);
    }

    [CreateAssetMenu(menuName = "ZG/State Machine Graph")]
    public class StateMachineGraph : ScriptableObject
    {
        [Serializable]
        public struct Group
        {
#if UNITY_EDITOR
            [HideInInspector]
            public float width;

            [HideInInspector]
            public Vector2 position;
#endif

            public int[] nodeIndices;
        }

        [Serializable]
        public class Groups : Map<Group>
        {
        }

        [HideInInspector]
        public Groups groups;

        [HideInInspector]
        public StateMachineNode[] nodes;

        public void Enable(StateMachineComponentEx instance, string groupName)
        {
            if (groups == null || string.IsNullOrEmpty(groupName))
                return;

            int numNodes = nodes == null ? 0 : nodes.Length;
            if (numNodes < 1)
                return;

            Group group;
            if (!groups.TryGetValue(groupName, out group))
                return;

            StateMachineNode node;
            foreach (int nodeIndex in group.nodeIndices)
            {
                node = nodeIndex < 0 || nodeIndex >= numNodes ? null : nodes[nodeIndex];
                if (node == null)
                    continue;

                node.Enable(instance);
            }
        }

        public void Disable(StateMachineComponentEx instance, string groupName)
        {
            if (groups == null || string.IsNullOrEmpty(groupName))
                return;

            int numNodes = nodes == null ? 0 : nodes.Length;
            if (numNodes < 1)
                return;

            Group group;
            if (!groups.TryGetValue(groupName, out group))
                return;

            StateMachineNode node;
            foreach (int nodeIndex in group.nodeIndices)
            {
                node = nodeIndex < 0 || nodeIndex >= numNodes ? null : nodes[nodeIndex];
                if (node == null)
                    continue;

                node.Disable(instance);
            }
        }
    }
}