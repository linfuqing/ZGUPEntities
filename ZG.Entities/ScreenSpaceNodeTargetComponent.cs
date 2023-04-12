using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using ZG;

[assembly: RegisterGenericJobType(typeof(CopyNativeArrayToComponentData<ScreenSpaceNodeVisible>))]
[assembly: ZG.RegisterEntityObject(typeof(ScreenSpaceNodeTargetComponentBase))]

namespace ZG
{
    [Serializable]
    public struct ScreenSpaceNode : IComponentData
    {
        public Entity entity;
    }

    [Serializable]
    public struct ScreenSpaceNodeTarget : IComponentData
    {
        public float3 offset;
        public ComponentType componentType;
    }
    
    [Serializable]
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
    
    [Serializable]
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

        public bool Set(in float3 offset)
        {
            if (gameObjectEntity.isCreated)
            {
                //Debug.LogError($"V {name} S");

                ScreenSpaceNodeTarget screenSpaceNodeTarget;
                screenSpaceNodeTarget.offset = offset;
                screenSpaceNodeTarget.componentType = GetType();
                this.AddComponentData(screenSpaceNodeTarget);
            }
            else
                gameObjectEntity.onCreated += __OnCreated;

            __offset = offset;

            return true;
        }

        public void Unset()
        {
            __offset = null;

            if (gameObjectEntity.isCreated)
            {
                //Debug.LogError($"V {name} U");

                this.RemoveComponent<ScreenSpaceNodeTarget>();
            }
            else
                gameObjectEntity.onCreated -= __OnCreated;
        }

        protected void OnDisable()
        {
            Unset();
        }

        private void __OnCreated()
        {
            if (__offset != null)
            {
                //Debug.LogError($"V {name} S");

                ScreenSpaceNodeTarget screenSpaceNodeTarget;
                screenSpaceNodeTarget.offset = __offset.Value;
                screenSpaceNodeTarget.componentType = GetType();
                this.AddComponentData(screenSpaceNodeTarget);
            }
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

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ScreenSpaceNodeSystem : SystemBase, IEntityCommandProducerJob
    {
        private class Visible : IEntityCommander<Entity>
        {
            [BurstCompile]
            private struct SetValues : IJobParallelFor
            {
                [ReadOnly, DeallocateOnJobCompletion]
                public NativeArray<Entity> entityArray;
                [ReadOnly]
                public NativeArray<ScreenSpaceNode> nodes;
                [ReadOnly]
                public NativeArray<EntityObject<Transform>> transforms;

                [NativeDisableParallelForRestriction]
                public ComponentLookup<ScreenSpaceNode> nodeResults;
                [NativeDisableParallelForRestriction]
                public ComponentLookup<EntityObject<Transform>> transformResults;

                public void Execute(int index)
                {
                    Entity entity = entityArray[index];
                    nodeResults[entity] = nodes[index];
                    transformResults[entity] = transforms[index];
                }
            }

            public const int innerloopBatchCount = 32;

            public EntityArchetype entityArchetype;

            private NativeList<ScreenSpaceNode> __nodes;
            private NativeList<EntityObject<Transform>> __transforms;

            public void Execute(
                EntityCommandPool<Entity>.Context context, 
                EntityCommandSystem system,
                ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
                in JobHandle inputDeps)
            {
                if (__nodes.IsCreated)
                    __nodes.Clear();
                else
                    __nodes = new NativeList<ScreenSpaceNode>(Allocator.Persistent);

                if (__transforms.IsCreated)
                    __transforms.Clear();
                else
                    __transforms = new NativeList<EntityObject<Transform>>(Allocator.Persistent);

                {
                    var enttiyManager = system.EntityManager;
                    ScreenSpaceNodeTargetComponentBase screenSpaceNodeTargetComponent;
                    Transform temp;
                    EntityObject<Transform> transform;
                    ScreenSpaceNode node;
                    while (context.TryDequeue(out var entity))
                    {
                        screenSpaceNodeTargetComponent = enttiyManager.HasComponent<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity) ?
                            enttiyManager.GetComponentData<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity).value : null;
                        temp = screenSpaceNodeTargetComponent == null ? null : screenSpaceNodeTargetComponent._Create();
                        if (temp == null)
                            continue;

                        transform = new EntityObject<Transform>(temp);
                        transform.Retain();

                        __transforms.Add(transform);

                        node.entity = entity;
                        __nodes.Add(node);
                    }

                    int length = __nodes.Length;
                    if (length > 0)
                    {
                        dependency.CompleteAll(inputDeps);

                        var entities = __nodes.AsArray().Reinterpret<Entity>();
                        enttiyManager.AddComponent<ScreenSpaceNodeVisible>(entities);

                        var entityArray = enttiyManager.CreateEntity(entityArchetype, length, Allocator.TempJob);

                        entityArray.Reinterpret<ScreenSpaceNodeVisible>().MoveTo(entities, system.GetComponentLookup<ScreenSpaceNodeVisible>());

                        SetValues setValues;
                        setValues.entityArray = entityArray;
                        setValues.nodes = __nodes.AsArray();
                        setValues.transforms = __transforms.AsArray();
                        setValues.nodeResults = system.GetComponentLookup<ScreenSpaceNode>();
                        setValues.transformResults = system.GetComponentLookup<EntityObject<Transform>>();
                        var jobHandle = setValues.Schedule(length, innerloopBatchCount, inputDeps);
                        //jobHandle = JobHandle.CombineDependencies(__nodes.Dispose(jobHandle), __transforms.Dispose(jobHandle));

                        dependency[typeof(ScreenSpaceNode)] = jobHandle;
                        dependency[typeof(EntityObject<Transform>)] = jobHandle;

                        return;
                    }
                }

                //__nodes.Dispose();
                //__transforms.Dispose();
            }

            public void Dispose()
            {
                if (__nodes.IsCreated)
                    __nodes.Dispose();

                if (__transforms.IsCreated)
                    __transforms.Dispose();
            }
        }

