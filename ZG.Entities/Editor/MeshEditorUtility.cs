using UnityEngine;
using UnityEditor;
using Unity.EditorCoroutines.Editor;

namespace ZG
{
    public partial class MeshEditorUtility
    {
        [MenuItem("GameObject/ZG/Mesh/SplitEx", false, 10)]
        public static void SplitEx(MenuCommand menuCommand)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(
                MeshUtility.Split(
                    menuCommand.context as GameObject, 
                    MeshEditor.splitBounds, 
                    0.0001f, 
                    64, 
                    32,
                    MeshEditor.splitSegmentLength,
                    MeshEditor.splitSegmentWidth,
                    MeshEditor.splitSegmentHeight, 
                    MeshEditor.splitUseNewMesh));
        }
    }
}