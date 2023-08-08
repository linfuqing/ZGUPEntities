using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using ZG;
using static ZG.Avatar.Database;

[assembly: RegisterGenericJobType(typeof(ClearHashMap<Entity, float3>))]
[assembly: RegisterGenericJobType(typeof(CopyNativeArrayToComponentData<ScreenSpaceNodeVisible>))]
[assembly: ZG.RegisterEntityObject(typeof(ScreenSpaceNodeTargetComponentBase))]

namespace ZG
{
    [UpdateInGroup(typeof(EndFrameEntityCommandSystemGroup))]
    public partial class ScreenSpaceNodeStructChangeSystem : SystemBase
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
            public ComponentLookup<EntityObject<Transform>> transformResults;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<ScreenSpaceNode> nodeResults;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<ScreenSpaceNodeVisible> nodeVisibleResults;

            public void Execute(int index)
            {
                ScreenSpaceNodeVisible nodeVisible;
                nodeVisible.entity = entityArray[index];

                var node = nodes[index];
                nodeResults[nodeVisible.entity] = node;
                transformResults[nodeVisible.entity] = transforms[index];

                nodeVisibleResults[node.entity] = nodeVisible;
            }
        }

        public int innerloopBatchCount = 32;

        private ComponentLookup<EntityObject<Transform>> __transformResults;
        private ComponentLookup<ScreenSpaceNode> __nodeResults;
        private ComponentLookup<ScreenSpaceNodeVisible> __nodeVisibleResults;

        private EntityArchetype __entityArchetype;

        private EntityCommandPool<Entity>.Context __addComponentCommander;
        private EntityCommandPool<Entity>.Context __removeComponentCommander;

        public EntityCommandPool<Entity> addComponentCommander => __addComponentCommander.pool;

        public EntityCommandPool<Entity> removeComponentCommander => __removeComponentCommander.pool;

        protected override void OnCreate()
        {
            base.OnCreate();

            __transformResults = GetComponentLookup<EntityObject<Transform>>();
            __nodeResults = GetComponentLookup<ScreenSpaceNode>();
            __nodeVisibleResults = GetComponentLookup<ScreenSpaceNodeVisible>();

            __entityArchetype = EntityManager.CreateArchetype(TransformAccessArrayEx.componentType, ComponentType.ReadOnly<EntityObjects>(), ComponentType.ReadOnly<ScreenSpaceNode>());

            __addComponentCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
            __removeComponentCommander = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            __addComponentCommander.Dispose();
            __removeComponentCommander.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var enttiyManager = EntityManager;
            ScreenSpaceNodeTargetComponentBase screenSpaceNodeTargetComponent;
            if (!__removeComponentCommander.isEmpty)
            {
                var sources = new NativeList<Entity>(Allocator.Temp);
                var destinations = new NativeList<Entity>(Allocator.Temp);

                Entity temp;
                while (__removeComponentCommander.TryDequeue(out Entity entity))
                {
                    if (enttiyManager.HasComponent<ScreenSpaceNodeVisible>(entity))
                    {
                        temp = enttiyManager.GetComponentData<ScreenSpaceNodeVisible>(entity).entity;

                        screenSpaceNodeTargetComponent = enttiyManager.HasComponent<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity) ?
                              enttiyManager.GetComponentData<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity).value : null;
                        if (screenSpaceNodeTargetComponent != null)
                            screenSpaceNodeTargetComponent._Destroy(enttiyManager.HasComponent<EntityObject<Transform>>(temp) ?
                              enttiyManager.GetComponentData<EntityObject<Transform>>(temp).value : null);

                        destinations.Add(temp);
                    }

                    sources.Add(entity);
                }

                if (!sources.IsEmpty)
                {
                    enttiyManager.RemoveComponent<ScreenSpaceNodeVisible>(sources.AsArray());
                    enttiyManager.DestroyEntity(destinations.AsArray());
                }

                sources.Dispose();
                destinations.Dispose();
            }

            if (!__addComponentCommander.isEmpty)
            {
                Transform temp;
                EntityObject<Transform> transform;
                ScreenSpaceNode node;
                var nodes = new NativeList<ScreenSpaceNode>(Allocator.TempJob);
                var transforms = new NativeList<EntityObject<Transform>>(Allocator.TempJob);
                while (__addComponentCommander.TryDequeue(out var entity))
                {
                    screenSpaceNodeTargetComponent = enttiyManager.HasComponent<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity) ?
                        enttiyManager.GetComponentData<EntityObject<ScreenSpaceNodeTargetComponentBase>>(entity).value : null;
                    temp = screenSpaceNodeTargetComponent == null ? null : screenSpaceNodeTargetComponent._Create();
                    if (temp == null)
                        continue;

                    transform = new EntityObject<Transform>(temp);
                    //transform.Retain();

                    transforms.Add(transform);

                    node.entity = entity;
                    nodes.Add(node);
                }

                int length = nodes.Length;
                if (length > 0)
                {
                    var entities = nodes.AsArray().Reinterpret<Entity>();
                    enttiyManager.AddComponent<ScreenSpaceNodeVisible>(entities);

                    var entityArray = enttiyManager.CreateEntity(__entityArchetype, length, Allocator.TempJob);

                    //entityArray.Reinterpret<ScreenSpaceNodeVisible>().MoveTo(entities, GetComponentLookup<ScreenSpaceNodeVisible>());
                    ref var state = ref this.GetState();

                    SetValues setValues;
                    setValues.entityArray = entityArray;
                    setValues.nodes = nodes.AsArray();
                    setValues.transforms = transforms.AsArray();
                    setValues.transformResults = __transformResults.UpdateAsRef(ref state);
                    setValues.nodeResults = __nodeResults.UpdateAsRef(ref state);
                    setValues.nodeVisibleResults = __nodeVisibleResults.UpdateAsRef(ref state);
                    var jobHandle = setValues.ScheduleByRef(length, innerloopBatchCount, Dependency);
                    //jobHandle = JobHandle.CombineDependencies(__nodes.Dispose(jobHandle), __transforms.Dispose(jobHandle));

                    Dependency = JobHandle.CombineDependencies(nodes.Dispose(jobHandle), transforms.Dispose(jobHandle));
                }
                else
                {
                    nodes.Dispose();
                    transforms.Dispose();
                }
            }
        }
    }

    [CreateAfter(typeof(ScreenSpaceNodeStructChangeSystem))]
    public partial class ScreenSpaceNodeSystem : SystemBase, IEntityCommandProducerJob
    {
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

            var endFrameBarrier = World.GetExistingSystemManaged<ScreenSpaceNodeStructChangeSystem>();
            __addComponentCommander = endFrameBarrier.addComponentCommander;
            __removeComponentCommander = endFrameBarrier.removeComponentCommander;

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
            if (sphereDelegates != null)
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