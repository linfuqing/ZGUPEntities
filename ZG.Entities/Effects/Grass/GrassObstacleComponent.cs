using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using UnityEngine;
using UnityEngine.Jobs;

namespace ZG
{
    public struct GrassObstacleData : IComponentData
    {
        public FixedString128Bytes pathPrefix;
    }

    public struct GrassObstacle : IBufferElementData
    {
        public float radius;
        public FixedString128Bytes transformPath;
    }

    public struct GrassObstacleEntity : ICleanupBufferElementData
    {
        public Entity value;
    }

    public struct GrassObstacleChild : IComponentData
    {
        public float radius;
        public Entity parent;
    }
    
    [EntityComponent(typeof(Transform))]
    [EntityComponent(typeof(GrassObstacleData))]
    [EntityComponent(typeof(GrassObstacle))]
    public class GrassObstacleComponent : EntityProxyComponent, IEntityComponent
    {
        [Serializable]
        public struct Obstacle
        {
            public float radius;
            public Transform child;
        }

        [SerializeField]
        internal Obstacle[] _obstacles = null;

        private static List<GrassObstacle> __obstacles;

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            var transform = base.transform;
            string pathPrefix = transform.GetPath(gameObjectEntity.transform);
            pathPrefix = string.IsNullOrEmpty(pathPrefix) ? string.Empty : pathPrefix + '/';
            GrassObstacleData instance;
            instance.pathPrefix = pathPrefix;
            assigner.SetComponentData(entity, instance);

            if (__obstacles == null)
                __obstacles = new List<GrassObstacle>();
            else
                __obstacles.Clear();

            int length = _obstacles.Length;
            Obstacle source;
            GrassObstacle destination;
            for(int i = 0; i < length; ++i)
            {
                source = _obstacles[i];

                destination.radius = source.radius;
                destination.transformPath = source.child.GetPath(transform) ?? string.Empty;

                __obstacles.Add(destination);
            }

