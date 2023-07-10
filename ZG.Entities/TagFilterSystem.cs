using Unity.Entities;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using System;

namespace ZG
{
    public struct Tag : ISharedComponentData, IEquatable<Tag>
    {
        public FixedString32Bytes value;

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public bool Equals(Tag other)
        {
            return value.Equals(other.value);
        }
    }

    public struct TagData : IComponentData
    {
        public FixedString32Bytes value;
    }

    public struct TagFilterData : IComponentData
    {
        public FixedString32Bytes value;
    }

    public struct TagFilterInfo : ICleanupComponentData
    {
        public FixedString32Bytes value;
    }

    [BurstCompile, RequireMatchingQueriesForUpdate]
    public partial struct TagFilterSystem : ISystem
    {
        [BurstCompile]
        private struct Create : IJob
        {
            [ReadOnly]
            public NativeArray<TagFilterData> instances;
            public NativeHashMap<FixedString32Bytes, int> counters;

            public NativeList<FixedString32Bytes> tags;

            public void Execute()
            {
                tags.Clear();

                int numInstances = instances.Length, count;
                FixedString32Bytes tag;
                for(int i = 0; i < numInstances; ++i)
                {
                    tag = instances[i].value;
                    if (counters.TryGetValue(tag, out count))
                        ++count;
                    else
                    {
                        count = 1;

                        tags.Add(tag);
                    }

                    counters[tag] = count;
                }
            }
        }

        [BurstCompile]
        private struct Destroy : IJob
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<TagFilterInfo> infos;
            public NativeHashMap<FixedString32Bytes, int> counters;

            public NativeList<FixedString32Bytes> tags;

            public void Execute()
            {
                tags.Clear();

                int numInfos = infos.Length, count;
                FixedString32Bytes tag;
                for (int i = 0; i < numInfos; ++i)
                {
                    tag = infos[i].value;
                    if (counters.TryGetValue(tag, out count) && count > 1)
                        counters[tag] = --count;
                    else if(counters.Remove(tag))
                        tags.Add(tag);
                }
            }
        }

        [BurstCompile]
        private struct SetValues  :IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<TagFilterData> instances;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<TagFilterInfo> infos;

            public void Execute(int index)
            {
                TagFilterInfo info;
                info.value = instances[index].value;
                infos[entityArray[index]] = info;
            }
        }

        private EntityQuery __filtersToCreate;
        private EntityQuery __filtersToDestroy;

        private EntityQuery __tagsToInit;
        private EntityQuery __tagsToEnable;
        private EntityQuery __tagsToDisable;

        private NativeList<FixedString32Bytes> __valuesToEnable;
        private NativeList<FixedString32Bytes> __valuesToDisable;

        private NativeHashMap<FixedString32Bytes, int> __counters;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using(var builder = new EntityQueryBuilder(Allocator.Temp))
                __filtersToCreate = builder
                    .WithAll<TagFilterData>()
                    .WithNone<TagFilterInfo>()
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __filtersToDestroy = builder
                    .WithAll<TagFilterInfo>()
                    .WithNone<TagFilterData>()
                    .AddAdditionalQuery()
                    .WithAll<TagFilterInfo, Disabled>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __tagsToInit = builder
                    .WithAll<TagData>()
                    .WithNone<Tag>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __tagsToEnable = builder
                    .WithAll<Tag, Disabled>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __tagsToDisable = builder
                    .WithAll<Tag>()
                    .Build(ref state);

            __valuesToEnable = new NativeList<FixedString32Bytes>(Allocator.Persistent);
            __valuesToDisable = new NativeList<FixedString32Bytes>(Allocator.Persistent);
            __counters = new NativeHashMap<FixedString32Bytes, int>(1, Allocator.Persistent);
        }

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __valuesToEnable.Dispose();
            __valuesToDisable.Dispose();
            __counters.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //TODO: 
            state.CompleteDependency();

            var entityManager = state.EntityManager;
            var instances = __filtersToCreate.ToComponentDataArray<TagFilterData>(Allocator.TempJob);
            {
                Create create;
                create.instances = instances;
                create.counters = __counters;
                create.tags = __valuesToEnable;
                create.Run();

                var entityArray = __filtersToCreate.ToEntityArray(Allocator.TempJob);
                {
                    entityManager.AddComponent<TagFilterInfo>(__filtersToCreate);

                    SetValues setValues;
                    setValues.entityArray = entityArray;
                    setValues.instances = instances;
                    setValues.infos = state.GetComponentLookup<TagFilterInfo>();
                    setValues.Run(entityArray.Length);
                }
            }

            var infos = __filtersToDestroy.ToComponentDataArray<TagFilterInfo>(Allocator.TempJob);
            {
                entityManager.RemoveComponent<TagFilterInfo>(__filtersToDestroy);

                Destroy destroy;
                destroy.infos = infos;
                destroy.counters = __counters;
                destroy.tags = __valuesToDisable;
                destroy.Run();
            }

            Tag tag;
            if (__counters.Count > 0)
            {
                using (var entityArray = __tagsToInit.ToEntityArray(Allocator.TempJob))
                {
                    int numEntities = entityArray.Length;
                    Entity entity;
                    for (int i = 0; i < numEntities; ++i)
                    {
                        entity = entityArray[i];

                        tag.value = entityManager.GetComponentData<TagData>(entity).value;
                        entityManager.AddSharedComponent(entity, tag);
                    }

                    entityManager.RemoveComponent<TagData>(entityArray);
                }
            }

            foreach (var value in __valuesToEnable)
            {
                tag.value = value;
                __tagsToEnable.SetSharedComponentFilter(tag);
                entityManager.RemoveComponent<Disabled>(__tagsToEnable);
            }

            foreach (var value in __valuesToDisable)
            {
                tag.value = value;
                __tagsToDisable.SetSharedComponentFilter(tag);
                entityManager.AddComponent<Disabled>(__tagsToDisable);
            }
        }
    }
}