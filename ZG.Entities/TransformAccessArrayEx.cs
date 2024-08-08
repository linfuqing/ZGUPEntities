using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Jobs;

[assembly: ZG.RegisterEntityObject(typeof(Transform))]

namespace ZG
{
    public class TransformAccessArrayEx : System.IDisposable
    {
        private int __version;
        private EntityQuery __group;
        private TransformAccessArray __value;

        public static ComponentType componentType
        {
            get
            {
                return ComponentType.ReadOnly<EntityObject<Transform>>();
            }
        }

        public static ComponentType componentTypeExcluded
        {
            get
            {
                return ComponentType.Exclude<EntityObject<Transform>>();
            }
        }

        public TransformAccessArrayEx(EntityQuery group)
        {
            __version = 0;
            __group = group;
            __value = default;
        }

        public void Dispose()
        {
            if(__value.isCreated)
                __value.Dispose();
        }
        
        public TransformAccessArray Convert(ComponentSystemBase system)
        {
            bool isCreated = __value.isCreated;
            int version = system.EntityManager.GetComponentOrderVersion<EntityObject<Transform>>();
            if (!isCreated || ChangeVersionUtility.DidChange((uint)version, (uint)__version)/* || value.__value.length != value.__group.CalculateEntityCount()*/)
            {
                __version = version;

                UnityEngine.Profiling.Profiler.BeginSample("DirtyTransformAccessArrayUpdate");
                //var transforms = value.__group.ToComponentArray<Transform>();

                //TODO
                __group.CompleteDependency();

                using(var sources = __group.ToComponentDataArray<EntityObject<Transform>>(Allocator.Temp))
                {
                    int destination = sources.Length;
                    EntityObject<Transform> transform;
                    if (isCreated)
                    {
                        int source = __value.length;
                        for(int i = source - 1; i >= destination; --i)
                            __value.RemoveAtSwapBack(i);

                        Transform value;
                        for (int i = 0; i < destination; ++i)
                        {
                            transform = sources[i];

                            value = transform.isCreated ? transform.value : null;
                            if(i < source)
                                __value[i] = value;
                            else
                                __value.Add(value);
                        }
                    }
                    else
                    {
                        var destinations = new Transform[destination];
                        for (int i = 0; i < destination; ++i)
                        {
                            transform = sources[i];
                            //UnityEngine.Assertions.Assert.AreNotEqual(null, (object)sources[i].value);

                            destinations[i] = transform.isCreated ? transform.value : null;
                        }
                        
                        __value = new TransformAccessArray(destinations);
                    }
                }

                UnityEngine.Profiling.Profiler.EndSample();
            }

            return __value;
        }

    }
}