            assigner.SetBuffer<GrassObstacle, List<GrassObstacle>>(EntityComponentAssigner.BufferOption.Override, entity, __obstacles);
        }
    }

    [UpdateInGroup(typeof(InitializationSystemGroup)), UpdateAfter(typeof(EntityObjectSystemGroup))]
    public partial class GrassObstacleFactorySystem : SystemBase
    {
        [BurstCompile]
        private struct Count : IJobChunk
        {
            public NativeCounter.Concurrent counter;

            [ReadOnly]
            public BufferTypeHandle<GrassObstacle> obstacleType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var obstacles = chunk.GetBufferAccessor(ref obstacleType);
                int numObstacles = obstacles.Length;
                for (int i = 0; i < numObstacles; ++i)
                    counter.Add(obstacles[i].Length);
            }
        }

        [BurstCompile]
        private struct SetChildValues : IJob
        {
            [ReadOnly]
            public NativeArray<Entity> entities;
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<ArchetypeChunk> chunks;
            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public BufferTypeHandle<GrassObstacle> obstacleType;

            //public ArchetypeChunkBufferType<GrassObstacleEntity> obstacleEntityType;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<GrassObstacleChild> obstacleChildren;

            public void Execute()
            {
                int entityIndex = 0, numChunks = chunks.Length, numEntities, numObstacles, i, j, k;
                Entity entity;
                ArchetypeChunk chunk;
                GrassObstacleChild child;
                GrassObstacle obstacle;
                //BufferAccessor<GrassObstacleEntity> obstacleEntityAccessor;
                BufferAccessor<GrassObstacle> obstacleAccessor;
                DynamicBuffer<GrassObstacle> obstacles;
                //DynamicBuffer<GrassObstacleEntity> obstacleEntities;
                NativeArray<Entity> entityArray;
                for (i = 0; i < numChunks; ++i)
                {
                    chunk = chunks[i];
                    
                    entityArray = chunk.GetNativeArray(entityType);
                    //obstacleEntityAccessor = chunk.GetBufferAccessor(obstacleEntityType);
                    obstacleAccessor = chunk.GetBufferAccessor(ref obstacleType);

                    numEntities = chunk.Count;
                    for (j = 0; j < numEntities; ++j)
                    {
                        entity = entityArray[j];
                        obstacles = obstacleAccessor[j];
                        numObstacles = obstacles.Length;

                        //obstacleEntities = obstacleEntityAccessor[j];
                        //obstacleEntities.ResizeUninitialized(numObstacles);
                        //NativeArray<Entity>.Copy(entities, entityIndex, obstacleEntities.Reinterpret<Entity>().AsNativeArray(), 0, numObstacles);
                        for (k = 0; k < numObstacles; ++k)
                        {
                            obstacle = obstacles[k];
                            child.radius = obstacle.radius;
                            child.parent = entity;
                            obstacleChildren[entities[entityIndex ++]] = child;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        private struct SetParentValues : IJob
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;
            [ReadOnly]
            public ComponentLookup<GrassObstacleChild> children;
            public BufferLookup<GrassObstacleEntity> parents;

            public void Execute()
            {
                GrassObstacleEntity parent;
                Entity entity;
                int length = entityArray.Length;
                for (int i = 0; i < length; ++i)
                {
                    entity = entityArray[i];
                    parent.value = entity;
                    parents[children[entity].parent].Add(parent);
                }
            }
        }

        private EntityArchetype __entityArchetype;
        private EntityQuery __groupToCreate;
        private EntityQuery __groupToDestroy;

        protected override void OnCreate()
        {
            base.OnCreate();

            __entityArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadOnly<GrassObstacleChild>(),
                ComponentType.ReadOnly<EntityObjects>(),
                TransformAccessArrayEx.componentType);

            __groupToCreate = GetEntityQuery(
                ComponentType.ReadOnly<GrassObstacle>(),
                ComponentType.Exclude<GrassObstacleEntity>(), 
                TransformAccessArrayEx.componentType);

            __groupToDestroy = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<GrassObstacleEntity>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(GrassObstacle)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<GrassObstacleEntity>(), 
                        ComponentType.ReadOnly<Disabled>()
                    }, 
                    
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
        }

        protected override void OnUpdate()
        {
            var entityManager = EntityManager;

            if (!__groupToDestroy.IsEmptyIgnoreFilter)
            {
                using (var entities = __groupToDestroy.ToEntityArray(Allocator.TempJob))
                {
                    foreach (var entity in entities)
                        entityManager.DestroyEntity(entityManager.GetBuffer<GrassObstacleEntity>(entity, true).Reinterpret<Entity>().AsNativeArray());
                }

                entityManager.RemoveComponent<GrassObstacleEntity>(__groupToDestroy);
            }

            if (!__groupToCreate.IsEmptyIgnoreFilter)
            {
                CompleteDependency();

                var obstacleType = GetBufferTypeHandle<GrassObstacle>(true);

                NativeCounter counter = new NativeCounter(Allocator.TempJob);
                Count count;
                count.counter = counter;
                count.obstacleType = obstacleType;
                count.Run(__groupToCreate);

                int countToCreate = counter.count;
                counter.Dispose();

                if (countToCreate > 0)
                {
                    var entities = entityManager.CreateEntity(__entityArchetype, countToCreate, Allocator.TempJob);
                    {
                        var chunks = __groupToCreate.ToArchetypeChunkArray(Allocator.TempJob);
                        var entityType = GetEntityTypeHandle();

                        var instanceType = GetComponentTypeHandle<GrassObstacleData>(true);
                        var transformType = GetComponentTypeHandle<EntityObject<Transform>>(true);
                        obstacleType = GetBufferTypeHandle<GrassObstacle>(true);
                        Job.WithCode(() =>
                        {
                            int childIndex = 0, numChunks = chunks.Length, numEntities, numObstacles, i, j, k;
                            string pathPrefix, path;
                            ArchetypeChunk chunk;
                            NativeArray<GrassObstacleData> instances;
                            NativeArray<EntityObject<Transform>> transforms;
                            BufferAccessor<GrassObstacle> obstacleAccessor;
                            DynamicBuffer<GrassObstacle> obstacles;
                            Transform root, transform;
                            for (i = 0; i < numChunks; ++i)
                            {
                                chunk = chunks[i];

                                transforms = chunk.GetNativeArray(ref transformType);
                                instances = chunk.GetNativeArray(ref instanceType);
                                obstacleAccessor = chunk.GetBufferAccessor(ref obstacleType);

                                numEntities = chunk.Count;
                                for (j = 0; j < numEntities; ++j)
                                {
                                    root = transforms[j].value;

                                    pathPrefix = j < instances.Length ? instances[j].pathPrefix.ToString() : null;

                                    obstacles = obstacleAccessor[j];

                                    numObstacles = obstacles.Length;
                                    for (k = 0; k < numObstacles; ++k)
                                    {
                                        path = string.IsNullOrEmpty(pathPrefix) ? obstacles[k].transformPath.ToString() : pathPrefix + obstacles[k].transformPath.ToString();
                                        transform = root.Find(path);
                                        if (transform == null)
                                        {
                                            Debug.LogError($"Fail To Find Path {path} Of {root.name}", root);

                                            ++childIndex;

                                            continue;
                                        }

                                        new EntityObject<Transform>(transform).SetTo(entities[childIndex++], entityManager);
                                    }
                                }
                            }
                        }).WithoutBurst().Run();

                        SetChildValues setChildValues;
                        setChildValues.entities = entities;
                        setChildValues.chunks = chunks;
                        setChildValues.entityType = entityType;
                        setChildValues.obstacleType = obstacleType;
                        setChildValues.obstacleChildren = GetComponentLookup<GrassObstacleChild>();
                        setChildValues.Run();

                        entityManager.AddComponent<GrassObstacleEntity>(__groupToCreate);

                        SetParentValues setParentValues;
                        setParentValues.entityArray = entities;
                        setParentValues.children = GetComponentLookup<GrassObstacleChild>(true);
                        setParentValues.parents = GetBufferLookup<GrassObstacleEntity>();
                        setParentValues.Run();
                    }
                }
            }
        }
    }
    
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class GrassObstacleSystem : SystemBase
    {
        [BurstCompile]
        private struct Write : IJobParallelForTransform
        {
            [ReadOnly]
            public NativeList<GrassObstacleChild> children;
            public NativeArray<Vector4> parameters;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                Vector3 position = transform.position, scale = transform.localScale;
                parameters[index] = new Vector4(position.x, position.y, position.z, math.max(math.max(scale.x, scale.y), scale.z) * children[index].radius);
            }
        }
        
        [BurstCompile]
        private struct Read : IJob
        {
            private struct Comparer : IComparer<Vector4>
            {
                public Vector3 position;

                public int Compare(Vector4 x, Vector4 y)
                {
                    return (new Vector3(x.x, x.y, x.z) - position).sqrMagnitude.CompareTo((new Vector3(y.x, y.y, y.z) - position).sqrMagnitude);
                }
            }
            
            public Vector3 position;
            public NativeArray<Vector4> parameters;

            public void Execute()
            {
                Comparer comparer;
                comparer.position = position;
                parameters.Sort(comparer);
            }
        }
        
        private JobHandle __jobHandle;
        private EntityQuery __group;
        private NativeList<Vector4> __parameters;
        private TransformAccessArrayEx __transformAccessArray;

        public int GetParameters(Vector3 position, Vector4[] parameters)
        {
            __jobHandle.Complete();
            __jobHandle = default;

            int count = __parameters.Length, maxCount = parameters.Length;
            var origins = __parameters.AsArray();
            if (count > maxCount)
            {
                Read read;
                read.position = position;
                read.parameters = origins;

                read.Run();

                count = maxCount;
            }
            else if(count < maxCount)
                parameters.MemClear(count, maxCount - count);

            NativeArray<Vector4>.Copy(origins, 0, parameters, 0, count);

            return count;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(ComponentType.ReadOnly<GrassObstacleChild>(), TransformAccessArrayEx.componentType);
            __transformAccessArray = new TransformAccessArrayEx(__group);

            __parameters = new NativeList<Vector4>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            __transformAccessArray.Dispose();

            if (__parameters.IsCreated)
                __parameters.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var transformAccessArray = __transformAccessArray.Convert(this);

            __parameters.ResizeUninitialized(transformAccessArray.length);

            var children = __group.ToComponentDataListAsync<GrassObstacleChild>(Allocator.TempJob, out JobHandle childrenJob);

            Write write;
            write.children = children;
            write.parameters = __parameters.AsArray();
            __jobHandle = write.Schedule(transformAccessArray, JobHandle.CombineDependencies(Dependency, childrenJob));

            Dependency = children.Dispose(__jobHandle);
        }
    }
}