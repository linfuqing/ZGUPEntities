using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZG
{
    public static partial class MeshUtility
    {
        public interface ISplitVertex<T> where T : struct, ISplitVertex<T>
        {
            Vector3 position { get; }

            void Substract(in T x);

            void Mad(float x, in T y);
        }

        public interface ISplitMeshWrapper<T> where T : struct, ISplitVertex<T>
        {
            void GetVertices(in Mesh.MeshData mesh, NativeArray<T> vertices);

            int GetTriangleCount(int subMesh, in Mesh.MeshData mesh);

            void GetTriangles(int subMesh, in Mesh.MeshData mesh, NativeArray<Triangle> triangles);

            void SetTriangles(in Mesh.MeshData mesh, in NativeArray<Triangle> triangles);

            void SetVertices(in Mesh.MeshData mesh, in NativeArray<T> vertices);

            void SetSubMesh(in Mesh.MeshData mesh, int subMesh, int startTriangleIndex, int triangleCount);
        }

        public struct SplitVertex : ISplitVertex<SplitVertex>
        {
            public Vector3 position;

            //public Vector2 uv1;
            //public Vector2 uv0;

            public void Substract(in SplitVertex x)
            {
                position -= x.position;
                //uv0 -= x.uv0;
                //uv1 -= x.uv1;
            }

            public void Mad(float x, in SplitVertex y)
            {
                position = math.mad(position, x, y.position);
                //uv0 = uv0 * x + y.uv0;
                //uv1 = uv1 * x + y.uv1;
            }

            Vector3 ISplitVertex<SplitVertex>.position => position;
        }

        public struct SplitMeshWrapper : ISplitMeshWrapper<SplitVertex>
        {
            public void GetVertices(in Mesh.MeshData mesh, NativeArray<SplitVertex> vertices)
            {
                mesh.GetVertices(vertices.Reinterpret<Vector3>());
            }

            public int GetTriangleCount(int subMesh, in Mesh.MeshData mesh)
            {
                return mesh.GetSubMesh(subMesh).indexCount / 3;
            }

            public void GetTriangles(int subMesh, in Mesh.MeshData mesh, NativeArray<Triangle> triangles)
            {
                var subMeshDesc = mesh.GetSubMesh(subMesh);
                __Check(subMeshDesc);

                switch (mesh.indexFormat)
                {
                    case IndexFormat.UInt16:
                        {
                            var indices = mesh.GetIndexData<ushort>();
                            Triangle triangle;
                            int numTriangles = triangles.Length;
                            for (int i = 0; i < numTriangles; ++i)
                            {
                                triangle.x = indices[i * 3 + 0 + subMeshDesc.indexStart] + subMeshDesc.baseVertex;
                                triangle.y = indices[i * 3 + 1 + subMeshDesc.indexStart] + subMeshDesc.baseVertex;
                                triangle.z = indices[i * 3 + 2 + subMeshDesc.indexStart] + subMeshDesc.baseVertex;

                                triangles[i] = triangle;
                            }
                        }
                        break;
                    case IndexFormat.UInt32:
                        mesh.GetIndices(triangles.Reinterpret<int>(UnsafeUtility.SizeOf<int>()), subMesh);
                        
                        break;
                }
            }

            public void SetTriangles(in Mesh.MeshData mesh, in NativeArray<Triangle> triangles)
            {
                int numIndices = triangles.Length * 3;
                if (mesh.vertexCount > ushort.MaxValue)
                {
                    mesh.SetIndexBufferParams(numIndices, IndexFormat.UInt32);

                    var indices = mesh.GetIndexData<uint>();

                    UnityEngine.Assertions.Assert.AreEqual(indices.Length, numIndices);

                    indices.CopyFrom(triangles.Reinterpret<uint>(UnsafeUtility.SizeOf<uint>()));
                }
                else
                {
                    mesh.SetIndexBufferParams(numIndices, IndexFormat.UInt16);
                    Triangle triangle;
                    var indices = mesh.GetIndexData<ushort>();

                    UnityEngine.Assertions.Assert.AreEqual(indices.Length, numIndices);

                    for (int i = 0; i < numIndices; i += 3)
                    {
                        triangle = triangles[i / 3];

                        indices[i + 0] = (ushort)triangle.x;
                        indices[i + 1] = (ushort)triangle.y;
                        indices[i + 2] = (ushort)triangle.z;
                    }
                }
            }

            public void SetVertices(in Mesh.MeshData mesh, in NativeArray<SplitVertex> vertices)
            {
                var attributes = new NativeArray<VertexAttributeDescriptor>(1, Allocator.Temp);

                attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0);
                mesh.SetVertexBufferParams(vertices.Length, attributes);

                attributes.Dispose();

                UnityEngine.Assertions.Assert.AreEqual(mesh.vertexCount, vertices.Length);

                mesh.GetVertexData<SplitVertex>().CopyFrom(vertices);
            }

            public void SetSubMesh(in Mesh.MeshData mesh, int subMesh, int startTriangleIndex, int triangleCount)
            {
                var subMeshDesc = new SubMeshDescriptor(startTriangleIndex * 3, triangleCount * 3);

                mesh.SetSubMesh(subMesh, new SubMeshDescriptor(startTriangleIndex * 3, triangleCount* 3));
            }

            [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __Check(SubMeshDescriptor subMeshDescriptor)
            {
                if (subMeshDescriptor.topology != MeshTopology.Triangles)
                    throw new System.NotSupportedException();
            }
        }

        public struct Triangle
        {
            public int x;
            public int y;
            public int z;

            public Triangle(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }
        }

        public struct SubMesh
        {
            public int triangleStartIndex;
            public int triangleCount;
        }

        public struct MeshArrayData
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<int> originMeshIndices;
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<int> meshCount;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> rootSubMeshIndexCount;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> rootSubMeshIndexOffsets;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> rootSubMeshIndices;

            /*public Mesh.MeshData this[int meshIndex]
            {
                get
                {
                    return meshes[meshIndex];
                }
            }*/

            public MeshArrayData(int maxMeshCount, int maxSubMeshCount, Allocator allocator)
            {
                //readOnlyMeshes = Mesh.AcquireReadOnlyMeshData(meshes);
                //meshes = Mesh.AllocateWritableMeshData(maxReadWriteMeshCount);
                originMeshIndices = new NativeArray<int>(maxMeshCount, allocator);
                meshCount = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
                rootSubMeshIndexCount = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
                rootSubMeshIndexOffsets = new NativeArray<int>(maxMeshCount, allocator, NativeArrayOptions.UninitializedMemory);
                rootSubMeshIndices = new NativeArray<int>(maxSubMeshCount, allocator, NativeArrayOptions.UninitializedMemory);
            }

            public void Dispose()
            {
                //readWriteMeshes.Dispose();
                originMeshIndices.Dispose();
                meshCount.Dispose();
                rootSubMeshIndexCount.Dispose();
                rootSubMeshIndexOffsets.Dispose();
                rootSubMeshIndices.Dispose();
            }

            public void Init(in Mesh.MeshDataArray meshArray)
            {
                int meshCount = meshArray.Length, subMeshIndexCount = 0, subMeshCount, i, j;
                Mesh.MeshData mesh;
                for(i = 0; i < meshCount; ++i)
                {
                    mesh = meshArray[i];

                    subMeshCount = mesh.subMeshCount;

                    for (j = 0; j < subMeshCount; ++j)
                        rootSubMeshIndices[subMeshIndexCount + j] = j;

                    rootSubMeshIndexOffsets[i] = subMeshIndexCount;

                    subMeshIndexCount += subMeshCount;
                }

                rootSubMeshIndexCount[0] = subMeshIndexCount;

                this.meshCount[0] = meshCount;

                for (i = 0; i < meshCount; ++i)
                    originMeshIndices[i] = -1;
            }

            public int GetRootOriginMeshIndex(int meshIndex)
            {
                int originMeshIndex = originMeshIndices[meshIndex];
                if (originMeshIndex == -1)
                    return meshIndex;

                return GetRootOriginMeshIndex(originMeshIndex);
            }

            public int AllocMesh(int originMeshIndex)
            {
                int meshIndex = meshCount.Increment(0) - 1;

                originMeshIndices[meshIndex] = originMeshIndex;

                return meshIndex;
            }

            public NativeArray<int> AllocRootSubMeshIndices(int meshIndex, int count)
            {
                int subMeshIndexOffset = rootSubMeshIndexCount.Add(0, count) - count;

                rootSubMeshIndexOffsets[meshIndex] = subMeshIndexOffset;

                return rootSubMeshIndices.GetSubArray(subMeshIndexOffset, count);
            }

            public NativeArray<int> GetRootSubMeshIndices(int meshIndex, int count)
            {
                return rootSubMeshIndices.GetSubArray(rootSubMeshIndexOffsets[meshIndex], count);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        private struct SplitInit : IJob
        {
            [ReadOnly]
            public Mesh.MeshDataArray meshArray;
            public MeshArrayData data;

            public void Execute()
            {
                data.Init(meshArray);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct SplitCopyMeshes<TVertex, TMeshWrapper> : IJobParallelFor
                where TVertex : struct, ISplitVertex<TVertex>
                where TMeshWrapper : struct, ISplitMeshWrapper<TVertex>
        {
            public TMeshWrapper meshWrapper;

            [ReadOnly]
            public Mesh.MeshDataArray sources;
            public Mesh.MeshDataArray destinations;

            public void Execute(int index)
            {
                var destination = destinations[index];
                Copy<TVertex, TMeshWrapper>(meshWrapper, sources[index], ref destination);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct SplitSegments<TVertex, TMeshWrapper> : IJobParallelForDefer
                where TVertex : unmanaged, ISplitVertex<TVertex>
                where TMeshWrapper : struct, ISplitMeshWrapper<TVertex>
        {
            public bool useNewMesh;
            public int segments;
            public float extent;
            public float center;
            public float planeDistanceError;
            public Vector3 normal;
            public TMeshWrapper meshWrapper;
            [NativeDisableParallelForRestriction]
            public Mesh.MeshDataArray meshArray;
            public MeshArrayData meshArrayData;
            [ReadOnly]
            public NativeArray<Matrix4x4> inverseMatrices;

            public void Execute(int index)
            {
                var inverseMatrix = inverseMatrices[meshArrayData.GetRootOriginMeshIndex(index)];
                float scale = extent / segments * 2.0f;
                for (int i = 1; i < segments; ++i)
                {
                    Split<TVertex, TMeshWrapper>(
                        useNewMesh,
                        index,
                        inverseMatrix.TransformPlane(new Plane(normal, -(center - extent + scale * i))),
                        meshWrapper,
                        ref meshArray,
                        ref meshArrayData,
                        planeDistanceError);
                    //Debug.Log($"Do {i} : {meshArrayData.meshCount[0]} : {meshArrayData.rootSubMeshIndexCount[0]}");
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct SplitSegmentsCount : IJobParallelFor
        {
            public bool useNewMesh;
            public int segments;
            public float extent;
            public float center;
            public float planeDistanceError;
            public Vector3 normal;
            public NativeArray<int> meshCounts;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> subMeshCounts;
            [ReadOnly]
            public Mesh.MeshDataArray meshArray;
            [ReadOnly]
            public NativeArray<Matrix4x4> inverseMatrices;

            public void Execute(int index)
            {
                var mesh = meshArray[index];
                var inverseMatrix = inverseMatrices[index];
                Plane plane;
                float scale = extent / segments * 2.0f;
                int sameSideCount = 1;
                using (var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
                {
                    mesh.GetVertices(vertices);

                    for (int i = 1; i < segments; ++i)
                    {
                        plane = inverseMatrix.TransformPlane(new Plane(normal, -(center - extent + scale * i)));
                        if (!IsSameSide(plane, vertices, planeDistanceError))
                            ++sameSideCount;
                    }
                }

                if(sameSideCount > 1)
                    subMeshCounts[index] += subMeshCounts[index] * (sameSideCount << 1);

                if (useNewMesh)
                    meshCounts[index] *= sameSideCount;
            }
        }

        public static void Copy<TVertex, TMeshWrapper>(
            in TMeshWrapper meshWrapper, 
            in Mesh.MeshData source, 
            ref Mesh.MeshData destination)
            where TVertex : struct, ISplitVertex<TVertex>
            where TMeshWrapper : struct, ISplitMeshWrapper<TVertex>
        {
            destination.subMeshCount = 0;

            using (var vertices = new NativeArray<TVertex>(source.vertexCount, Allocator.Temp))
            {
                meshWrapper.GetVertices(source, vertices);

                meshWrapper.SetVertices(destination, vertices);
            }

            int triangleCount, triangleOffset, subMeshCount = source.subMeshCount;
            var triangles = new NativeList<Triangle>(Allocator.Temp);
            var subMeshes = new NativeArray<(int, int)>(subMeshCount, Allocator.Temp);
            for (int i = 0; i < subMeshCount; ++i)
            {
                triangleCount = meshWrapper.GetTriangleCount(i, source);

                triangleOffset = triangles.Length;

                triangles.ResizeUninitialized(triangleOffset + triangleCount);

                meshWrapper.GetTriangles(i, source, triangles.AsArray().GetSubArray(triangleOffset, triangleCount));

                subMeshes[i] = (triangleOffset, triangleCount);
            }

            destination.subMeshCount = subMeshCount;
            meshWrapper.SetTriangles(destination, triangles.AsArray());

            triangles.Dispose();

            (int, int) subMesh;
            for (int i = 0; i < subMeshCount; ++i)
            {
                subMesh = subMeshes[i];
                meshWrapper.SetSubMesh(destination, i, subMesh.Item1, subMesh.Item2);
            }

            subMeshes.Dispose();
        }

        public static bool IsSameSide(
            in Plane plane, 
            in NativeArray<Vector3> vertices,
            float planeDistanceError = 0.0001f)
        {
            int i, numVertices = vertices.Length;
            for (i = 0; i < numVertices; ++i)
            {
                if (math.abs(plane.GetDistanceToPoint(vertices[i])) > planeDistanceError)
                    break;
            }

            if (i < numVertices)
            {
                bool side = plane.GetSide(vertices[i]);

                for (++i; i < numVertices; ++i)
                {
                    if (plane.GetSide(vertices[i]) != side && math.abs(plane.GetDistanceToPoint(vertices[i])) > planeDistanceError)
                        return false;
                }
            }

            return true;
        }

        /*public static bool IsSameSide(
            in Plane plane, 
            in NativeArray<Vector3> vertices, 
            in NativeArray<ushort> indices)
        {
            bool side = plane.GetSide(vertices[indices[0]]);
            int numIndices = indices.Length;
            for (int i = 1; i < numIndices; ++i)
            {
                if (plane.GetSide(vertices[indices[i]]) != side)
                    return false;
            }

            return true;
        }

        public static bool IsSameSide(
            in Plane plane,
            in NativeArray<Vector3> vertices,
            in NativeArray<int> indices)
        {
            bool side = plane.GetSide(vertices[indices[0]]);
            int numIndices = indices.Length;
            for (int i = 1; i < numIndices; ++i)
            {
                if (plane.GetSide(vertices[indices[i]]) != side)
                    return false;
            }

            return true;
        }*/

        public static int Split<TVertex, TMeshWrapper>(
            bool useNewMesh,
            int meshIndex, 
            in Plane plane,
            in TMeshWrapper meshWrapper,
            ref Mesh.MeshDataArray meshArray, 
            ref MeshArrayData meshArrayData, 
            float planeDistanceError = 0.0001f)
            where TVertex : unmanaged, ISplitVertex<TVertex>
            where TMeshWrapper : struct, ISplitMeshWrapper<TVertex>
        {
            //plane = plane.flipped;

            var mesh = meshArray[meshIndex];

            int vertexCount = mesh.vertexCount;
            NativeList<Triangle> trianglesX = default, trianglesY = default;
            NativeList<TVertex> verticesX = default, verticesY = default;

            int i, subMeshCount = mesh.subMeshCount;
            NativeParallelHashMap<int, (int, int)> subMeshesX = default, subMeshesY = default;
            using (var vertices = new NativeArray<TVertex>(vertexCount, Allocator.Temp))
            {
                meshWrapper.GetVertices(mesh, vertices);

                var above = new NativeArray<bool>(vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var indices = new NativeArray<int>(vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                TVertex vertex;
                for (i = 0; i < vertexCount; ++i)
                {
                    vertex = vertices[i];
                    if (above[i] = plane.GetSide(vertex.position))
                    {
                        if (!verticesX.IsCreated)
                        {
                            verticesX = new NativeList<TVertex>(Allocator.Temp);

                            if (!useNewMesh)
                                verticesY = verticesX;
                        }

                        indices[i] = verticesX.Length;

                        verticesX.Add(vertex);
                    }
                    else
                    {
                        if (!verticesY.IsCreated)
                        {
                            verticesY = new NativeList<TVertex>(Allocator.Temp);

                            if (!useNewMesh)
                                verticesX = verticesY;
                        }

                        indices[i] = verticesY.Length;

                        verticesY.Add(vertex);
                    }
                }

                for(i = 0; i < vertexCount; ++i)
                {
                    if (math.abs(plane.GetDistanceToPoint(vertices[i].position)) > planeDistanceError)
                        break;
                }

                bool result;
                if (i < vertexCount)
                {
                    result = above[i];
                    for (++i; i < vertexCount; ++i)
                    {
                        if (above[i] != result && math.abs(plane.GetDistanceToPoint(vertices[i].position)) > planeDistanceError)
                            break;
                    }
                }

                if (i == vertexCount)
                {
                    above.Dispose();
                    indices.Dispose();

                    if (verticesX.IsCreated)
                    {
                        verticesX.Dispose();

                        if (!useNewMesh)
                            verticesY = default;
                    }

                    if (verticesY.IsCreated)
                    {
                        verticesY.Dispose();

                        if (!useNewMesh)
                            verticesX = default;
                    }

                    return 0;
                }

                int triangleOffsetX = 0, triangleOffsetY = 0, index0, index1, index2, indexXU, indexYU, indexXV, indexYV, triangleCount, j;
                bool aboveX, aboveY, aboveZ;
                float enter, invMagnitude;
                Vector3 position;
                TVertex vertexX, vertexY, vertexZ;
                Triangle triangle;
                for (i = 0; i < subMeshCount; ++i)
                {
                    triangleCount = meshWrapper.GetTriangleCount(i, mesh);
                    using (var triangles = new NativeArray<Triangle>(triangleCount, Allocator.Temp))
                    {
                        meshWrapper.GetTriangles(i, mesh, triangles);
                        for (j = 0; j < triangleCount; ++j)
                        {
                            triangle = triangles[j];

                            aboveX = above[triangle.x];
                            aboveY = above[triangle.y];
                            aboveZ = above[triangle.z];

                            if (aboveX && aboveY && aboveZ)
                            {
                                if (!trianglesX.IsCreated)
                                    trianglesX = new NativeList<Triangle>(Allocator.Temp);

                                trianglesX.Add(new Triangle(indices[triangle.x], indices[triangle.y], indices[triangle.z]));
                            }
                            else if (!aboveX && !aboveY && !aboveZ)
                            {
                                if (!trianglesY.IsCreated)
                                    trianglesY = new NativeList<Triangle>(Allocator.Temp);

                                trianglesY.Add(new Triangle(indices[triangle.x], indices[triangle.y], indices[triangle.z]));
                            }
                            else
                            {
                                if (aboveX == aboveY)
                                {
                                    result = aboveZ;

                                    index0 = indices[triangle.x];
                                    index1 = indices[triangle.y];
                                    index2 = indices[triangle.z];

                                    vertexX = vertices[triangle.x];
                                    vertexY = vertices[triangle.y];
                                    vertexZ = vertices[triangle.z];
                                }
                                else if (aboveY == aboveZ)
                                {
                                    result = aboveX;

                                    index0 = indices[triangle.y];
                                    index1 = indices[triangle.z];
                                    index2 = indices[triangle.x];

                                    vertexX = vertices[triangle.y];
                                    vertexY = vertices[triangle.z];
                                    vertexZ = vertices[triangle.x];
                                }
                                else
                                {
                                    result = aboveY;

                                    index0 = indices[triangle.z];
                                    index1 = indices[triangle.x];
                                    index2 = indices[triangle.y];

                                    vertexX = vertices[triangle.z];
                                    vertexY = vertices[triangle.x];
                                    vertexZ = vertices[triangle.y];
                                }

                                indexXU = indexXV = indexYU = indexYV = -1;

                                vertex = vertexX;
                                vertex.Substract(vertexZ);

                                position = vertex.position;
                                //invMagnitude = position.magnitude;
                                //invMagnitude = 1.0f / invMagnitude;
                                invMagnitude = math.rsqrt(position.sqrMagnitude);
                                position *= invMagnitude;
                                plane.Raycast(new Ray(vertexZ.position, position), out enter);

                                enter *= invMagnitude;

                                vertex.Mad(enter, vertexZ);

                                if (!verticesX.IsCreated)
                                {
                                    verticesX = new NativeList<TVertex>(Allocator.Temp);

                                    if (!useNewMesh)
                                        verticesY = verticesX;
                                }

                                indexXU = verticesX.Length;

                                verticesX.Add(vertex);

                                if (useNewMesh)
                                {
                                    if (!verticesY.IsCreated)
                                        verticesY = new NativeList<TVertex>(Allocator.Temp);


                                    indexYU = verticesY.Length;

                                    verticesY.Add(vertex);
                                }
                                else
                                    indexYU = indexXU;

                                vertex = vertexY;
                                vertex.Substract(vertexZ);

                                position = vertex.position;
                                //invMagnitude = position.magnitude;
                                //invMagnitude = 1.0f / invMagnitude;
                                invMagnitude = math.rsqrt(position.sqrMagnitude);
                                position *= invMagnitude;
                                plane.Raycast(new Ray(vertexZ.position, position), out enter);

                                enter *= invMagnitude;

                                vertex.Mad(enter, vertexZ);

                                indexXV = verticesX.Length;

                                verticesX.Add(vertex);

                                if (useNewMesh)
                                {
                                    indexYV = verticesY.Length;

                                    verticesY.Add(vertex);
                                }
                                else
                                    indexYV = indexXV;

                                if (result)
                                {
                                    if (!trianglesX.IsCreated)
                                        trianglesX = new NativeList<Triangle>(Allocator.Temp);

                                    trianglesX.Add(new Triangle(index2, indexXU, indexXV));

                                    /*indicesX.Add(index2);
                                    indicesX.Add(indexXU);
                                    indicesX.Add(indexXV);*/

                                    if (!trianglesY.IsCreated)
                                        trianglesY = new NativeList<Triangle>(Allocator.Temp);

                                    trianglesY.Add(new Triangle(index0, index1, indexYU));
                                    trianglesY.Add(new Triangle(index1, indexYV, indexYU));

                                    /*indicesY.Add(index0);
                                    indicesY.Add(index1);
                                    indicesY.Add(indexYU);

                                    indicesY.Add(index1);
                                    indicesY.Add(indexYV);
                                    indicesY.Add(indexYU);*/
                                }
                                else
                                {
                                    if (!trianglesY.IsCreated)
                                        trianglesY = new NativeList<Triangle>(Allocator.Temp);

                                    trianglesY.Add(new Triangle(index2, indexYU, indexYV));

                                    /*indicesY.Add(index2);
                                    indicesY.Add(indexYU);
                                    indicesY.Add(indexYV);*/

                                    if (!trianglesX.IsCreated)
                                        trianglesX = new NativeList<Triangle>(Allocator.Temp);

                                    trianglesX.Add(new Triangle(index0, index1, indexXU));
                                    trianglesX.Add(new Triangle(index1, indexXV, indexXU));

                                    /*indicesX.Add(index0);
                                    indicesX.Add(index1);
                                    indicesX.Add(indexXU);

                                    indicesX.Add(index1);
                                    indicesX.Add(indexXV);
                                    indicesX.Add(indexXU);*/
                                }
                            }
                        }
                    }

                    triangleCount = trianglesX.IsCreated ? trianglesX.Length : 0;
                    if (triangleCount > triangleOffsetX)
                    {
                        if (!subMeshesX.IsCreated)
                            subMeshesX = new NativeParallelHashMap<int, (int, int)>(1, Allocator.Temp);

                        subMeshesX.Add(i, (triangleOffsetX, triangleCount - triangleOffsetX));

                        triangleOffsetX = triangleCount;
                    }

                    triangleCount = trianglesY.IsCreated ? trianglesY.Length : 0;
                    if (triangleCount > triangleOffsetY)
                    {
                        if (!subMeshesY.IsCreated)
                            subMeshesY = new NativeParallelHashMap<int, (int, int)>(1, Allocator.Temp);

                        subMeshesY.Add(i, (triangleOffsetY, triangleCount - triangleOffsetY));

                        triangleOffsetY = triangleCount;
                    }
                }

                above.Dispose();
                indices.Dispose();
            }

            int meshIndexX = -1, meshIndexY = -1, baseIndexX = 0, baseIndexY = 0;
            Mesh.MeshData x = default, y = default;
            NativeArray<int> rootSubMeshIndicesX = default, 
                rootSubMeshIndicesY = default, 
                originSubMeshIndices = meshArrayData.GetRootSubMeshIndices(meshIndex, subMeshCount);
            if (verticesX.IsCreated && trianglesX.IsCreated)
            {
                if (meshIndexX == -1)
                {
                    meshIndexX = meshIndexY == meshIndex ? meshArrayData.AllocMesh(meshIndex) : meshIndex;
                    x = meshArray[meshIndexX];

                    if(meshIndexX == meshIndex)
                        x.subMeshCount = 0;

                    meshWrapper.SetVertices(x, verticesX.AsArray());

                    if (useNewMesh)
                        subMeshCount = subMeshesX.Count();
                    else
                    {
                        baseIndexY = trianglesX.Length;

                        if (trianglesY.IsCreated)
                            trianglesX.AddRange(trianglesY.AsArray());

                        subMeshCount = subMeshesX.Count() + (subMeshesY.IsCreated ? subMeshesY.Count() : 0);

                        y = x;

                        meshIndexY = meshIndexX;
                    }

                    meshWrapper.SetTriangles(x, trianglesX.AsArray());

                    rootSubMeshIndicesX = meshArrayData.AllocRootSubMeshIndices(meshIndexX, subMeshCount);
                    if (!useNewMesh)
                        rootSubMeshIndicesY = rootSubMeshIndicesX;

                    x.subMeshCount = subMeshCount;

                    subMeshCount = 0;

                    verticesX.Dispose();
                }

                trianglesX.Dispose();

                (int, int) subMesh;
                foreach (var pair in subMeshesX)
                {
                    subMesh = pair.Value;

                    meshWrapper.SetSubMesh(x, subMeshCount, subMesh.Item1 + baseIndexX, subMesh.Item2);

                    rootSubMeshIndicesX[subMeshCount++] = originSubMeshIndices[pair.Key];
                }

                subMeshesX.Dispose();
            }

            if (verticesY.IsCreated && trianglesY.IsCreated)
            {
                if (meshIndexY == -1)
                {
                    meshIndexY = meshIndexX == meshIndex ? meshArrayData.AllocMesh(meshIndex) : meshIndex;

                    y = meshArray[meshIndexY];

                    if (meshIndexY == meshIndex)
                        y.subMeshCount = 0;

                    meshWrapper.SetVertices(y, verticesY.AsArray());

                    if (useNewMesh)
                        subMeshCount = subMeshesY.Count();
                    else
                    {
                        baseIndexX = trianglesY.Length;

                        if (trianglesX.IsCreated)
                            trianglesY.AddRange(trianglesX.AsArray());

                        subMeshCount = (subMeshesX.IsCreated ? subMeshesX.Count() : 0) + subMeshesY.Count();

                        x = y;

                        meshIndexX = meshIndexY;
                    }

                    meshWrapper.SetTriangles(y, trianglesY.AsArray());

                    rootSubMeshIndicesY = meshArrayData.AllocRootSubMeshIndices(meshIndexY, subMeshCount);
                    if (!useNewMesh)
                        rootSubMeshIndicesX = rootSubMeshIndicesY;

                    y.subMeshCount = subMeshCount;

                    subMeshCount = 0;

                    verticesY.Dispose();
                }

                trianglesY.Dispose();

                (int, int) subMesh;
                foreach (var pair in subMeshesY)
                {
                    subMesh = pair.Value;

                    meshWrapper.SetSubMesh(y, subMeshCount, subMesh.Item1 + baseIndexY, subMesh.Item2);

                    rootSubMeshIndicesY[subMeshCount++] = originSubMeshIndices[pair.Key];
                }

                subMeshesY.Dispose();
            }

            int meshCount = 0;
            if (meshIndexX != -1)
                ++meshCount;

            if (meshIndexY != -1)
                ++meshCount;

            return meshCount;
        }

        public static JobHandle Split<TVertex, TMeshWrapper>(
            bool useNewMesh, 
            int length, 
            int width, 
            int height, 
            int innerloopBatchCount, 
            float planeDistanceError, 
            in float3 center, 
            in float3 extents, 
            in NativeArray<Matrix4x4> inverseMatrices,
            in TMeshWrapper meshWrapper, 
            in Mesh.MeshDataArray inputs, 
            ref Mesh.MeshDataArray outputs,
            ref MeshArrayData results,
            in JobHandle inputDeps)
                where TVertex : unmanaged, ISplitVertex<TVertex>
                where TMeshWrapper : struct, ISplitMeshWrapper<TVertex>
        {
            if (length < 2 && width < 2 && height < 2)
                return inputDeps;

            SplitInit init;
            init.meshArray = inputs;
            init.data = results;
            var temp = init.Schedule(inputDeps);

            SplitCopyMeshes<TVertex, TMeshWrapper> copyMeshes;
            copyMeshes.meshWrapper = meshWrapper;
            copyMeshes.sources = inputs;
            copyMeshes.destinations = outputs;
            var jobHandle = copyMeshes.Schedule(inputs.Length, innerloopBatchCount, inputDeps);

            jobHandle = JobHandle.CombineDependencies(temp, jobHandle);

            SplitSegments<TVertex, TMeshWrapper> splitSegments;
            splitSegments.useNewMesh = useNewMesh;
            splitSegments.planeDistanceError = planeDistanceError;
            splitSegments.meshWrapper = meshWrapper;
            splitSegments.meshArray = outputs;
            splitSegments.meshArrayData = results;
            splitSegments.inverseMatrices = inverseMatrices;

            splitSegments.segments = length;
            splitSegments.center = center.z;
            splitSegments.extent = extents.z;
            splitSegments.normal = Vector3.forward;
            jobHandle = splitSegments.ScheduleUnsafeIndex0(results.meshCount, innerloopBatchCount, jobHandle);

            splitSegments.segments = width;
            splitSegments.center = center.x;
            splitSegments.extent = extents.x;
            splitSegments.normal = Vector3.right;
            jobHandle = splitSegments.ScheduleUnsafeIndex0(results.meshCount, innerloopBatchCount, jobHandle);

            splitSegments.segments = height;
            splitSegments.center = center.y;
            splitSegments.extent = extents.y;
            splitSegments.normal = Vector3.up;
            jobHandle = splitSegments.ScheduleUnsafeIndex0(results.meshCount, innerloopBatchCount, jobHandle);

            return jobHandle;
        }

        public static JobHandle CalculateSplitCounts(
            bool useNewMesh,
            int length,
            int width,
            int height,
            int innerloopBatchCount,
            float planeDistanceError, 
            in float3 center,
            in float3 extents,
            in NativeArray<Matrix4x4> inverseMatrices, 
            in Mesh.MeshDataArray meshes, 
            ref NativeArray<int> meshCounts, 
            ref NativeArray<int> subMeshCounts,
            in JobHandle inputDeps)
        {
            int i, numMeshes = meshes.Length;
            for(i = 0; i < numMeshes; ++i)
            {
                meshCounts[i] = 1;

                subMeshCounts[i] = meshes[i].subMeshCount;
            }

            var jobHandle = inputDeps;

            SplitSegmentsCount count;
            count.useNewMesh = useNewMesh;
            count.planeDistanceError = planeDistanceError;
            count.meshCounts = meshCounts;
            count.subMeshCounts = subMeshCounts;
            count.meshArray = meshes;
            count.inverseMatrices = inverseMatrices;

            count.segments = length;
            count.center = center.z;
            count.extent = extents.z;
            count.normal = Vector3.forward;
            jobHandle = count.Schedule(numMeshes, innerloopBatchCount, jobHandle);

            count.segments = width;
            count.center = center.x;
            count.extent = extents.x;
            count.normal = Vector3.right;
            jobHandle = count.Schedule(numMeshes, innerloopBatchCount, jobHandle);

            count.segments = height;
            count.center = center.y;
            count.extent = extents.y;
            count.normal = Vector3.up;
            jobHandle = count.Schedule(numMeshes, innerloopBatchCount, jobHandle);

            return jobHandle;
        }
    }
}