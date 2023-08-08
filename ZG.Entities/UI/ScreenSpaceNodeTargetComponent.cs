using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using ZG;

namespace ZG
{
    public struct ScreenSpaceNode : IComponentData
    {
        public Entity entity;
    }

    public struct ScreenSpaceNodeTarget : IComponentData
    {
        public float3 offset;
        public ComponentType componentType;
    }
    
    public struct ScreenSpaceNodeSphere
    {
        public float radiusSquare;
        public float3 center;
        public ComponentType componentType;

        public bool Check(float3 point)
        {
            return math.lengthsq(point - center) < radiusSquare;
        }

        public static bool Test(NativeArray<ScreenSpaceNodeSphere> spheres, ComponentType componentType, float3 point)
        {
            if (!spheres.IsCreated || spheres.Length < 1)
                return true;

            bool result = true;
            int length = spheres.Length;
            ScreenSpaceNodeSphere sphere;
            for (int i = 0; i < length; ++i)
            {
                sphere = spheres[i];
                if (sphere.componentType == componentType)
                {
                    if (sphere.Check(point))
                        return true;
                    
                    result = false;
                }
            }

            return result;
        }
    }
    
    public struct ScreenSpaceNodeVisible : ICleanupComponentData
    {
        public Entity entity;
    }

    [EntityComponent]
    [EntityComponent(typeof(Transform))]
    public abstract class ScreenSpaceNodeTargetComponentBase : EntityProxyComponent
    {
        private float3? __offset;

        public bool isVail => this.HasComponent<ScreenSpaceNodeTarget>();

        public bool isSet => __offset != null;

        public void Set(in float3 offset)
        {
            ScreenSpaceNodeTarget screenSpaceNodeTarget;
            screenSpaceNodeTarget.offset = offset;
            screenSpaceNodeTarget.componentType = GetType();
            this.AddComponentData(screenSpaceNodeTarget);

            __offset = offset;
        }

        public void Unset()
        {
            __offset = null;

            this.RemoveComponent<ScreenSpaceNodeTarget>();
        }

        /*protected void Start()
        {
            if (__offset != null)
            {
                //Debug.LogError($"V {name} S");

                ScreenSpaceNodeTarget screenSpaceNodeTarget;
                screenSpaceNodeTarget.offset = __offset.Value;
                screenSpaceNodeTarget.componentType = GetType();
                this.AddComponentData(screenSpaceNodeTarget);
            }
        }*/

        protected void OnDisable()
        {
            Unset();
        }

        protected internal abstract Transform _Create();

        protected internal abstract void _Destroy(Transform node);
    }

    public class ScreenSpaceNodeTargetComponent : ScreenSpaceNodeTargetComponentBase
    {
        public GameObject target;
        public GameObject root;

        protected internal override Transform _Create()
        {
            if (root != null)
                root.SetActive(true);

            return target.transform;
        }

        protected internal override void _Destroy(Transform node)
        {
            if (root != null)
                root.SetActive(false);
        }
    }
}