using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Jobs;

namespace ZG
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FilterEffectSystem : SystemBase
    {
        [BurstCompile]
        private struct CopySources : IJobParallelForTransform
        {
            [ReadOnly]
            public NativeList<FilterEffectSource> sources;

            public NativeArray<Vector4> destinations;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                destinations[index] = math.float4(transform.position, sources[index].radius);
            }
        }

        [BurstCompile]
        private struct SetDestinationTypes : IJobParallelForTransform
        {
            [ReadOnly]
            public NativeArray<Vector4> sources;

            public NativeList<FilterEffectDestination> destinations;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                bool isActive = false;
                int length = sources.Length;
                Vector4 source;
                for (int i = 0; i < length; ++i)
                {
                    source = sources[i];
                    if (math.distance(transform.position,
                        new float3(source.x, source.y, source.z)) < source.w)
                    {
                        isActive = true;

                        break;
                    }
                }

                FilterEffectDestination destination = destinations[index];
                if (isActive)
                    destination.type |= FilterEffectDestination.Type.Active;
                else
                    destination.type &= ~FilterEffectDestination.Type.Active;

                destinations[index] = destination;
            }
        }

        [BurstCompile]
        private struct ReleaseDestinations : IJob
        {
            public NativeList<FilterEffectDestination> destinations;

            public void Execute() { }
        }

        private JobHandle __jobHandle;

        private EntityQuery __sources;
        private EntityQuery __destinations;

        private TransformAccessArrayEx __sourceTransforms;
        private TransformAccessArrayEx __destinationTransforms;

        private NativeArray<Vector4> __parameters;

        public ref NativeArray<Vector4> parameters
        {
            get
            {
                __jobHandle.Complete();
                __jobHandle = default;

                return ref __parameters;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            __sources = GetEntityQuery(ComponentType.ReadWrite<FilterEffectSource>(), TransformAccessArrayEx.componentType);
            __destinations = GetEntityQuery(ComponentType.ReadWrite<FilterEffectDestination>(), TransformAccessArrayEx.componentType);

            __sourceTransforms = new TransformAccessArrayEx(__sources);
            __destinationTransforms = new TransformAccessArrayEx(__destinations);

            Enabled = false;
        }

        protected override void OnDestroy()
        {
            if (__parameters.IsCreated)
                __parameters.Dispose();

            __sourceTransforms.Dispose();
            __destinationTransforms.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            TransformAccessArray transformAccessArray = __sourceTransforms.Convert(this);

            int length = transformAccessArray.length;
            if (length > 0)
            {
                if (!__parameters.IsCreated || __parameters.Length != length)
                {
                    if (__parameters.IsCreated)
                    {
                        __jobHandle.Complete();
                        __jobHandle = default;

                        __parameters.Dispose();
                    }

                    __parameters = new NativeArray<Vector4>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }

                JobHandle jobHandle;

                //__sources.AddDependency(inputDeps);

                CopySources copySources = new CopySources();
                copySources.sources = __sources.ToComponentDataListAsync<FilterEffectSource>(WorldUpdateAllocator, out jobHandle);
                copySources.destinations = __parameters;
                __jobHandle = copySources.Schedule(transformAccessArray, JobHandle.CombineDependencies(Dependency, jobHandle));

                //__destinations.AddDependency(inputDeps);

                transformAccessArray = __destinationTransforms.Convert(this);

                var destinations = __destinations.ToComponentDataListAsync<FilterEffectDestination>(WorldUpdateAllocator, out jobHandle);
                SetDestinationTypes setDestinationTypes;
                setDestinationTypes.sources = __parameters;
                setDestinationTypes.destinations = destinations;

                jobHandle = setDestinationTypes.Schedule(transformAccessArray, JobHandle.CombineDependencies(__jobHandle, jobHandle));

                //__destinations.AddDependency(jobHandle);

                //jobHandle.Complete();
                __destinations.CopyFromComponentDataListAsync(destinations, jobHandle, out jobHandle);

                ReleaseDestinations releaseDestinations;
                releaseDestinations.destinations = destinations;
                Dependency = releaseDestinations.Schedule(jobHandle);
            }
            else if (__parameters.IsCreated)
            {
                __jobHandle.Complete();
                __jobHandle = default;

                __parameters.Dispose();
            }
        }
    }

    [DisableAutoCreation, AlwaysSynchronizeSystem]
    public partial class FilterEffectDestinationSystem : SystemBase
    {
        public Vector4 disableColor;
        public Vector4 enableColor;
        public Vector4 activeColor;
        public Vector4 mixColor;

        private EntityQuery __group;
        private List<Renderer> __renderers;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(ComponentType.ReadOnly<FilterEffectDestination>(), ComponentType.ReadOnly<EntityObject<Transform>>());

            __renderers = new List<Renderer>();
        }
        
        protected override void OnUpdate()
        {
            using(var transforms = __group.ToComponentDataArray<EntityObject<Transform>>(Allocator.Temp))
            using (var destinations = __group.ToComponentDataArray<FilterEffectDestination>(Allocator.Temp))
            {
                int length = math.min(transforms.Length, destinations.Length);
                for (int i = 0; i < length; ++i)
                    __Set(transforms[i], destinations[i]);
            }
        }

        private void __Set(in EntityObject<Transform> transform, in FilterEffectDestination destination)
        {
            transform.value.GetComponentsInChildren(__renderers);
            if (__renderers.Count < 1)
                return;

            Color color;
            if ((destination.type & FilterEffectDestination.Type.Enable) == FilterEffectDestination.Type.Enable)
            {
                if ((destination.type & FilterEffectDestination.Type.Active) == FilterEffectDestination.Type.Active)
                    color = activeColor;
                else
                    color = enableColor;
            }
            else if ((destination.type & FilterEffectDestination.Type.Active) == FilterEffectDestination.Type.Active)
                color = mixColor;
            else
                color = disableColor;

            foreach (Renderer renderer in __renderers)
                renderer.material.SetColor("_FilterColor", color);
        }
    }

    public class RenderFilterEffect : MonoBehaviour
    {
        public int maxSourceLength;

        /*[Range(0.0f, 1.0f)]
        public float glossiness;
        [Range(0.0f, 1.0f)]
        public float metallic;*/

        [ColorUsage(true, true)]
        [System.NonSerialized]
        public Color mixColor = Color.blue;
        [ColorUsage(true, true)]
        [System.NonSerialized]
        public Color activeColor = Color.yellow;
        [ColorUsage(true, true)]
        public Color enableColor = Color.green;
        [ColorUsage(true, true)]
        public Color disableColor = Color.red;

        [ColorUsage(true, true)]
        public Color sourceColor;
        [ColorUsage(true, true)]
        public Color destinationColor;

        public string worldName = "Client";

        private World __world;
        private Shader __shader;
        private Camera __camera;
        private FilterEffectSystem __system;
        private FilterEffectDestinationSystem __destinationSystem;
        private Vector4[] __params;

        public static int activeCount;

        public World world
        {
            get
            {
                if (__world == null)
                    __world = WorldUtility.GetWorld(worldName);

                return __world;
            }
        }

        public FilterEffectSystem system
        {
            get
            {
                if (__system == null)
                    __system = world.GetExistingSystemManaged<FilterEffectSystem>();

                return __system;
            }
        }

        public FilterEffectDestinationSystem destinationSystem
        {
            get
            {
                if (__destinationSystem == null)
                    __destinationSystem = world.GetExistingSystemManaged<FilterEffectDestinationSystem>();

                return __destinationSystem;
            }
        }

        public Vector4[] GetParameters(out int length)
        {
            length = 0;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                return null;
#endif
            var system = this.system;

            bool isActive = activeCount > 0;
            if (system.Enabled != isActive)
            {
                system.Enabled = isActive;

                return null;
            }
            else if (!system.Enabled)
            {
                OnDisable();

                return null;
            }

            if (isActive)
            {
                NativeArray<Vector4> parameters = system.parameters;
                if (parameters.IsCreated)
                {
                    if (__params == null || __params.Length < maxSourceLength)
                        __params = new Vector4[maxSourceLength];

                    length = Mathf.Min(parameters.Length, __params.Length);
                    for (int i = 0; i < length; ++i)
                        __params[i] = parameters[i];

                    return __params;
                }
            }

            return null;
        }

        public void UpdateDestinations()
        {
            FilterEffectDestinationSystem destinationSystem = this.destinationSystem;
            if (destinationSystem != null)
            {
                destinationSystem.disableColor = disableColor;
                destinationSystem.enableColor = enableColor;
                destinationSystem.activeColor = activeColor;
                destinationSystem.mixColor = mixColor;

                destinationSystem.Update();
            }
        }

        void OnPreRender()
        {
            if (__camera == null)
            {
                __camera = GetComponent<Camera>();

                if (__camera != null)
                {
                    if (__shader == null)
                        __shader = Shader.Find("ZG/FilterEffect");

                    __camera.SetReplacementShader(__shader, "RenderType");
                }
            }

            __params = GetParameters(out int length);

            Shader.SetGlobalInt("g_FilterParamsLength", length);

            if(length > 0)
                Shader.SetGlobalVectorArray("g_FilterParams", __params);

            /*Shader.SetGlobalFloat("g_FilterGlossiness", glossiness);
            Shader.SetGlobalFloat("g_FilterMetallic", metallic);*/

            Shader.SetGlobalColor("g_FilterSourceColor", sourceColor);
            Shader.SetGlobalColor("g_FilterDestinationColor", destinationColor);

            UpdateDestinations();
        }

        void OnDisable()
        {
            if (__camera != null)
            {
                __camera.ResetReplacementShader();

                __camera = null;
            }
        }
    }
}