using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace ZG
{
    [Serializable]
    public struct EntityTransform
    {
        public float3 scale;
        public RigidTransform rigidTransform;

        public float4x4 matrix
        {
            get
            {
                return float4x4.TRS(rigidTransform.pos, rigidTransform.rot, scale);
            }
        }
    }

    public static class HybridUtility
    {
        public static EntityTransform GetEntityTransform(this Transform transform)
        {
            EntityTransform entityTransform;
            entityTransform.scale = transform.localScale;
            entityTransform.rigidTransform.rot = transform.localRotation;
            entityTransform.rigidTransform.pos = transform.localPosition;

            return entityTransform;
        }

        public static void GetHierarchy(this Transform transform, Transform root, NativeList<EntityTransform> transforms)
        {
            if (transform != root)
            {
                Transform parent = transform.parent;
                if (parent != null)
                    GetHierarchy(parent, root, transforms);
            }

            EntityTransform entityTransform;
            entityTransform.scale = transform.localScale;
            entityTransform.rigidTransform.rot = transform.localRotation;
            entityTransform.rigidTransform.pos = transform.localPosition;

            transforms.Add(entityTransform);
        }

        public static float4x4 CalculateLocalToWorld(this NativeArray<EntityTransform> hierarchy, out float4x4 localToRoot)
        {
            localToRoot = float4x4.identity;

            int length = hierarchy.Length;
            for(int i = length - 1; i > 0; --i)
                localToRoot = math.mul(hierarchy[i].matrix, localToRoot);

            return math.mul(hierarchy[0].matrix, localToRoot);
        }
    }
}