        private class Invisible : IEntityCommander<Entity>
        {
            private NativeList<Entity> __sources;
            private NativeList<Entity> __destinations;

            public void Execute(
                EntityCommandPool<Entity>.Context context, 
                EntityCommandSystem system,
                ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
                in JobHandle inputDeps)
            {
                if (__sources.IsCreated)
                    __sources.Clear();
                else
                    __sources = new NativeList<Entity>(Allocator.Persistent);

                if (__destinations.IsCreated)
                    __destinations.Clear();
                else
                    __destinations = new NativeList<Entity>(Allocator.Persistent);

                {
                    var enttiyManager = system.EntityManager;
                    ScreenSpaceNodeTargetComponentBase screenSpaceNodeTargetComponent;
                    Entity temp;
                    while (context.TryDequeue(out Entity entity))
                    {
                        if (enttiyManager.HasComponent<ScreenSpaceNodeVisible>(entity))
                        {
                            dependency.CompleteAll(inputDeps);

                            temp = enttiyManager.GetComponentData<ScreenSpaceNodeVisible>(entity).entity;

                            screenSpaceNodeTargetComponent = enttiyManager.HasComponent<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity) ?
                                  enttiyManager.GetComponentData<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity).value : null;
                            if(screenSpaceNodeTargetComponent != null)
                                screenSpaceNodeTargetComponent._Destroy(enttiyManager.HasComponent<EntityObject<Transform>>(temp) ?
                                  enttiyManager.GetComponentData<EntityObject<Transform>>(temp).value : null);

                            __destinations.Add(temp);
                        }

                        __sources.Add(entity);
                    }

