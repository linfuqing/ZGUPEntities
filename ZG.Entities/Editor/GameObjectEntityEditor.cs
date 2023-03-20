using UnityEngine;
using UnityEditor;

namespace ZG
{
    [CustomEditor(typeof(GameObjectEntity))]
    public class GameObjectEntityEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if(GUILayout.Button("Refresh"))
                ((GameObjectEntity)target).Refresh();
        }
    }
}