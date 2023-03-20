using Unity.Mathematics;
using Unity.Collections;

namespace ZG
{
    public struct Triangle
    {
        //0, 1, 2
        public int3 vertexIndices;

        //01, 12, 20
        public int3 neighborTriangleIndices;
    }

    public struct TriangleNode
    {
        public int headTriangleIndex;

        public int neighborIndex;

        public int depth;
    }

    public static class TriangleUtility
    {
        public static void ToStrips(in NativeArray<Triangle> triangles, ref NativeParallelMultiHashMap<int, int> indices)
        {
            
        }

        /*public static int ToStrips(
            int depth, 
            int headTriangleIndex,
            int parentTriangleIndex, 
            int triangleIndex,
            in NativeArray<Triangle> triangles, 
            ref NativeHashMap<int, TriangleNode> triangleNodes)
        {
            if(triangleNodes.TryGetValue(triangleIndex, out var triangleNode))
            {
                if(triangleNode.headTriangleIndex == headTriangleIndex)
                {

                }
            }
        }*/
    }
}