                    if (!__sources.IsEmpty)
                    {
                        dependency.CompleteAll(inputDeps);

                        enttiyManager.RemoveComponent<ScreenSpaceNodeVisible>(__sources.AsArray());
                        enttiyManager.DestroyEntity(__destinations.AsArray());
                    }
                }
            }

            public void Dispose()
            {
                if (__sources.IsCreated)
                    __sources.Dispose();

                if (__destinations.IsCreated)
                    __destinations.Dispose();
            }
        }

        [BurstCompile]
        private struct CreateNodes : IJobParallelForTransform, IEntityCommandProducerJob
        {
            public float nearClipPlane;
            public float farClipPlane;
            public float4x4 viewProjectionMatrix;

            [ReadOnly]
            public NativeArray<ScreenSpaceNodeSphere> spheres;

            [ReadOnly]
            public NativeList<Entity> entityArray;

            [ReadOnly]
            public NativeList<ScreenSpaceNodeTarget> targets;

            public EntityCommandQueue<Entity>.ParallelWriter entityManager;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                ScreenSpaceNodeTarget target = targets[index];
                if (!ScreenSpaceNodeSphere.Test(spheres, target.componentType, transform.position))
                    return;

                float4 position = Project(transform, viewProjectionMatrix, target.offset);
                if (IsOnScreen(nearClipPlane, farClipPlane, position))
                    entityManager.Enqueue(entityArray[index]);
            }
        }

        [BurstCompile]
        private struct DestroyNodes : IJobParallelForTransform
        {
            public float nearClipPlane;
            public float farClipPlane;
            public float4x4 viewProjectionMatrix;

            [ReadOnly]
            public NativeArray<ScreenSpaceNodeSphere> spheres;

            [ReadOnly]
            public NativeList<Entity> entityArray;

            [ReadOnly]
            public NativeList<ScreenSpaceNodeTarget> targets;

            public NativeParallelHashMap<Entity, float3>.ParallelWriter positions;

            public EntityCommandQueue<Entity>.ParallelWriter entityManager;
            
            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                Entity entity = entityArray[index];

                ScreenSpaceNodeTarget target = targets[index];
                float4 position = Project(transform, viewProjectionMatrix, target.offset);
                positions.TryAdd(entity, position.xyz);
                if (!ScreenSpaceNodeSphere.Test(spheres, target.componentType, transform.position) || !IsOnScreen(nearClipPlane, farClipPlane, position))
                    entityManager.Enqueue(entity);
            }
        }

        [BurstCompile]
        private struct CopyEntities : IJobParallelFor
        {
            [ReadOnly]
            public NativeList<Entity> entityArray;
            public EntityCommandQueue<Entity>.ParallelWriter entityManager;

            public void Execute(int index)
            {
                entityManager.Enqueue(entityArray[index]);
            }
        }

        [BurstCompile]
        private struct TransformNodes : IJobParallelForTransform
        {
            public float lerp;
            public float width;
            public float height;
            
            [ReadOnly]
            public NativeList<ScreenSpaceNode> nodes;

            [ReadOnly]
            public NativeParallelHashMap<Entity, float3> positions;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                ScreenSpaceNode node = nodes[index];
                float3 position;
                if (positions.TryGetValue(node.entity, out position))
                {
                    position = (position + 1.0f) * 0.5f;
                    transform.position = math.lerp(
                        transform.position,
                        new float3(position.x * width, position.y * height, position.z),
                        lerp);
                }
            }
        }

        public static float4 Project(TransformAccess transform, float4x4 viewProjectionMatrix, float3 offset)
        {
            float4 position = math.mul(viewProjectionMatrix, new float4((float3)transform.position + transform.localScale * math.mul(transform.rotation, offset), 1.0f));
            return math.abs(position.w) > math.FLT_MIN_NORMAL ? new float4(position.xyz / position.w, position.w) : position;
        }

        public static bool IsOnScreen(float min, float max, float4 position)
        {
            return position.w > min && 
                position.w < max && 
                position.x > -1.0f && 
                position.x < 1.0f && 
                position.y > -1.0f && 
                position.y < 1.0f;
        }

        public Camera camera;
        
        public event Func<ScreenSpaceNodeSphere> onUpdate; 

        public int innerloopBatchCount = 16;

        public float lerp = 0.9f;

        private EntityQuery __invisibleTargets;
        private EntityQuery __visibleTargets;
        private EntityQuery __invailTargets;
        private EntityQuery __nodes;

        private TransformAccessArrayEx __invisibleTransforms;
        private TransformAccessArrayEx __visibleTransforms;
        private TransformAccessArrayEx __nodeTransforms;

        private EntityCommandPool<Entity> __addComponentCommander;
        private EntityCommandPool<Entity> __removeComponentCommander;
        
        private NativeList<ScreenSpaceNodeSphere> __spheres;
        private NativeParallelHashMap<Entity, float3> __positions;
        
        protected override void OnCreate()
        {
            __invisibleTargets = GetEntityQuery(ComponentType.ReadOnly<ScreenSpaceNodeTarget>(), ComponentType.Exclude<ScreenSpaceNodeVisible>(), TransformAccessArrayEx.componentType);
            __visibleTargets = GetEntityQuery(ComponentType.ReadOnly<ScreenSpaceNodeTarget>(), ComponentType.ReadOnly<ScreenSpaceNodeVisible>(), TransformAccessArrayEx.componentType);

            __invailTargets = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<ScreenSpaceNodeVisible>(),
                    },
                    None = new ComponentType[]
                    {
                        typeof(ScreenSpaceNodeTarget),
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                }, 
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<ScreenSpaceNodeVisible>(),
                        ComponentType.ReadOnly<Disabled>()
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                }
            );

            __nodes = GetEntityQuery(ComponentType.ReadWrite<ScreenSpaceNode>(), TransformAccessArrayEx.componentType);

            __invisibleTransforms = new TransformAccessArrayEx(__invisibleTargets);
            __visibleTransforms = new TransformAccessArrayEx(__visibleTargets);
            __nodeTransforms = new TransformAccessArrayEx(__nodes);

            var endFrameBarrier = World.GetOrCreateSystemManaged<EndFrameEntityCommandSystem>();
            Visible visible = new Visible();
            visible.entityArchetype = EntityManager.CreateArchetype(TransformAccessArrayEx.componentType, ComponentType.ReadOnly<EntityObjects>(), ComponentType.ReadOnly<ScreenSpaceNode>());
            __addComponentCommander = endFrameBarrier.Create<Entity, Visible>(EntityCommandManager.QUEUE_ADD, visible);
            __removeComponentCommander = endFrameBarrier.GetOrCreate<Entity, Invisible>(EntityCommandManager.QUEUE_REMOVE);

            __positions = new NativeParallelHashMap<Entity, float3>(1, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            //__nodes.Dispose();
            
            if (__spheres.IsCreated)
                __spheres.Dispose();

            __invisibleTransforms.Dispose();
            __visibleTransforms.Dispose();
            __nodeTransforms.Dispose();
            
            __positions.Dispose();
        }

        protected override void OnUpdate()
        {
            if (camera == null/* || !camera.isActiveAndEnabled*/)
            {
                camera = Camera.current;
                if (camera == null)
                    camera = Camera.main;
            }

            if (camera == null)
                return;

            if (__spheres.IsCreated)
                __spheres.Clear();
            else
                __spheres = new NativeList<ScreenSpaceNodeSphere>(Allocator.Persistent);

            Delegate[] sphereDelegates = this.onUpdate == null ? null : this.onUpdate.GetInvocationList();
            if(sphereDelegates != null)
            {
                Func<ScreenSpaceNodeSphere> onUpdate;
                foreach (Delegate sphereDelegate in sphereDelegates)
                {
                    onUpdate = sphereDelegate as Func<ScreenSpaceNodeSphere>;
                    if (onUpdate == null)
                        continue;

                    __spheres.Add(onUpdate());
                }
            }

            /*if (version != (entityManager == null ? 0 : entityManager.Version))
            {
                RequireForUpdate(__invisibleTargets);
                RequireForUpdate(__visibleTargets);
                RequireForUpdate(__nodes);
            }*/

            float nearClipPlane = camera.nearClipPlane, farClipPlane = camera.farClipPlane;
            float4x4 viewProjectionMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;

            var removeComponentCommander = __removeComponentCommander.Create();
            var entityManager = removeComponentCommander.parallelWriter;

            var worldUpdateAllocator = WorldUpdateAllocator;
            var transformAccessArray = __visibleTransforms.Convert(this);
            int visibleEntityCount = transformAccessArray.length;
            JobHandle positionJobHandle, inputDeps = Dependency;
            JobHandle? visibleJobHandle;
            if (visibleEntityCount > 0)
            {
                DestroyNodes destroyNodes;
                destroyNodes.nearClipPlane = nearClipPlane;
                destroyNodes.farClipPlane = farClipPlane;
                destroyNodes.viewProjectionMatrix = viewProjectionMatrix;

                destroyNodes.spheres = __spheres.AsArray();
                destroyNodes.entityArray = __visibleTargets.ToEntityListAsync(worldUpdateAllocator, out JobHandle destroyEntityjobHandle);

                destroyNodes.targets = __visibleTargets.ToComponentDataListAsync<ScreenSpaceNodeTarget>(worldUpdateAllocator, out JobHandle destroyTargetJobHandle);

                var jobHandle = JobHandle.CombineDependencies(destroyEntityjobHandle, destroyTargetJobHandle, __positions.Clear(visibleEntityCount, inputDeps));

                destroyNodes.positions = __positions.AsParallelWriter();
                destroyNodes.entityManager = entityManager;

                positionJobHandle = destroyNodes.Schedule(transformAccessArray, jobHandle);

                visibleJobHandle = positionJobHandle;
            }
            else
            {
                positionJobHandle = Dependency;

                visibleJobHandle = null;
            }

            int invailEntityCount = __invailTargets.CalculateEntityCount();
            JobHandle? copyEntityJobHandle;
            if (invailEntityCount > 0)
            {
                CopyEntities copyEntities;
                copyEntities.entityArray = __invailTargets.ToEntityListAsync(worldUpdateAllocator, out var entityJobHandle);
                copyEntities.entityManager = entityManager;
                copyEntityJobHandle = copyEntities.Schedule(invailEntityCount, innerloopBatchCount, JobHandle.CombineDependencies(entityJobHandle, visibleJobHandle == null ? inputDeps : visibleJobHandle.Value));

                visibleJobHandle = copyEntityJobHandle;
            }
            else
                copyEntityJobHandle = null;

            if (visibleJobHandle != null)
                removeComponentCommander.AddJobHandleForProducer<ScreenSpaceNodeSystem>(visibleJobHandle.Value);

            transformAccessArray = __invisibleTransforms.Convert(this);
            int invisibleEntityCount = transformAccessArray.length;
            JobHandle? invisibleJobHandle;
            if (invisibleEntityCount > 0)
            {
                var addComponentCommander = __addComponentCommander.Create();

                CreateNodes createNodes;
                createNodes.nearClipPlane = nearClipPlane;
                createNodes.farClipPlane = farClipPlane;
                createNodes.viewProjectionMatrix = viewProjectionMatrix;

                createNodes.spheres = __spheres.AsArray();
                createNodes.entityArray = __invisibleTargets.ToEntityListAsync(worldUpdateAllocator, out JobHandle createEntityjobHandle);

                createNodes.targets = __invisibleTargets.ToComponentDataListAsync<ScreenSpaceNodeTarget>(worldUpdateAllocator, out JobHandle createTargetJobHandle);

                createNodes.entityManager = addComponentCommander.parallelWriter;

                var jobHandle = createNodes.Schedule(transformAccessArray, JobHandle.CombineDependencies(inputDeps, createEntityjobHandle, createTargetJobHandle));

                addComponentCommander.AddJobHandleForProducer<CreateNodes>(jobHandle);

                invisibleJobHandle = jobHandle;
            }
            else
                invisibleJobHandle = null;

            transformAccessArray = __nodeTransforms.Convert(this);

            TransformNodes transformNodes;
            transformNodes.lerp = lerp;
            transformNodes.width = camera.pixelWidth;
            transformNodes.height = camera.pixelHeight;
            transformNodes.nodes = __nodes.ToComponentDataListAsync<ScreenSpaceNode>(worldUpdateAllocator, out var nodeJobHandle);
            transformNodes.positions = __positions;
            
            var result = transformNodes.Schedule(transformAccessArray, JobHandle.CombineDependencies(nodeJobHandle, positionJobHandle));

            if (copyEntityJobHandle != null)
                result = JobHandle.CombineDependencies(result, copyEntityJobHandle.Value);

            if (invisibleJobHandle != null)
                result = JobHandle.CombineDependencies(result, invisibleJobHandle.Value);

            Dependency = result;
        }
    }
}