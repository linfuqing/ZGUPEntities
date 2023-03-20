using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace ZG
{
    public static partial class MeshUtility
    {
        public static void Split<TVertex, TMeshWrapper>(
            IList<MeshFilter> meshFilters, 
            Bounds bounds,
            TMeshWrapper meshWrapper, 
            float planeDistanceError, 
            int innerloopBatchCount, 
            int length, 
            int width, 
            int height,
            bool useNewMesh)
                where TVertex : unmanaged, ISplitVertex<TVertex>
                where TMeshWrapper : struct, ISplitMeshWrapper<TVertex>
        {
            if (length < 2 && width < 2 && height < 2)
                return;

            int numMeshes = meshFilters.Count, numSubMeshes = 0, i;
            MeshFilter meshFilter;
            Mesh mesh;
            var meshes = new Mesh[numMeshes];
            var inverseMatrices = new NativeArray<Matrix4x4>(numMeshes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (i = 0; i < numMeshes; ++i)
            {
                meshFilter = meshFilters[i];

#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Initializating Meshes And Inverse Matrices..", meshFilter.name, i * 1.0f / numMeshes))
                {
                    UnityEditor.EditorUtility.ClearProgressBar();

                    return;
                }
#endif
                mesh = meshFilter.sharedMesh;

                numSubMeshes += mesh.subMeshCount;

                meshes[i] = mesh;

                inverseMatrices[i] = meshFilter.transform.worldToLocalMatrix;
            }

            var meshCounts = new NativeArray<int>(numMeshes, Allocator.TempJob);
            var subMeshCounts = new NativeArray<int>(numMeshes, Allocator.TempJob);

            var inputs = Mesh.AcquireReadOnlyMeshData(meshes);

            BurstUtility.InitializeJobParallelFor<SplitSegmentsCount>();

            var jobHandle = CalculateSplitCounts(
                useNewMesh,
                length,
                width,
                height,
                innerloopBatchCount,
                planeDistanceError, 
                bounds.center,
                bounds.extents,
                inverseMatrices,
                inputs,
                ref meshCounts,
                ref subMeshCounts,
                default);

            jobHandle.Complete();

            uint maxMeshCount = 0;
            ulong maxSubMeshCount = 0;
            for (i = 0; i < numMeshes; ++i)
            {
                maxMeshCount += (uint)meshCounts[i];

                maxSubMeshCount += (ulong)subMeshCounts[i];
            }

            meshCounts.Dispose();
            subMeshCounts.Dispose();

            var outputs = Mesh.AllocateWritableMeshData((int)maxMeshCount);
            var results = new MeshArrayData((int)maxMeshCount, (int)Math.Min(maxSubMeshCount, int.MaxValue), Allocator.Persistent);

            BurstUtility.InitializeJob<SplitInit>();
            BurstUtility.InitializeJobParallelFor<SplitCopyMeshes<TVertex, TMeshWrapper>>();
            BurstUtility.InitializeJobParalledForDefer<SplitSegments<TVertex, TMeshWrapper>>();

            jobHandle = Split<TVertex, TMeshWrapper>(
                useNewMesh, 
                length, 
                width, 
                height, 
                innerloopBatchCount,
                planeDistanceError, 
                bounds.center, 
                bounds.extents, 
                inverseMatrices, 
                meshWrapper,
                inputs, 
                ref outputs, 
                ref results, 
                default);

            Unity.Jobs.JobHandle.ScheduleBatchedJobs();

            var originMaterials = new Material[numMeshes][];
            for (i = 0; i < numMeshes; ++i)
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Creating Origin Materials..", meshFilters[i].name, i * 1.0f / numMeshes))
                {
                    UnityEditor.EditorUtility.ClearProgressBar();

                    return;
                }
#endif

                originMaterials[i] = meshFilters[i].GetComponent<Renderer>().sharedMaterials;
            }

            meshes = new Mesh[maxMeshCount];

            for (i = 0; i < maxMeshCount; ++i)
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Creating Writable Meshes..", $"{i}/{maxMeshCount}", i * 1.0f / maxMeshCount))
                {
                    UnityEditor.EditorUtility.ClearProgressBar();

                    return;
                }
#endif

                meshes[i] = new Mesh();
            }

#if UNITY_EDITOR
            do
            {
                i = results.meshCount[0];

                UnityEditor.EditorUtility.DisplayCancelableProgressBar("Waiting For Job..", $"{i}/{maxMeshCount}", i * 1.0f / maxMeshCount);
            } while (i < maxMeshCount && !jobHandle.IsCompleted);
#endif

            jobHandle.Complete();

            inverseMatrices.Dispose();

            inputs.Dispose();

            Mesh.ApplyAndDisposeWritableMeshData(outputs, meshes);

            int meshCount = results.meshCount[0];
            for (i = meshCount; i < maxMeshCount; ++i)
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Destroing Meshes..", $"{i}/{maxMeshCount}", i * 1.0f / maxMeshCount))
                {
                    UnityEditor.EditorUtility.ClearProgressBar();

                    return;
                }
#endif

                UnityEngine.Object.DestroyImmediate(meshes[i]);
            }

            int j, numRenderers, numLODs, originMeshIndex;
            MeshFilter targetMeshFilter;
            Renderer targetRenderer, renderer;
            LODGroup lodGroup;
            LOD[] lods;
            Material[] sourceMaterials, destinationMaterials;
            var originMeshIndices = new HashSet<int>();
            for (i = 0; i < meshCount; ++i)
            {
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Building Mesh Filters..", $"{i}/{meshCount}", i * 1.0f / meshCount))
                {
                    UnityEditor.EditorUtility.ClearProgressBar();

                    return;
                }
#endif
                originMeshIndex = results.GetRootOriginMeshIndex(i);
                meshFilter = meshFilters[originMeshIndex];
                renderer = meshFilter.GetComponent<Renderer>();
                if (!originMeshIndices.Add(originMeshIndex))
                {
                    meshFilter = meshFilters[originMeshIndex];
                    targetMeshFilter = UnityEngine.Object.Instantiate(meshFilter, meshFilter.transform.parent);

                    targetRenderer = targetMeshFilter.GetComponent<Renderer>();

                    lodGroup = targetRenderer.GetComponentInParent<LODGroup>();
                    lods = lodGroup == null ? null : lodGroup.GetLODs();
                    numLODs = lods == null ? 0 : lods.Length;
                    if (numLODs > 0)
                    {
                        for (j = 0; j < numLODs; ++j)
                        {
                            ref var lod = ref lods[j];
                            if (Array.IndexOf(lod.renderers, renderer) != -1)
                            {
                                numRenderers = lod.renderers.Length;
                                Array.Resize(ref lod.renderers, numRenderers + 1);

                                lod.renderers[numRenderers] = targetRenderer;
                            }
                        }

                        lodGroup.SetLODs(lods);
                    }

                    renderer = targetRenderer;
                    meshFilter = targetMeshFilter;
                }

                mesh = meshes[i];
                numSubMeshes = mesh.subMeshCount;
                sourceMaterials = originMaterials[originMeshIndex];
                destinationMaterials = new Material[numSubMeshes];
                for (j = 0; j < numSubMeshes; ++j)
                    destinationMaterials[j] = sourceMaterials[results.GetRootSubMeshIndices(i, numSubMeshes)[j]];

                renderer.sharedMaterials = destinationMaterials;

                //mesh.UploadMeshData(true);
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.UploadMeshData(true);
                meshFilter.sharedMesh = mesh;
            }

            results.Dispose();

#if UNITY_EDITOR
            UnityEditor.EditorUtility.ClearProgressBar();
#endif
        }

        public static IEnumerator Split(
            GameObject gameObject,
            Bounds bounds,
            float planeDistanceError, 
            int maxMeshFilterPerTime, 
            int innerloopBatchCount,
            int length,
            int width,
            int height,
            bool useNewMesh)
        {
            SplitMeshWrapper splitMeshWrapper;

            var meshFilters = gameObject.GetComponentsInChildren<MeshFilter>();
            int numMeshFilters = meshFilters.Length;
            for (int i = 0; i < numMeshFilters; i += maxMeshFilterPerTime)
            {
                Split<SplitVertex, SplitMeshWrapper>(
                    new ArraySegment<MeshFilter>(meshFilters, i, Mathf.Min(maxMeshFilterPerTime, numMeshFilters - i)),
                    bounds,
                    splitMeshWrapper,
                    planeDistanceError, 
                    innerloopBatchCount,
                    length,
                    width,
                    height,
                    useNewMesh);

#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Split..", $"{i}/{numMeshFilters}", i * 1.0f / numMeshFilters))
                {
                    UnityEditor.EditorUtility.ClearProgressBar();

                    yield break;
                }
#endif
                yield return null;
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.ClearProgressBar();
#endif
        }
    }
}
