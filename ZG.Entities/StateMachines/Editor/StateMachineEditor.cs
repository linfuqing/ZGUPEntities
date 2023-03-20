using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace ZG
{
    public class StateMachineEditor : EditorWindow
    {
        private const string __NAME_SPACE_GRAPH = "StateMachineGraph";

        public static StateMachineGraph graph
        {
            get
            {
                return AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(__NAME_SPACE_GRAPH))) as StateMachineGraph;
            }

            set
            {
                if (value)
                    EditorPrefs.SetString(__NAME_SPACE_GRAPH, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)));
                else
                    EditorPrefs.DeleteKey(__NAME_SPACE_GRAPH);
            }
        }
        
        private class Group
        {
            private bool __isRename;
            private int __renameIndex;
            private string __name;
            
            private StateMachineGraph.Group __group;

            private StateMachineGraph __graph;
            private ReorderableList __reorderableList;

            private Vector2? __offset;

            public string name
            {
                get
                {
                    return __name;
                }
            }
            
            public Group(string name, StateMachineGraph graph)
            {
                __isRename = false;
                __renameIndex = -1;
                __name = name;
                __group = default;
                __graph = graph;
                __reorderableList = null;
            }

            public bool Draw()
            {
                if (__name == null || __graph == null || __graph.groups == null || !__graph.groups.TryGetValue(__name, out __group))
                    return false;
                
                if (__reorderableList == null)
                {
                    __reorderableList = new ReorderableList(__group.nodeIndices, typeof(int), true, true, true, false);

                    __reorderableList.headerHeight = EditorGUIUtility.singleLineHeight;
                    __reorderableList.drawHeaderCallback = __DrawHeader;
                    __reorderableList.drawElementCallback = __DrawElement;
                    __reorderableList.onAddCallback = __Add;
                    __reorderableList.onReorderCallback = __Reorder;
                }
                else
                    __reorderableList.list = __group.nodeIndices;

                Rect rect = new Rect(__group.position, new Vector2(Mathf.Max(__group.width, EditorGUIUtility.labelWidth), __reorderableList.GetHeight()));
                __reorderableList.DoList(rect);
                
                Event current = Event.current;
                if (current != null)
                {
                    Vector2 mousePosition = current.mousePosition;
                    switch (current.type)
                    {
                        case EventType.MouseDown:
                            if (!rect.Contains(mousePosition))
                                break;

                            __offset = __group.position - mousePosition;
                            break;
                        case EventType.MouseDrag:
                            if (__offset == null)
                                break;

                            __group.position = mousePosition + __offset.Value;

                            __graph.groups[__name] = __group;
                            return true;
                        case EventType.MouseUp:
                            if (__offset == null)
                                break;

                            __offset = null;

                            EditorUtility.SetDirty(__graph);
                            break;
                        case EventType.ContextClick:
                            if (!rect.Contains(mousePosition))
                                break;

                            current.Use();

                            GenericMenu genericMenu = new GenericMenu();

                            genericMenu.AddItem(new GUIContent("Rename Group"), false, __RenameGroup);

                            genericMenu.AddItem(new GUIContent("Destroy Group"), false, __DestroyGroup);

                            genericMenu.ShowAsContext();

                            break;
                    }
                }

                return false;
            }

            private void __DestroyGroup()
            {
                if (__graph == null || __graph.groups == null)
                    return;

                __graph.groups.Remove(__name);

                EditorUtility.SetDirty(__graph);
            }

            private void __RenameGroup()
            {
                __isRename = true;

                __renameIndex = -1;
            }

            private void __RenameNode(object nodeIndex)
            {
                __isRename = true;

                __renameIndex = (int)nodeIndex;
            }

            private void __RemoveNode(object nodeIndex)
            {
                if (__name == null || __graph == null || __graph.groups == null || !__graph.groups.TryGetValue(__name, out __group))
                    return;

                ArrayUtility.Remove(ref __group.nodeIndices, (int)nodeIndex);

                __graph.groups[__name] = __group;

                EditorUtility.SetDirty(__graph);
            }

            private void __DestroyNode(object nodeIndex)
            {
                if (__name == null || __graph == null || __graph.nodes == null)
                    return;
                
                int index = (int)nodeIndex;
                if (__graph.nodes.Length <= index)
                    return;

                if (__graph.groups != null && __graph.groups.TryGetValue(__name, out __group))
                {
                    ArrayUtility.Remove(ref __group.nodeIndices, index);

                    __graph.groups[__name] = __group;
                }

                StateMachineNode node = __graph.nodes[index];
                
                ArrayUtility.RemoveAt(ref __graph.nodes, index);

                if (__graph.groups != null)
                {
                    int i, numNodeIndices, temp;
                    StateMachineGraph.Group group;
                    List<string> names = new List<string>(__graph.groups.Keys);
                    foreach (string name in names)
                    {
                        group = __graph.groups[name];
                        numNodeIndices = group.nodeIndices == null ? 0 : group.nodeIndices.Length;
                        for(i = 0; i < numNodeIndices; ++i)
                        {
                            temp = group.nodeIndices[i];
                            if (temp < index)
                                continue;

                            if (temp > index)
                                group.nodeIndices[i] = temp - 1;
                            else
                                ArrayUtility.RemoveAt(ref group.nodeIndices, i);
                        }

                        __graph.groups[name] = group;
                    }
                }

                AssetDatabase.RemoveObjectFromAsset(node);

                EditorUtility.SetDirty(__graph);
            }

            private void __Add(object nodeIndex)
            {
                if (__name == null || __graph == null || __graph.groups == null || !__graph.groups.TryGetValue(__name, out __group))
                    return;
                
                ArrayUtility.Add(ref __group.nodeIndices, (int)nodeIndex);

                __graph.groups[__name] = __group;
                
                EditorUtility.SetDirty(__graph);
            }

            private void __Add(ReorderableList list)
            {
                int numNodes = __graph == null || __graph.nodes == null ? 0 : __graph.nodes.Length;
                StateMachineNode node;
                HashSet<Type> types = new HashSet<Type>();
                foreach (int nodeIndex in __group.nodeIndices)
                {
                    node = nodeIndex < 0 || nodeIndex >= numNodes ? null : __graph.nodes[nodeIndex];
                    if (node == null)
                        continue;
                    
                    types.Add(node.GetType());
                }

                GenericMenu genericMenu = null;
                string name;
                string[] names = new string[numNodes];
                for (int i = 0; i < numNodes; ++i)
                {
                    node = __graph.nodes[i];
                    if (node == null || types.Contains(node.GetType()))
                        continue;

                    name = node.name;
                    names[i] = name;
                    
                    if (genericMenu == null)
                        genericMenu = new GenericMenu();

                    genericMenu.AddItem(new GUIContent("Add/" + name), false, __Add, i);
                }
                
                string path = AssetDatabase.GetAssetPath(graph);
                IEnumerable<Type> loadedTypes = EditorHelper.loadedTypes;
                foreach (Type loadedType in loadedTypes)
                {
                    if (loadedType == null || !loadedType.IsSubclassOf(typeof(StateMachineNode)) || loadedType.IsAbstract || types.Contains(loadedType))
                        continue;

                    if (genericMenu == null)
                        genericMenu = new GenericMenu();

                    Type type = loadedType;
                    string typeName = ObjectNames.NicifyVariableName(type.Name);
                    genericMenu.AddItem(new GUIContent("Create/" + typeName), false, () =>
                    {
                        if (__name == null || __graph == null || !__graph.groups.TryGetValue(__name, out __group))
                            return;

                        StateMachineNode result = CreateInstance(type) as StateMachineNode;
                        if (result == null)
                            return;

                        result.name = ObjectNames.GetUniqueName(names, typeName);
                        result.hideFlags = HideFlags.HideInHierarchy;

                        AssetDatabase.AddObjectToAsset(result, path);
                        
                        ArrayUtility.Add(ref __group.nodeIndices,  __graph.nodes == null ? 0 : __graph.nodes.Length);

                        if (__graph.nodes == null)
                        {
                            __graph.nodes = new StateMachineNode[1];
                            __graph.nodes[0] = result;
                        }
                        else
                            ArrayUtility.Add(ref __graph.nodes, result);

                        __graph.groups[__name] = __group;

                        EditorUtility.SetDirty(__graph);
                    });
                }

                if (genericMenu != null)
                    genericMenu.ShowAsContext();
            }

            private void __Reorder(ReorderableList list)
            {
                EditorUtility.SetDirty(__graph);
            }

            private void __DrawHeader(Rect rect)
            {
                if (__isRename && __renameIndex == -1)
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUI.DelayedTextField(rect, __name);
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (__graph != null && __graph.groups != null)
                        {
                            __graph.groups.Remove(__name);
                            __graph.groups[name] = __group;

                            EditorUtility.SetDirty(__graph);
                        }

                        __name = name;

                        __isRename = false;
                    }
                }
                else
                    EditorGUI.LabelField(rect, __name);
            }

            private void __DrawElement(Rect rect, int index, bool isActive, bool isFocused)
            {
                int nodeIndex = __group.nodeIndices[index];
                StateMachineNode node = __graph?.nodes?[nodeIndex];
                if(__isRename && __renameIndex == nodeIndex && node != null)
                {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUI.DelayedTextField(rect, node.name);
                    if (EditorGUI.EndChangeCheck())
                    {
                        int numNodes = __graph.nodes == null ? 0 : __graph.nodes.Length;
                        string[] names = new string[numNodes];
                        for (int i = 0; i < numNodes; ++i)
                            names[i] = __graph.nodes[i]?.name;

                        ObjectNames.SetNameSmart(node, ObjectNames.GetUniqueName(names, name));

                        __isRename = false;
                    }
                }
                else
                    EditorGUI.LabelField(rect, node?.name);

                if (isActive && isFocused)
                    Selection.activeObject = node;

                Event current = Event.current;
                if (current != null)
                {
                    Vector2 mousePosition = current.mousePosition;
                    switch (current.type)
                    {
                        case EventType.ContextClick:
                            if (!rect.Contains(mousePosition))
                                break;

                            current.Use();

                            GenericMenu genericMenu = new GenericMenu();

                            genericMenu.AddItem(new GUIContent("Rename Node"), false, __RenameNode, nodeIndex);
                            
                            genericMenu.AddItem(new GUIContent("Remove Node"), false, __RemoveNode, nodeIndex);

                            genericMenu.AddItem(new GUIContent("Destroy Node"), false, __DestroyNode, nodeIndex);

                            genericMenu.ShowAsContext();

                            break;
                    }
                }

            }
        }

        private string[] __groupNames;
        private Group[] __groups;
        private StateMachineGraph __graph;

        [MenuItem("Window/ZG/StateMachine Editor")]
        public static void GetWindow()
        {
            GetWindow<StateMachineEditor>();
        }

        private void __CreateGroup(object position)
        {
            if (__graph == null)
                return;

            StateMachineGraph.Group result;
            result.width = EditorGUIUtility.labelWidth;
            result.position = (Vector2)position;
            result.nodeIndices = Array.Empty<int>();

            if (__graph.groups == null)
                __graph.groups = new StateMachineGraph.Groups();

            __graph.groups[ObjectNames.GetUniqueName(__groupNames, "New Group")] = result;

            EditorUtility.SetDirty(__graph);

            Repaint();
        }
        
        void OnGUI()
        {
            StateMachineGraph graph = Selection.activeObject as StateMachineGraph;
            if (graph != null)
                StateMachineEditor.graph = graph;
            else
                graph = StateMachineEditor.graph;

            if (graph == null)
                return;

            if (__graph != graph)
            {
                __graph = graph;

                __groups = null;
            }

            if (graph.groups != null)
            {
                int numGroups = graph.groups.Count;
                
                if (__groups != null && __groups.Length != numGroups)
                    __groups = null;

                if (__groups == null)
                {
                    __groupNames = new string[numGroups];

                    __groups = new Group[numGroups];
                }

                var keys = __graph.groups.Keys;
                if (keys != null)
                    keys.CopyTo(__groupNames, 0);

                int index = 0;
                string name;
                Group group;
                for(int i = 0; i < numGroups; ++i)
                {
                    name = __groupNames[i];
                    group = __groups[index];
                    if (group == null || group.name != name)
                    {
                        group = new Group(name, graph);

                        __groups[index] = group;
                    }

                    if (group.Draw())
                        Repaint();

                    ++index;
                }
            }

            Event current = Event.current;
            if (current != null)
            {
                switch (current.type)
                {
                    case EventType.ContextClick:

                        current.Use();

                        string path = AssetDatabase.GetAssetPath(graph);
                        GenericMenu genericMenu = new GenericMenu();
                        genericMenu.AddItem(new GUIContent("Create Group"), false, __CreateGroup, current.mousePosition);
                        genericMenu.ShowAsContext();

                        break;
                }
            }

            /*int numGroups = graph.groups == null ? 0 : graph.groups.Length;
            StateMachineNode node;
            Event current = Event.current;
            if (current != null)
            {
                Vector2 mousePosition = current.mousePosition;
                switch (current.type)
                {
                    case EventType.MouseDown:
                        if (current.button == 0)
                        {
                            for (int i = 0; i < numNodes; ++i)
                            {
                                node = graph.nodes[i];
                                if (node != null && node.position.Contains(mousePosition))
                                {
                                    __node = node;
                                    __offset = node.position.position - mousePosition;
                                    __isDrag = false;

                                    break;
                                }
                            }
                        }
                        else
                        {

                            current.Use();

                            GenericMenu genericMenu = null;
                            for (int i = 0; i < numNodes; ++i)
                            {
                                StateMachineNode temp = graph.nodes[i];
                                if (temp != null && temp.position.Contains(mousePosition))
                                {
                                    genericMenu = new GenericMenu();

                                    genericMenu.AddItem(new GUIContent("Destroy"), false, () =>
                                    {
                                        ArrayUtility.Remove(ref graph.nodes, temp);

                                        AssetDatabase.RemoveObjectFromAsset(temp);

                                        EditorUtility.SetDirty(graph);
                                        
                                        Repaint();
                                    });

                                    break;
                                }
                            }

                            if (genericMenu == null)
                            {
                                string path = AssetDatabase.GetAssetPath(graph);
                                IEnumerable<Type> loadedTypes = EditorHelper.loadedTypes;
                                foreach (Type loadedType in loadedTypes)
                                {
                                    if (loadedType == null || !loadedType.IsSubclassOf(typeof(StateMachineNode)) || loadedType.IsAbstract)
                                        continue;
                                    
                                    if (genericMenu == null)
                                        genericMenu = new GenericMenu();

                                    Type type = loadedType;
                                    genericMenu.AddItem(new GUIContent("Create/" + type.Name), false, () =>
                                    {
                                        StateMachineNode result = CreateInstance(type) as StateMachineNode;
                                        if (result == null)
                                            return;

                                        result.name = type.Name;
                                        result.position = new Rect(mousePosition, new Vector2(EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight));
                                        result.hideFlags = HideFlags.HideInHierarchy;

                                        AssetDatabase.AddObjectToAsset(result, path);
                                        
                                        ArrayUtility.Add(ref graph.nodes, result);

                                        EditorUtility.SetDirty(graph);

                                        Repaint();
                                    });
                                }
                            }

                            if (genericMenu != null)
                                genericMenu.ShowAsContext();
                        }
                        break;
                    case EventType.MouseUp:
                        if (__isDrag)
                            current.Use();

                        __node = null;
                        break;
                    case EventType.MouseDrag:
                        if (__node != null)
                        {
                            __node.position.position = current.mousePosition + __offset;
                            __isDrag = true;

                            EditorUtility.SetDirty(__node);
                        }
                        break;
                }
            }

            bool isOn;
            for (int i = 0; i < numNodes; ++i)
            {
                node = graph.nodes[i];
                if (node != null)
                {
                    isOn = Selection.activeObject == node;
                    if (isOn != GUI.Toggle(node.position, isOn, node.name))
                    {
                        Selection.activeObject = node;

                        break;
                    }
                }
            }*/
        }
    }
}