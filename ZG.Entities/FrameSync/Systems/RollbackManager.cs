using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using ZG;

[assembly: RegisterGenericJobType(typeof(RollbackResize<Entity>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<RollbackChunk>))]

namespace ZG
{
    public interface IRollbackContainer : IDisposable
    {
        void Clear();
    }

    public interface IRollbackCore
    {
        void ScheduleRestore(uint frameIndex, ref SystemState systemState);

        void ScheduleSave(uint frameIndex, in EntityTypeHandle entityType, ref SystemState systemState);

        void ScheduleClear(uint maxFrameIndex, uint frameIndex, uint frameCount, ref SystemState systemState);
    }

    public struct RollbackFrame : IComponentData
    {
        public uint index;

        public static EntityQuery GetEntityQuery(ref SystemState state)
        {
            return state.GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RollbackFrame>()
                },
                Options = EntityQueryOptions.IncludeSystems
            });
        }
    }

    /*public struct RollbackRestoreFrame : IComponentData
    {
        public uint index;
    }*/

    public struct RollbackFrameRestore : IComponentData
    {
        public uint index;
    }

    public struct RollbackFrameSave : IComponentData
    {
        public uint minIndex;
    }

    public struct RollbackFrameClear : IComponentData
    {
        public uint maxIndex;
    }

    public struct RollbackSaveData
    {
        public NativeArray<int> entityCount;
        public NativeArray<int> chunkBaseEntityIndices;
        public LookupJobManager lookupJobManager;

        public RollbackSaveData(in EntityQuery group, ref SystemState state)
        {
            var allocator = state.WorldUpdateAllocator;
            entityCount = CollectionHelper.CreateNativeArray<int>(1, allocator);
            chunkBaseEntityIndices = group.CalculateBaseEntityIndexArrayAsync(allocator, state.Dependency, out var jobHandle);
            lookupJobManager = default;
            lookupJobManager.readWriteJobHandle = group.CalculateEntityCountAsync(entityCount, jobHandle);
        }
    }

    public struct RollbackGroup : IRollbackContainer
    {
        [BurstCompile]
        public struct SaveChunks : IJob
        {
            public uint frameIndex;

            [ReadOnly]
            public NativeArray<int> entityCount;

            [ReadOnly]
            public NativeArray<int> startIndex;

            public NativeParallelHashMap<uint, RollbackChunk> chunks;

            public void Execute()
            {
                RollbackChunk chunk;
                chunk.count = entityCount[0];
                if (chunk.count > 0)
                {
                    chunk.startIndex = startIndex[0];
                    chunks[frameIndex] = chunk;
                }
            }
        }

        private NativeArray<JobHandle> __jobHandles;
        private NativeArray<int> __startIndex;
        private NativeList<Entity> __entities;
        private NativeParallelHashMap<uint, RollbackChunk> __chunks;

#if ENABLE_PROFILER
        private ProfilerMarker __resize;
        private ProfilerMarker __saveEntities;
        private ProfilerMarker __saveChunks;
#endif

        public JobHandle entityJobHandle
        {
            get => __jobHandles[0];

            set => __jobHandles[0] = value;
        }

        public JobHandle chunkJobHandle
        {
            get => __jobHandles[1];

            private set => __jobHandles[1] = value;
        }

        public JobHandle readWriteJobHandle
        {
            get => __jobHandles[2];

            private set => __jobHandles[2] = value;
        }

        public RollbackGroup(int capacity, Allocator allocator)
        {
            BurstUtility.InitializeJob<SaveChunks>();
            BurstUtility.InitializeJob<RollbackResize<Entity>>();

            __jobHandles = new NativeArray<JobHandle>(3, allocator, NativeArrayOptions.ClearMemory);
            __startIndex = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
            __entities = new NativeList<Entity>(allocator);
            __chunks = new NativeParallelHashMap<uint, RollbackChunk>(capacity, allocator);

#if ENABLE_PROFILER
            __resize = new ProfilerMarker("Resize");
            __saveEntities = new ProfilerMarker("Save Entities");
            __saveChunks = new ProfilerMarker("Save Chunks");
#endif
        }

        public int GetEntityCount(uint frameIndex)
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            if (__chunks.TryGetValue(frameIndex, out var chunk))
                return chunk.count;

            return 0;
        }

        public bool GetEntities(uint frameIndex, out NativeSlice<Entity> entities)
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            if (!__chunks.TryGetValue(frameIndex, out var chunk))
            {
                entities = default;

                return false;
            }

            entities = __entities.AsArray().Slice(chunk.startIndex, chunk.count);

            return true;
        }

        public int IndexOf(uint frameIndex, Entity entity)
        {
            if (__chunks.TryGetValue(frameIndex, out var framaeChunk))
            {
                int index;
                for (int i = 0; i < framaeChunk.count; ++i)
                {
                    index = i + framaeChunk.startIndex;
                    if (__entities[i + framaeChunk.startIndex] == entity)
                        return index;
                }
            }

            return -1;
        }

        /*public JobHandle Schedule<TChunk, TEntity>(in TChunk factory, uint frameIndex, EntityTypeHandle entityType, EntityQuery group, JobHandle dependsOn)
            where TChunk : struct, IRollbackRestoreChunk<TEntity>
            where TEntity : struct, IRestore
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            if (!__chunks.TryGetValue(frameIndex, out var chunk))
                return dependsOn;

            RestoreChunk<TChunk, TEntity> restore;
            restore.startIndex = chunk.startIndex;
            restore.entities = __entities;
            restore.entityType = entityType;
            restore.factory = factory;

            var jobHandle = restore.ScheduleParallel(group, dependsOn);

            entityJobHandle = jobHandle;

            return jobHandle;
        }*/

        public JobHandle Schedule<T>(in T instance, uint frameIndex, in JobHandle dependsOn) where T : struct, IRollbackRestore
        {
            RollbackRestoreSingle<T> restore;
            restore.frameIndex = frameIndex;
            restore.entityArray = __entities.AsDeferredJobArray();
            restore.chunks = __chunks;
            restore.instance = instance;

            var jobHandle = restore.Schedule(JobHandle.CombineDependencies(chunkJobHandle, dependsOn));

            chunkJobHandle = jobHandle;

            entityJobHandle = jobHandle;

            return jobHandle;
        }

        public JobHandle ScheduleParallel<T>(in T instance, uint frameIndex, int innerloopBatchCount, in JobHandle dependsOn) where T : struct, IRollbackRestore
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            if (!__chunks.TryGetValue(frameIndex, out var chunk))
                return dependsOn;

            RollbackRestore<T> restore;
            restore.startIndex = chunk.startIndex;
            restore.entityArray = __entities.AsArray();
            restore.instance = instance;

            var jobHandle = restore.Schedule(chunk.count, innerloopBatchCount, dependsOn);

            entityJobHandle = jobHandle;

            return jobHandle;
        }

        public JobHandle ScheduleParallel<T>(
            in T instance,
            uint frameIndex,
            in EntityTypeHandle entityType,
            in EntityQuery group, 
            in RollbackSaveData data) where T : struct, IRollbackSave
        {
            readWriteJobHandle.Complete();

            JobHandle resizeJobHandle;

#if ENABLE_PROFILER
            using (__resize.Auto())
#endif
            {
                RollbackResize<Entity> resize;
                resize.entityCount = data.entityCount;
                resize.startIndex = __startIndex;
                resize.values = __entities;
                resizeJobHandle = resize.Schedule(JobHandle.CombineDependencies(data.lookupJobManager.readOnlyJobHandle, this.entityJobHandle));
            }

            JobHandle entityJobHandle;

#if ENABLE_PROFILER
            using (__saveEntities.Auto())
#endif
            {
                RollbackSave<T> save;
                save.entityType = entityType;
                save.startIndex = __startIndex;
                save.chunkBaseEntityIndices = data.chunkBaseEntityIndices;
                save.entityArray = __entities.AsDeferredJobArray();
                save.instance = instance;

                entityJobHandle = save.ScheduleParallel(group, JobHandle.CombineDependencies(resizeJobHandle, data.lookupJobManager.readWriteJobHandle));// JobHandle.CombineDependencies(resizeJobHandle, dependsOn));
            }

            this.entityJobHandle = entityJobHandle;

            JobHandle chunkJobHandle;

#if ENABLE_PROFILER
            using (__saveChunks.Auto())
#endif
            {
                SaveChunks saveChunks;
                saveChunks.frameIndex = frameIndex;
                saveChunks.entityCount = data.entityCount;
                saveChunks.startIndex = __startIndex;
                saveChunks.chunks = __chunks;

                chunkJobHandle = saveChunks.Schedule(resizeJobHandle);
            }

            this.chunkJobHandle = chunkJobHandle;

            var jobHandle = JobHandle.CombineDependencies(entityJobHandle, chunkJobHandle);

            readWriteJobHandle = jobHandle;

            return jobHandle;

            /*Save<T> save;
            save.frameIndex = frameIndex;
            save.entities = __entities;
            save.frameChunks = __chunks;
            save.chunks = group.CreateArchetypeChunkArrayAsync(Allocator.TempJob, out var jobHandle);
            save.entityType = entityType;
            save.instance = instance;

            jobHandle = save.Schedule(JobHandle.CombineDependencies(dependsOn, jobHandle));

            entityJobHandle = chunkJobHandle = jobHandle;

            return jobHandle;*/
        }

        public JobHandle Schedule<T>(in T instance, uint maxFrameIndex, uint frameIndex, uint frameCount, in JobHandle dependsOn) where T : struct, IRollbackClear
        {
            //__chunkJobHandle.Complete();

            RollbackClear<T> clear;
            clear.maxFrameIndex = maxFrameIndex;
            clear.frameIndex = frameIndex;
            clear.frameCount = frameCount;
            clear.entities = __entities;
            clear.chunks = __chunks;
            clear.instance = instance;

            var jobHandle = clear.Schedule(dependsOn);

            entityJobHandle = chunkJobHandle = jobHandle;

            return jobHandle;
        }

        public void Clear()
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            entityJobHandle.Complete();
            entityJobHandle = default;

            __startIndex[0] = 0;
            __entities.Clear();
            __chunks.Clear();
        }

        public void Dispose()
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            entityJobHandle.Complete();
            entityJobHandle = default;

            __jobHandles.Dispose();
            __startIndex.Dispose();
            __entities.Dispose();
            __chunks.Dispose();
        }
    }

    public struct RollbackComponent<T> : IRollbackContainer where T : unmanaged, IComponentData
    {
        private NativeArray<int> __startIndex;
        private NativeList<T> __values;

        private ComponentTypeHandle<T> __type;
        private ComponentLookup<T> __targets;

#if ENABLE_PROFILER
        private ProfilerMarker __restore;
        private ProfilerMarker __save;
        private ProfilerMarker __clear;
#endif

        public T this[int index]
        {
            get
            {
                return __values[index];
            }
        }

        public RollbackComponent(int capacity, Allocator allocator, ref SystemState state)
        {
            BurstUtility.InitializeJob<RollbackResize<T>>();

            __startIndex = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
            __values = new NativeList<T>(capacity, allocator);

            __type = state.GetComponentTypeHandle<T>(true);
            __targets = state.GetComponentLookup<T>();

#if ENABLE_PROFILER
            __restore = new ProfilerMarker($"{typeof(T).Name} Restore");
            __save = new ProfilerMarker($"{typeof(T).Name} Save");
            __clear = new ProfilerMarker($"{typeof(T).Name} Clear");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentRestoreChunkFunction<T> DelegateRestoreChunk(ref SystemState systemState)
        {
            return new RollbackComponentRestoreChunkFunction<T>(__values.AsArray(), __type.UpdateAsRef(ref systemState));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentRestoreFunction<T> DelegateRestore(ref SystemState systemState)
        {
#if ENABLE_PROFILER
            using (__restore.Auto())
#endif
                return new RollbackComponentRestoreFunction<T>(__values.AsArray(), __targets.UpdateAsRef(ref systemState));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentSaveFunction<T> DelegateSave(ref RollbackSaveData data, ref SystemState systemState)
        {
#if ENABLE_PROFILER
            using (__save.Auto())
#endif
                return RollbackComponentSaveFunction<T>.Delegate(__startIndex, __values, __type.UpdateAsRef(ref systemState), ref data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentClearFunction<T> DelegateClear()
        {
#if ENABLE_PROFILER
            using (__clear.Auto())
#endif
                return new RollbackComponentClearFunction<T>(__values);
        }

        public void Clear()
        {
            __startIndex[0] = 0;

            __values.Clear();
        }

        public void Dispose()
        {
            __startIndex.Dispose();

            __values.Dispose();
        }
    }

    public struct RollbackComponentRestoreChunkFunction<T> where T : unmanaged, IComponentData
    {
        [ReadOnly]
        private NativeArray<T> __values;

        private ComponentTypeHandle<T> __type;

        public T this[int index]
        {
            get
            {
                return __values[index];
            }
        }

        public RollbackComponentRestoreChunkFunction(in NativeArray<T> values, ComponentTypeHandle<T> type)
        {
            __values = values;
            __type = type;
        }

        public RollbackComponentRestoreEntityFunction<T> Invoke(ArchetypeChunk chunk)
        {
            return new RollbackComponentRestoreEntityFunction<T>(__values, chunk.GetNativeArray(ref __type));
        }
    }

    public struct RollbackComponentRestoreEntityFunction<T> where T : struct, IComponentData
    {
        [ReadOnly]
        private NativeArray<T> __values;
        private NativeArray<T> __targets;

        public T this[int index]
        {
            get
            {
                return __values[index];
            }
        }

        public RollbackComponentRestoreEntityFunction(in NativeArray<T> values, NativeArray<T> targets)
        {
            __values = values;
            __targets = targets;
        }

        public void Invoke(int index, int entityIndex)
        {
            __targets[index] = __values[entityIndex];
        }

        public void Invoke(int index, int entityIndex, out T value)
        {
            value = __values[entityIndex];
            __targets[index] = value;
        }
    }

    public struct RollbackComponentRestoreFunction<T> where T : unmanaged, IComponentData
    {
        [ReadOnly]
        private NativeArray<T> __values;

        [NativeDisableParallelForRestriction]
        private ComponentLookup<T> __targets;

        public T this[int index]
        {
            get
            {
                return __values[index];
            }
        }

        public T this[Entity entity]
        {
            get
            {
                return __targets[entity];
            }

            set
            {
                __targets[entity] = value;
            }
        }

        public RollbackComponentRestoreFunction(in NativeArray<T> values, ComponentLookup<T> targets)
        {
            __values = values;
            __targets = targets;
        }

        public bool IsExists(in Entity entity)
        {
            return __targets.HasComponent(entity);
        }

        public void Invoke(int entityIndex, in Entity entity)
        {
            __targets[entity] = __values[entityIndex];
        }
    }

    public struct RollbackComponentSaveFunction<T> where T : unmanaged, IComponentData
    {
        private NativeArray<T> __values;

        [ReadOnly]
        private NativeArray<int> __startIndex;

        [ReadOnly]
        private ComponentTypeHandle<T> __type;

        public static RollbackComponentSaveFunction<T> Delegate(
            NativeArray<int> startIndex,
            NativeList<T> values,
            in ComponentTypeHandle<T> type,
            ref RollbackSaveData data)
        {
            RollbackResize<T> resize;
            resize.entityCount = data.entityCount;
            resize.startIndex = startIndex;
            resize.values = values;
            var jobHandle = resize.Schedule(data.lookupJobManager.readOnlyJobHandle);

            data.lookupJobManager.AddReadOnlyDependency(jobHandle);

            return new RollbackComponentSaveFunction<T>(values.AsDeferredJobArray(), startIndex, type);
        }

        private RollbackComponentSaveFunction(NativeArray<T> values, in NativeArray<int> startIndex, in ComponentTypeHandle<T> type)
        {
            __values = values;
            __startIndex = startIndex;
            __type = type;
        }

        public void Invoke(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var values = chunk.GetNativeArray(ref __type);
            if (useEnabledMask)
            {
                int index = firstEntityIndex + __startIndex[0];
                var iterator = new ChunkEntityEnumerator(true, chunkEnabledMask, chunk.Count);
                while(iterator.NextEntityIndex(out int i))
                    __values[index++] = values[i];
            }
            else
                NativeArray<T>.Copy(values, 0, __values, firstEntityIndex + __startIndex[0], chunk.Count);

            //__values.AddRange(chunk.GetNativeArray(__type));
        }
    }

    public struct RollbackComponentClearFunction<T> where T : unmanaged, IComponentData
    {
        private NativeList<T> __values;

        public RollbackComponentClearFunction(NativeList<T> values)
        {
            __values = values;
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            __values.AsArray().MemMove(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            __values.ResizeUninitialized(count);
        }
    }

    public struct RollbackBuffer<T> : IRollbackContainer where T : unmanaged, IBufferElementData
    {
        private NativeCounter __counter;
        private NativeArray<int> __startIndex;
        private NativeList<T> __values;
        private NativeList<RollbackChunk> __chunks;

        private BufferTypeHandle<T> __type;
        private BufferLookup<T> __targets;

#if ENABLE_PROFILER
        private ProfilerMarker __restore;
        private ProfilerMarker __save;
        private ProfilerMarker __clear;
#endif

        public RollbackBuffer(int capacity, Allocator allocator, ref SystemState systemState)
        {
            BurstUtility.InitializeJob<RollbackResize<RollbackChunk>>();
            BurstUtility.InitializeJob<RollbackSaveBufferResizeValues<T>>();

            __counter = new NativeCounter(allocator);
            __startIndex = new NativeArray<int>(1, allocator, NativeArrayOptions.ClearMemory);
            __values = new NativeList<T>(capacity, allocator);
            __chunks = new NativeList<RollbackChunk>(capacity, allocator);

            __type = systemState.GetBufferTypeHandle<T>(true);
            __targets = systemState.GetBufferLookup<T>();

#if ENABLE_PROFILER
            __restore = new ProfilerMarker($"{typeof(T).Name} Restore");
            __save = new ProfilerMarker($"{typeof(T).Name} Save");
            __clear = new ProfilerMarker($"{typeof(T).Name} Clear");
#endif
        }

        public void CopyTo(int entityIndex, in DynamicBuffer<T> targets)
        {
            //targets.Clear();

            /*if (!__chunks.TryGetValue(entityIndex, out FrameChunk frameChunk))
                return;*/

            var frameChunk = __chunks[entityIndex];
            for (int i = 0; i < frameChunk.count; ++i)
                targets.Add(__values[frameChunk.startIndex + i]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferRestoreChunkFunction<T> DelegateRestoreChunk(ref SystemState systemState)
        {
            return new RollbackBufferRestoreChunkFunction<T>(__values.AsArray(), __chunks.AsArray(), __type.UpdateAsRef(ref systemState));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferRestoreFunction<T> DelegateRestore(ref SystemState systemState)
        {
#if ENABLE_PROFILER
            using (__restore.Auto())
#endif
                return new RollbackBufferRestoreFunction<T>(__values.AsArray(), __chunks.AsArray(), __targets.UpdateAsRef(ref systemState));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferSaveFunction<T> DelegateSave(
            in EntityQuery group,
            ref RollbackSaveData data, 
            ref SystemState systemState)
        {
#if ENABLE_PROFILER
            using (__save.Auto())
#endif
                return RollbackBufferSaveFunction<T>.Deletege(
                    __counter,
                    __startIndex, 
                    __values,
                    __chunks,
                    __type.UpdateAsRef(ref systemState),
                    group,
                    ref data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferClearFunction<T> DelegateClear()
        {
#if ENABLE_PROFILER
            using (__clear.Auto())
#endif
                return new RollbackBufferClearFunction<T>(__counter, __values, __chunks);
        }

        public void Clear()
        {
            __counter.count = 0;
            __startIndex[0] = 0;
            __values.Clear();
            __chunks.Clear();
        }

        public void Dispose()
        {
            __counter.Dispose();
            __startIndex.Dispose();
            __values.Dispose();
            __chunks.Dispose();
        }
    }

    public struct RollbackBufferRestoreChunkFunction<T> where T : unmanaged, IBufferElementData
    {
        [ReadOnly]
        private NativeArray<T> __values;
        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;

        private BufferTypeHandle<T> __type;

        public T this[int index]
        {
            get
            {
                return __values[index];
            }
        }

        public RollbackBufferRestoreChunkFunction(in NativeArray<T> values, in NativeArray<RollbackChunk> chunks, in BufferTypeHandle<T> type)
        {
            __values = values;
            __chunks = chunks;
            __type = type;
        }

        public RollbackBufferRestoreEntityFunction<T> Invoke(in ArchetypeChunk chunk)
        {
            return new RollbackBufferRestoreEntityFunction<T>(__values, __chunks, chunk.GetBufferAccessor(ref __type));
        }
    }

    public struct RollbackBufferRestoreEntityFunction<T> where T : unmanaged, IBufferElementData
    {
        [ReadOnly]
        private NativeArray<T> __values;
        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;
        private BufferAccessor<T> __targets;

        public RollbackBufferRestoreEntityFunction(in NativeArray<T> values, in NativeArray<RollbackChunk> chunks, in BufferAccessor<T> targets)
        {
            __values = values;
            __chunks = chunks;
            __targets = targets;
        }

        public bool IsExists(int index)
        {
            return index < __targets.Length;
        }

        public void CopyTo(int entityIndex, in DynamicBuffer<T> targets)
        {
            var chunk = __chunks[entityIndex];

            if (chunk.count > 0)
                targets.CopyFrom(__values.Slice(chunk.startIndex, chunk.count));
            else
                targets.Clear();
        }

        public void Invoke(int index, int entityIndex)
        {
            CopyTo(entityIndex, __targets[index]);
        }
    }

    public struct RollbackBufferRestoreFunction<T> where T : unmanaged, IBufferElementData
    {
        [ReadOnly]
        private NativeArray<T> __values;

        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;

        [NativeDisableParallelForRestriction]
        private BufferLookup<T> __targets;

        public bool IsExists(Entity entity) => __targets.HasBuffer(entity);

        public RollbackBufferRestoreFunction(in NativeArray<T> values, in NativeArray<RollbackChunk> chunks, BufferLookup<T> targets)
        {
            __values = values;
            __chunks = chunks;
            __targets = targets;
        }

        public void CopyTo(int entityIndex, in DynamicBuffer<T> targets)
        {
            var chunk = __chunks[entityIndex];
            /*if (!__chunks.TryGetValue(entityIndex, out FrameChunk frameChunk))
                return;*/

            for (int i = 0; i < chunk.count; ++i)
                targets.Add(__values[chunk.startIndex + i]);
        }

        public void Invoke(int entityIndex, in Entity entity)
        {
            var targets = __targets[entity];
            targets.Clear();

            CopyTo(entityIndex, targets);
        }
    }

    public struct RollbackBufferSaveFunction<T> where T : unmanaged, IBufferElementData
    {
        [ReadOnly]
        private BufferTypeHandle<T> __type;

        [ReadOnly]
        private NativeArray<int> __startIndex;

        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;

        private NativeArray<T> __values;

        public static RollbackBufferSaveFunction<T> Deletege(
            NativeCounter counter,
            NativeArray<int> startIndex,
            in NativeList<T> values,
            in NativeList<RollbackChunk> chunks,
            in BufferTypeHandle<T> type,
            in EntityQuery group,
            ref RollbackSaveData data)
        {
            RollbackResize<RollbackChunk> resize;
            resize.entityCount = data.entityCount;
            resize.startIndex = startIndex;
            resize.values = chunks;
            var jobHandle = resize.Schedule(data.lookupJobManager.readOnlyJobHandle);

            var frameChunks = chunks.AsDeferredJobArray();

            RollbackSaveBufferCount<T> count;
            count.type = type;
            count.startIndex = startIndex;
            count.chunkBaseEntityIndices = data.chunkBaseEntityIndices;
            count.chunks = frameChunks;
            count.counter = counter;
            jobHandle = count.ScheduleParallel(group, jobHandle);

            RollbackSaveBufferResizeValues<T> resizeValues;
            resizeValues.counter = counter;
            resizeValues.values = values;
            jobHandle = resizeValues.Schedule(jobHandle);

            data.lookupJobManager.AddReadOnlyDependency(jobHandle);

            return new RollbackBufferSaveFunction<T>(type, startIndex, frameChunks, values.AsDeferredJobArray());
        }

        private RollbackBufferSaveFunction(
            in BufferTypeHandle<T> type,
            in NativeArray<int> startIndex,
            in NativeArray<RollbackChunk> chunks,
            in NativeArray<T> values)
        {
            __type = type;
            __chunks = chunks;
            __startIndex = startIndex;
            __values = values;
        }

        public void Invoke(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var bufferAccessor = chunk.GetBufferAccessor(ref __type);

            RollbackChunk frameChunk;
            int index = firstEntityIndex + __startIndex[0];
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
            {
                frameChunk = __chunks[index++];
                if (frameChunk.count > 0)
                    NativeArray<T>.Copy(bufferAccessor[i].AsNativeArray(), 0, __values, frameChunk.startIndex, frameChunk.count);
            }
        }
    }

    /*public struct BufferSaveFunction<T> where T : struct, IBufferElementData
    {
        private NativeList<T> __values;
        private NativeList<FrameChunk> __chunks;

        [ReadOnly]
        public BufferTypeHandle<T> __type;

        public BufferSaveFunction(in NativeList<T> values, in NativeList<FrameChunk> chunks, in BufferTypeHandle<T> type)
        {
            __values = values;
            __chunks = chunks;
            __type = type;
        }

        public void Invoke(in ArchetypeChunk chunk)
        {
            int capacity = __chunks.Length + chunk.Count;
            if(capacity > __chunks.Capacity)
                __chunks.Capacity = capacity;

            BufferAccessor<T> bufferAccessor = chunk.GetBufferAccessor(__type);

            int count = bufferAccessor.Length;
            FrameChunk frameChunk;
            DynamicBuffer<T> buffer;
            frameChunk.startIndex = __values.Length;
            for (int i = 0; i < count; ++i)
            {
                buffer = bufferAccessor[i];
                frameChunk.count = buffer.Length;

                __chunks.Add(frameChunk);

                if (frameChunk.count > 0)
                {
                    __values.AddRange(buffer.AsNativeArray());

                    frameChunk.startIndex += frameChunk.count;
                }
            }
        }
    }*/

    public struct RollbackBufferClearFunction<T> where T : unmanaged, IBufferElementData
    {
        private NativeCounter __counter;
        private NativeList<T> __values;
        private NativeList<RollbackChunk> __chunks;

        private RollbackChunk __chunk;

        public RollbackBufferClearFunction(NativeCounter counter, in NativeList<T> values, in NativeList<RollbackChunk> chunks)
        {
            __counter = counter;
            __values = values;
            __chunks = chunks;

            __chunk = default;
        }

        public void Remove(int startEntityIndex, int entityCount)
        {
            int length = __values.Length;
            RollbackChunk chunk;

            __chunk.startIndex = length;
            __chunk.count = 0;
            for (int i = 0; i < entityCount; ++i)
            {
                chunk = __chunks[startEntityIndex + i];
                if (chunk.count > 0)
                {
                    __chunk.startIndex = math.min(__chunk.startIndex, chunk.startIndex);
                    __chunk.count += chunk.count;
                }
            }

            __counter.count -= __chunk.count;

            UnityEngine.Assertions.Assert.IsFalse(__counter.count < 0);
        }

        public void Move(int fromEntityIndex, int toEntityIndex, int entityCount)
        {
#if DEBUG
            if (__chunk.count != 0)
                UnityEngine.Assertions.Assert.AreEqual(0, __chunk.startIndex);
#endif

            //UnityEngine.Assertions.Assert.IsFalse(__chunks.ContainsKey(fromEntityIndex - 1));
            //UnityEngine.Assertions.Assert.AreEqual(__chunks.ContainsKey(fromEntityIndex + entityCount));
            UnityEngine.Assertions.Assert.AreEqual(fromEntityIndex + entityCount, __chunks.Length);

            UnityEngine.Assertions.Assert.IsTrue(fromEntityIndex > toEntityIndex);

            RollbackChunk frameChunk;
            for (int i = 0; i < entityCount; ++i)
            {
                frameChunk = __chunks[fromEntityIndex + i];
                if (frameChunk.startIndex >= 0)
                {
                    if (frameChunk.startIndex < __chunk.count)
                        UnityEngine.Assertions.Assert.IsFalse(frameChunk.startIndex < __chunk.count);

                    frameChunk.startIndex -= __chunk.count;
                }

                __chunks[toEntityIndex + i] = frameChunk;
            }
        }

        public void Resize(int count)
        {
            int offset = __chunk.startIndex + __chunk.count, length = __values.Length;
            if (offset < length)
            {
                UnityEngine.Assertions.Assert.AreEqual(0, __chunk.startIndex);

                __values.AsArray().MemMove(offset, __chunk.startIndex, length - offset);

                __chunk.startIndex = length - __chunk.count;
            }
#if DEBUG
            else
                UnityEngine.Assertions.Assert.AreEqual(length, offset);

            /*FrameChunk temp;
            int num = 0;
            for (int i = 0; i < count; ++i)
            {
                temp = __chunks[i];
                if (num == 0)
                    num = temp.startIndex;
                else if(temp.startIndex != num)
                    UnityEngine.Assertions.Assert.AreEqual(temp.startIndex, num);

                num += temp.count;
            }

            if(__chunk.startIndex != num)
                UnityEngine.Assertions.Assert.AreEqual(__chunk.startIndex, num);*/
#endif

            UnityEngine.Assertions.Assert.IsTrue(__chunk.startIndex <= __values.Length);

            __values.ResizeUninitialized(__chunk.startIndex);

            UnityEngine.Assertions.Assert.IsTrue(count <= __chunks.Length);

            __chunks.ResizeUninitialized(count);
        }
    }

    public struct RollbackManager : IRollbackContainer
    {
        [BurstCompile]
        private struct ClearHashMap : IJob
        {
            public int capacity;

            public NativeParallelHashMap<Entity, int> value;

            public void Execute()
            {
                value.Capacity = math.max(value.Capacity, capacity);
                value.Clear();
            }
        }

        [BurstCompile]
        private struct BuildEntityIndices : IJobParallelFor
        {
            [ReadOnly]
            public NativeSlice<Entity> entities;
            public NativeParallelHashMap<Entity, int>.ParallelWriter results;

            public void Execute(int index)
            {
                results.TryAdd(entities[index], index);
            }
        }

        [BurstCompile]
        private struct RestoreChunk : IJobChunk, IEntityCommandProducerJob
        {
#if UNITY_EDITOR
            public bool isAddOrRemove;
            public uint frameIndex;
#endif

            public ComponentType componentType;

            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public NativeParallelHashMap<Entity, int> entityIndices;

            public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                EntityCommandStructChange command;
                command.componentType = componentType;

                var entityArray = chunk.GetNativeArray(entityType);
                Entity entity;
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    entity = entityArray[i];
                    if (!entityIndices.ContainsKey(entity))
                    {
                        command.entity = entity;

#if GAME_DEBUG_DISABLE
                        if (isAddOrRemove && componentType.TypeIndex == TypeManager.GetTypeIndex<Disabled>())
                            UnityEngine.Debug.Log($"Chunk Disable {entity.Index} In {frameIndex}");
#endif

                        entityManager.Enqueue(command);
                    }
                    /*for (j = 0; j < numEntities; ++j)
                    {
                        if (entities[j] == entity)
                            break;
                    }

                    if (j == numEntities)
                        entityManager.Enqueue(entity);*/
                }
            }
        }

        public readonly int InnerloopBatchCount;

        /*public readonly uint MaxFrameCount;

        private EntityQuery __flagGroup;
        private EntityQuery __restoreFrameGroup;*/
        private EntityQuery __frameGroup;

        private RollbackGroup __group;

        private UnsafeList<uint> __frameIndices;
        //private UnsafeListEx<JobHandle> __saveJobHandles;

        private NativeParallelHashMap<Entity, int> __entityIndices;

#if ENABLE_PROFILER
        private ProfilerMarker __restore;
        private ProfilerMarker __save;
        private ProfilerMarker __clear;
        private ProfilerMarker __scheduleRestore;
        private ProfilerMarker __scheduleSave;
        private ProfilerMarker __scheduleClear;
        private ProfilerMarker __addOrRemoveComponentIfNotSaved;
        private ProfilerMarker __addComponentIfNotSaved;
        private ProfilerMarker __removeComponentIfNotSaved;
#endif

        /*public JobHandle saveJobHandle
        {
            get
            {
                var jobHandle = JobHandle.CombineDependencies(__saveJobHandles.AsArray());

                __saveJobHandles.Clear();

                return jobHandle;
            }
        }*/

        public uint maxSaveFrameIndex
        {
            get
            {
                return __frameIndices[0];
            }

            private set
            {
                __frameIndices[0] = value;
            }
        }

        public uint minSaveFrameIndex
        {
            get
            {
                return __frameIndices[1];
            }

            private set
            {
                __frameIndices[1] = value;
            }
        }

        public RollbackManager(ref SystemState systemState, Allocator allocator, int innerloopBatchCount = 1/*, uint maxFrameCount = 256*/)
        {
            BurstUtility.InitializeJob<ClearHashMap>();
            BurstUtility.InitializeJobParallelFor<BuildEntityIndices>();

            systemState.SetAlwaysUpdateSystem(true);

            InnerloopBatchCount = innerloopBatchCount;
            /*MaxFrameCount = maxFrameCount;

            __flagGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<RollbackFlag>());
            __restoreFrameGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<RollbackRestoreFrame>());*/
            __frameGroup = systemState.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<RollbackFrame>(),
                        ComponentType.ReadOnly<RollbackFrameRestore>(),
                        ComponentType.ReadOnly<RollbackFrameSave>(),
                        ComponentType.ReadOnly<RollbackFrameClear>()
                    }, 
                    Options = EntityQueryOptions.IncludeSystems
                });

            __group = new RollbackGroup(1, allocator);

            __frameIndices = new UnsafeList<uint>(2, allocator, NativeArrayOptions.UninitializedMemory);
            __frameIndices.Resize(2, NativeArrayOptions.UninitializedMemory);

            //__saveJobHandles = new UnsafeListEx<JobHandle>(allocator);

            __entityIndices = new NativeParallelHashMap<Entity, int>(1, allocator);

#if ENABLE_PROFILER
            __restore = new ProfilerMarker("Restore Ex");
            __save = new ProfilerMarker("Save Ex");
            __clear = new ProfilerMarker("Clear Ex");
            __scheduleRestore = new ProfilerMarker("Schedule Restore");
            __scheduleSave = new ProfilerMarker("Schedule Save");
            __scheduleClear = new ProfilerMarker("Schedule Clear");
            __addOrRemoveComponentIfNotSaved = new ProfilerMarker("Add Or Remove Component If Not Saved");
            __addComponentIfNotSaved = new ProfilerMarker("Add Component If Not Saved");
            __removeComponentIfNotSaved = new ProfilerMarker("Remove Component If Not Saved");
#endif

            maxSaveFrameIndex = 0;
            minSaveFrameIndex = 1;
        }

        public void Dispose()
        {
            __group.Dispose();

            __frameIndices.Dispose();

            //__saveJobHandles.Dispose();

            __entityIndices.Dispose();
        }

        public void Clear()
        {
            maxSaveFrameIndex = 0;
            minSaveFrameIndex = 1;

            __group.Clear();
        }

        public int GetEntityCount(uint frameIndex) => __group.GetEntityCount(frameIndex);

        public int IndexOf(uint frameIndex, Entity entity) => __group.IndexOf(frameIndex, entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentRestoreFunction<T> DelegateRestore<T>(in RollbackComponent<T> instance, ref SystemState systemState) where T : unmanaged, IComponentData
        {
            return instance.DelegateRestore(ref systemState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentSaveFunction<T> DelegateSave<T>(
            in RollbackComponent<T> instance, 
            ref RollbackSaveData data, 
            ref SystemState systemState) where T : unmanaged, IComponentData
        {
            //var result = instance.DelegateSave(entityCount, ref systemState, ref jobHandle);

            //__saveJobHandles.Add(jobHandle);

            return instance.DelegateSave(ref data, ref systemState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentClearFunction<T> DelegateClear<T>(in RollbackComponent<T> instance) where T : unmanaged, IComponentData
        {
            return instance.DelegateClear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferRestoreFunction<T> DelegateRestore<T>(in RollbackBuffer<T> instance, ref SystemState systemState) where T : unmanaged, IBufferElementData
        {
            return instance.DelegateRestore(ref systemState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferSaveFunction<T> DelegateSave<T>(
            in RollbackBuffer<T> instance,
            in EntityQuery group,
            ref RollbackSaveData data,
            ref SystemState systemState) where T : unmanaged, IBufferElementData
        {
            return instance.DelegateSave(
                group,
                ref data, 
                ref systemState);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferClearFunction<U> DelegateClear<U>(in RollbackBuffer<U> instance) where U : unmanaged, IBufferElementData
        {
            return instance.DelegateClear();
        }

        public bool AddOrRemoveComponentIfNotSaved(
#if UNITY_EDITOR
            bool isAddOrRemove, 
#endif
            uint frameIndex,
            in ComponentType componentType,
            in EntityQuery group,
            in EntityCommandPool<EntityCommandStructChange> commander,
            ref SystemState systemState)
        {
#if ENABLE_PROFILER
            using (__addOrRemoveComponentIfNotSaved.Auto())
#endif

            {
                if (__group.GetEntities(frameIndex, out var entities))
                {
                    NativeParallelHashMap<Entity, int> entityIndices = __entityIndices;

                    var entityIndicesParallelWriter = entityIndices.AsParallelWriter();

                    var jobHandle = systemState.Dependency;

                    ClearHashMap clearHashMap;
                    clearHashMap.capacity = entities.Length;
                    clearHashMap.value = __entityIndices;
                    jobHandle = clearHashMap.Schedule(jobHandle);

                    BuildEntityIndices buildEntityIndices;
                    buildEntityIndices.entities = entities;
                    buildEntityIndices.results = entityIndicesParallelWriter;
                    jobHandle = buildEntityIndices.Schedule(entities.Length, InnerloopBatchCount, jobHandle);

                    __group.entityJobHandle = jobHandle;

                    var entityManager = commander.Create();

                    RestoreChunk restoreChunk;
#if UNITY_EDITOR
                    restoreChunk.isAddOrRemove = isAddOrRemove;
                    restoreChunk.frameIndex = frameIndex;
#endif
                    restoreChunk.componentType = componentType;
                    restoreChunk.entityType = systemState.GetEntityTypeHandle();
                    restoreChunk.entityIndices = __entityIndices;
                    restoreChunk.entityManager = entityManager.parallelWriter;

                    jobHandle = restoreChunk.ScheduleParallel(group, jobHandle);

                    entityManager.AddJobHandleForProducer<RestoreChunk>(jobHandle);

                    systemState.Dependency = jobHandle;

                    return true;
                }

                return false;
            }
        }

        public void AddComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityCommandPool<EntityCommandStructChange> addComponentCommander,
            ref SystemState systemState) where T : struct, IComponentData
        {
#if ENABLE_PROFILER
            using (__addComponentIfNotSaved.Auto())
#endif
            {
                if (frameIndex < minSaveFrameIndex)
                    UnityEngine.Debug.LogError($"Frame Index {frameIndex} Less Than Range {minSaveFrameIndex}");
                else if (!AddOrRemoveComponentIfNotSaved(
#if UNITY_EDITOR
                    true, 
#endif
                    frameIndex,
                    ComponentType.ReadWrite<T>(),
                    group,
                    addComponentCommander,
                    ref systemState))
                {
#if GAME_DEBUG_DISABLE
                    if(TypeManager.GetTypeIndex<T>() == TypeManager.GetTypeIndex<Disabled>() && !group.IsEmpty)
                    {
                        group.CompleteDependency();

                        var entityArray = group.ToEntityArrayBurstCompatible(systemState.GetEntityTypeHandle(), Allocator.TempJob);
                        foreach(var entity in entityArray)
                        {
                            UnityEngine.Debug.Log($"Disable {entity.Index} In {frameIndex}");
                        }
                    }
#endif
                    //jobHandle.Complete();
                    //systemState.CompleteDependency();

                    //jobHandle = endFrameBarrier.AddComponent<T>(group, inputDeps);
                    systemState.EntityManager.AddComponent<T>(group);
                }
            }
        }

        public void RemoveComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityCommandPool<EntityCommandStructChange> removeComponentCommander,
            ref SystemState systemState) where T : struct, IComponentData
        {
#if ENABLE_PROFILER
            using (__removeComponentIfNotSaved.Auto())
#endif
            {
                if (frameIndex < minSaveFrameIndex)
                    UnityEngine.Debug.LogError($"Frame Index {frameIndex} Less Than Range {minSaveFrameIndex}");
                else if (!AddOrRemoveComponentIfNotSaved(
#if UNITY_EDITOR
                    false,
#endif
                    frameIndex,
                    ComponentType.ReadWrite<T>(),
                    group,
                    removeComponentCommander,
                    ref systemState))
                {
                    //jobHandle.Complete();
                    //systemState.CompleteDependency();

                    //jobHandle = endFrameBarrier.RemoveComponent<T>(group, inputDeps);
                    systemState.EntityManager.RemoveComponent<T>(group);
                }
            }
        }

        public JobHandle ScheduleParallel<T>(
            in T instance,
            uint frameIndex,
            in JobHandle dependsOn) where T : struct, IRollbackRestore
        {
#if ENABLE_PROFILER
            using (__scheduleRestore.Auto())
#endif
                return __group.ScheduleParallel(
                instance,
                frameIndex,
                InnerloopBatchCount,
                dependsOn);
        }

        public JobHandle Schedule<T>(
            in T instance,
            uint frameIndex,
            in JobHandle dependsOn) where T : struct, IRollbackRestore
        {
#if ENABLE_PROFILER
            using (__scheduleRestore.Auto())
#endif
                return __group.Schedule(
                instance,
                frameIndex,
                dependsOn);
        }

        public JobHandle ScheduleParallel<T>(
            in T instance,
            uint frameIndex,
            in EntityTypeHandle entityType,
            in EntityQuery group,
            in RollbackSaveData data) where T : struct, IRollbackSave
        {
#if ENABLE_PROFILER
            using (__scheduleSave.Auto())
#endif
                return __group.ScheduleParallel(
                    instance,
                    frameIndex,
                    entityType,
                    group,
                    data);
        }

        public JobHandle Schedule<T>(
            in T instance,
            uint maxFrameIndex,
            uint frameIndex,
            uint frameCount,
            in JobHandle dependsOn) where T : struct, IRollbackClear
        {
#if ENABLE_PROFILER
            using (__scheduleClear.Auto())
#endif
                return __group.Schedule(
                    instance,
                    maxFrameIndex,
                    frameIndex,
                    frameCount,
                    dependsOn);
        }

        public void Update<U>(ref SystemState systemState, ref U core) where U : IRollbackCore
        {
            __Update(
                __frameGroup.GetSingleton<RollbackFrame>().index,
                __frameGroup.GetSingleton<RollbackFrameRestore>().index,
                __frameGroup.GetSingleton<RollbackFrameSave>().minIndex,
                __frameGroup.GetSingleton<RollbackFrameClear>().maxIndex,
                ref systemState, 
                ref core);
        }

        private void __Update<U>(/*bool isClear, */
            uint frameIndex, 
            uint frameIndexToRestore, 
            uint minFrameIndexToSave, 
            uint maxFrameIndexToClear, 
            ref SystemState systemState, 
            ref U core) where U : IRollbackCore
        {
            uint maxSaveFrameIndex = this.maxSaveFrameIndex;
            if (maxSaveFrameIndex < frameIndexToRestore || frameIndexToRestore > frameIndex)
            {
                if (frameIndexToRestore <= frameIndex)
                {
                    UnityEngine.Debug.LogError("Save when restore!");

                    uint minSaveFrameIndex = this.minSaveFrameIndex;
                    if (maxSaveFrameIndex >= minSaveFrameIndex)
#if ENABLE_PROFILER
                        using (__clear.Auto())
#endif
                            core.ScheduleClear(maxSaveFrameIndex, minSaveFrameIndex, maxSaveFrameIndex - minSaveFrameIndex, ref systemState);

                    this.maxSaveFrameIndex = frameIndex - 1;
                    this.minSaveFrameIndex = frameIndex;
                }
                else if(frameIndex <= maxSaveFrameIndex)
                    UnityEngine.Debug.LogError("WTF?");

#if ENABLE_PROFILER
                using (__save.Auto())
#endif
                    __Save(/*isClear, */
                        frameIndex, 
                        minFrameIndexToSave, 
                        maxFrameIndexToClear,
                        ref systemState, 
                        ref core);

                return;
            }

#if ENABLE_PROFILER
            using (__restore.Auto())
#endif
                core.ScheduleRestore(frameIndexToRestore, ref systemState);

            if (maxSaveFrameIndex > frameIndexToRestore)
#if ENABLE_PROFILER
                using (__clear.Auto())
#endif
                    core.ScheduleClear(maxSaveFrameIndex, frameIndexToRestore + 1, maxSaveFrameIndex - frameIndexToRestore, ref systemState);

            this.maxSaveFrameIndex = frameIndexToRestore;
        }

        private void __Save<U>(/*bool isClear, */
            uint frameIndex,
            uint minFrameIndexToSave,
            uint maxFrameIndexToClear,
            ref SystemState systemState, 
            ref U core) where U : IRollbackCore
        {
            uint maxSaveFrameIndex = this.maxSaveFrameIndex;
            UnityEngine.Assertions.Assert.IsTrue(frameIndex > maxSaveFrameIndex, $"{frameIndex} : {maxSaveFrameIndex}");

            if (/*isClear && */frameIndex > minFrameIndexToSave/*minSaveFrameIndex + MaxFrameCount*/)
            {
                //uint minFrameIndex = frameIndex - (MaxFrameCount >> 1);
                uint minSaveFrameIndex = this.minSaveFrameIndex, minFrameIndex = math.min(frameIndex - 1, maxFrameIndexToClear);
                if (minFrameIndex > maxSaveFrameIndex)
                {
                    if (maxSaveFrameIndex >= minSaveFrameIndex)
#if ENABLE_PROFILER
                        using (__clear.Auto())
#endif
                            core.ScheduleClear(maxSaveFrameIndex, minSaveFrameIndex, maxSaveFrameIndex - minSaveFrameIndex, ref systemState);

                    maxSaveFrameIndex = frameIndex - 1;
                    this.minSaveFrameIndex = frameIndex;
                }
                else if (minSaveFrameIndex < minFrameIndex)
                {
#if ENABLE_PROFILER
                    using (__clear.Auto())
#endif
                        core.ScheduleClear(maxSaveFrameIndex, minSaveFrameIndex, minFrameIndex - minSaveFrameIndex, ref systemState);

                    this.minSaveFrameIndex = minFrameIndex;
                }
            }

            var entityType = systemState.GetEntityTypeHandle();

#if ENABLE_PROFILER
            using (__save.Auto())
#endif
                for (uint i = maxSaveFrameIndex + 1; i <= frameIndex; ++i)
                    core.ScheduleSave(i, entityType, ref systemState);

            this.maxSaveFrameIndex = frameIndex;
        }
    }

    public struct RollbackManager<TRestore, TSave, TClear> : IRollbackContainer 
        where TRestore : struct, IRollbackRestore
        where TSave : struct, IRollbackSave
        where TClear : struct, IRollbackClear
    {
        private RollbackManager __imp;

        public RollbackManager(ref SystemState systemState, Allocator allocator, int innerloopBatchCount = 1/*, uint maxFrameCount = 256*/)
        {
            __imp = new RollbackManager(ref systemState, allocator, innerloopBatchCount/*, maxFrameCount*/);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => __imp.Dispose();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => __imp.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetEntityCount(uint frameIndex) => __imp.GetEntityCount(frameIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(uint frameIndex, Entity entity) => __imp.IndexOf(frameIndex, entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentRestoreFunction<T> DelegateRestore<T>(in RollbackComponent<T> instance, ref SystemState systemState) 
            where T : unmanaged, IComponentData => __imp.DelegateRestore(instance, ref systemState);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentSaveFunction<T> DelegateSave<T>(in RollbackComponent<T> instance, ref RollbackSaveData data, ref SystemState systemState)
            where T : unmanaged, IComponentData => __imp.DelegateSave(instance, ref data, ref systemState);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentClearFunction<T> DelegateClear<T>(in RollbackComponent<T> instance) 
            where T : unmanaged, IComponentData => __imp.DelegateClear(instance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferRestoreFunction<T> DelegateRestore<T>(in RollbackBuffer<T> instance, ref SystemState systemState)
            where T : unmanaged, IBufferElementData => __imp.DelegateRestore(instance, ref systemState);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferSaveFunction<T> DelegateSave<T>(
            in RollbackBuffer<T> instance,
            in EntityQuery group,
            ref RollbackSaveData data, 
            ref SystemState systemState)
            where T : unmanaged, IBufferElementData => __imp.DelegateSave(instance, group, ref data, ref systemState);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackBufferClearFunction<U> DelegateClear<U>(in RollbackBuffer<U> instance)
            where U : unmanaged, IBufferElementData => __imp.DelegateClear(instance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityCommandPool<EntityCommandStructChange> addComponentCommander,
            ref SystemState systemState)
            where T : struct, IComponentData => __imp.AddComponentIfNotSaved<T>(frameIndex, group, addComponentCommander, ref systemState);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityCommandPool<EntityCommandStructChange> removeComponentCommander,
            ref SystemState systemState)
            where T : struct, IComponentData => __imp.RemoveComponentIfNotSaved<T>(frameIndex, group, removeComponentCommander, ref systemState);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle ScheduleParallel(
            in TRestore instance,
            uint frameIndex,
            in JobHandle dependsOn) => __imp.ScheduleParallel(instance, frameIndex, dependsOn);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle Schedule(
            in TRestore instance,
            uint frameIndex,
            in JobHandle dependsOn) => __imp.Schedule(instance, frameIndex, dependsOn);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle ScheduleParallel(
            in TSave instance, 
            uint frameIndex,
            in EntityTypeHandle entityType,
            in EntityQuery group,
            in RollbackSaveData data) => __imp.ScheduleParallel(instance, frameIndex, entityType, group, data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle Schedule(
            in TClear instance,
            uint maxFrameIndex,
            uint frameIndex,
            uint frameCount,
            in JobHandle dependsOn) => __imp.Schedule(instance, maxFrameIndex, frameIndex, frameCount, dependsOn);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update<T>(ref SystemState systemState, ref T core)
            where T : IRollbackCore => __imp.Update(ref systemState, ref core);
    }

    public class RollbackContainerManager
    {
        private HashSet<IRollbackContainer> __containers;

        public RollbackContainerManager()
        {
            __containers = new HashSet<IRollbackContainer>();
        }

        public RollbackManager<TRestore, TSave, TClear> CreateManager<TRestore, TSave, TClear>(ref SystemState systemState, int innerloopBatchCount = 4/*, uint maxFrameCount = 256*/)
            where TRestore : struct, IRollbackRestore
            where TSave : struct, IRollbackSave
            where TClear : struct, IRollbackClear
        {
            var container = new RollbackManager<TRestore, TSave, TClear>(ref systemState, Allocator.Persistent, innerloopBatchCount/*, maxFrameCount*/);

            __containers.Add(container);

            return container;
        }

        public RollbackManager CreateManager(ref SystemState systemState, int innerloopBatchCount = 1/*, uint maxFrameCount = 256*/)
        {
            var container = new RollbackManager(ref systemState, Allocator.Persistent, innerloopBatchCount/*, maxFrameCount*/);

            __containers.Add(container);

            return container;
        }

        public RollbackGroup CreateGroup()
        {
            var container = new RollbackGroup(1, Allocator.Persistent);

            __containers.Add(container);

            return container;
        }

        public RollbackComponent<T> CreateComponent<T>(ref SystemState systemState) where T : unmanaged, IComponentData
        {
            var container = new RollbackComponent<T>(1, Allocator.Persistent, ref systemState);

            __containers.Add(container);

            return container;
        }

        public RollbackBuffer<T> CreateBuffer<T>(ref SystemState systemState) where T : unmanaged, IBufferElementData
        {
            var container = new RollbackBuffer<T>(1, Allocator.Persistent, ref systemState);

            __containers.Add(container);

            return container;
        }

        public bool Register<T>(T container) where T : IRollbackContainer
        {
            return __containers.Add(container);
        }

        public bool Unregister<T>(T container) where T : IRollbackContainer
        {
            return __containers.Remove(container);
        }

        public void Clear()
        {
            if (__containers == null)
                return;

            foreach (var container in __containers)
                container.Clear();
        }

        public void Dispose()
        {
            if (__containers == null)
                return;

            foreach (var container in __containers)
                container.Dispose();

            __containers = null;
        }
    }

    public static partial class RollbackUtility
    {
        public static bool InvokeDiff<T>(this RollbackComponentRestoreFunction<T> function, int entityIndex, Entity entity, out T value) where T : unmanaged, IEquatable<T>, IComponentData
        {
            value = function[entityIndex]; ;
            if (value.Equals(function[entity]))
                return false;

            function[entity] = value;

            return true;
        }

        public static bool InvokeDiff<T>(this RollbackComponentRestoreFunction<T> function, int entityIndex, Entity entity) where T : unmanaged, IEquatable<T>, IComponentData
        {
            return InvokeDiff(function, entityIndex, entity, out var value);
        }

    }
}
