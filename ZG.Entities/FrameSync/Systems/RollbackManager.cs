using System;
using System.Runtime.InteropServices;
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
using static ZG.RollbackUtility;

[assembly: RegisterGenericJobType(typeof(RollbackResize<Entity>))]
[assembly: RegisterGenericJobType(typeof(RollbackResize<RollbackChunk>))]

namespace ZG
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void RollbackContainerDelegate(in UnsafeBlock value);

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
            using(var builder = new EntityQueryBuilder(Allocator.Temp))
                return builder
                        .WithAll<RollbackFrame>()
                        .WithOptions(EntityQueryOptions.IncludeSystems)
                        .Build(ref state);
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
        private struct LoadChunk : IJob
        {
            public uint frameIndex;

            [ReadOnly]
            public NativeHashMap<uint, RollbackChunk> chunks;

            public NativeArray<int> countAndStartIndex;

            public void Execute()
            {
                if (chunks.TryGetValue(frameIndex, out var chunk))
                {
                    countAndStartIndex[0] = chunk.count;
                    countAndStartIndex[1] = chunk.startIndex;
                }
                else
                {
                    countAndStartIndex[0] = 0;
                    countAndStartIndex[1] = -1;
                }
            }
        }

        [BurstCompile]
        private struct SaveChunks : IJob
        {
            public uint frameIndex;

            [ReadOnly]
            public NativeArray<int> entityCount;

            [ReadOnly]
            public NativeArray<int> countAndStartIndex;

            public NativeHashMap<uint, RollbackChunk> chunks;

            public void Execute()
            {
                RollbackChunk chunk;
                chunk.count = entityCount[0];
                if (chunk.count > 0)
                {
                    chunk.startIndex = countAndStartIndex[1];
                    chunks[frameIndex] = chunk;
                }
            }
        }

        private NativeArray<JobHandle> __jobHandles;
        private NativeArray<int> __countAndStartIndex;
        private NativeList<Entity> __entities;
        private NativeHashMap<uint, RollbackChunk> __chunks;

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

        public NativeArray<int> countAndStartIndex => __countAndStartIndex;

        public NativeArray<Entity> entities => __entities.AsDeferredJobArray();

        public RollbackGroup(int capacity, Allocator allocator)
        {
            BurstUtility.InitializeJob<SaveChunks>();
            BurstUtility.InitializeJob<RollbackResize<Entity>>();

            __jobHandles = new NativeArray<JobHandle>(3, allocator, NativeArrayOptions.ClearMemory);
            __countAndStartIndex = new NativeArray<int>(2, allocator, NativeArrayOptions.ClearMemory);
            __entities = new NativeList<Entity>(allocator);
            __chunks = new NativeHashMap<uint, RollbackChunk>(capacity, allocator);

#if ENABLE_PROFILER
            __resize = new ProfilerMarker("Resize");
            __saveEntities = new ProfilerMarker("Save Entities");
            __saveChunks = new ProfilerMarker("Save Chunks");
#endif
        }

        /*public int GetEntityCount(uint frameIndex)
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            if (__chunks.TryGetValue(frameIndex, out var chunk))
                return chunk.count;

            return 0;
        }*/

        public JobHandle GetChunk(uint frameIndex, in JobHandle dependsOn)
        {
            LoadChunk loadChunk;
            loadChunk.frameIndex = frameIndex;
            loadChunk.countAndStartIndex = __countAndStartIndex;
            loadChunk.chunks = __chunks;

            return loadChunk.ScheduleByRef(JobHandle.CombineDependencies(dependsOn, chunkJobHandle));
        }

        /*public bool GetEntities(uint frameIndex, out NativeSlice<Entity> entities)
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
        }*/

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

            var jobHandle = restore.ScheduleByRef(JobHandle.CombineDependencies(chunkJobHandle, dependsOn));

            chunkJobHandle = jobHandle;

            entityJobHandle = jobHandle;

            return jobHandle;
        }

        public JobHandle ScheduleParallel<T>(
            in T instance, 
            uint frameIndex, 
            int innerloopBatchCount, 
            in JobHandle dependsOn, 
            bool needChunk) where T : struct, IRollbackRestore
        {
            /*chunkJobHandle.Complete();
            chunkJobHandle = default;

            if (!__chunks.TryGetValue(frameIndex, out var chunk))
                return dependsOn;*/

            var jobHandle = needChunk ? GetChunk(frameIndex, dependsOn) : dependsOn;

            RollbackRestore<T> restore;
            restore.countAndStartIndex = __countAndStartIndex;
            restore.entityArray = __entities.AsArray();
            restore.instance = instance;

            jobHandle = restore.ScheduleUnsafeIndex0ByRef(__countAndStartIndex, innerloopBatchCount, jobHandle);

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
            //readWriteJobHandle.Complete();

            JobHandle resizeJobHandle;

#if ENABLE_PROFILER
            using (__resize.Auto())
#endif
            {
                RollbackResize<Entity> resize;
                resize.entityCount = data.entityCount;
                resize.countAndStartIndex = __countAndStartIndex;
                resize.values = __entities;
                resizeJobHandle = resize.ScheduleByRef(JobHandle.CombineDependencies(data.lookupJobManager.readWriteJobHandle, readWriteJobHandle, this.entityJobHandle));
            }

            JobHandle entityJobHandle;

#if ENABLE_PROFILER
            using (__saveEntities.Auto())
#endif
            {
                RollbackSave<T> save;
                save.entityType = entityType;
                save.countAndStartIndex = __countAndStartIndex;
                save.chunkBaseEntityIndices = data.chunkBaseEntityIndices;
                save.entityArray = __entities.AsDeferredJobArray();
                save.instance = instance;

                entityJobHandle = save.ScheduleParallelByRef(group, resizeJobHandle);// JobHandle.CombineDependencies(resizeJobHandle, dependsOn));
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
                saveChunks.countAndStartIndex = __countAndStartIndex;
                saveChunks.chunks = __chunks;

                chunkJobHandle = saveChunks.ScheduleByRef(resizeJobHandle);
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

            var jobHandle = clear.ScheduleByRef(dependsOn);

            entityJobHandle = chunkJobHandle = jobHandle;

            return jobHandle;
        }

        public void Clear()
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            entityJobHandle.Complete();
            entityJobHandle = default;

            __countAndStartIndex[0] = 0;
            __countAndStartIndex[1] = -1;
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
            __countAndStartIndex.Dispose();
            __entities.Dispose();
            __chunks.Dispose();
        }
    }

    public struct RollbackComponentContainer : IRollbackContainer
    {
        internal NativeArray<int> _countAndStartIndex;
        internal NativeList<byte> _values;

        public void Clear()
        {
            _countAndStartIndex[0] = 0;
            _countAndStartIndex[1] = -1;

            _values.Clear();
        }

        public void Dispose()
        {
            _countAndStartIndex.Dispose();

            _values.Dispose();
        }
    }

    public struct RollbackComponent<T> where T : unmanaged, IComponentData
    {
        private NativeArray<int> __countAndStartIndex;
        private NativeList<T> __values;

        private ComponentTypeHandle<T> __type;
        private ComponentLookup<T> __targets;

#if ENABLE_PROFILER
        private ProfilerMarker __restore;
        private ProfilerMarker __save;
        private ProfilerMarker __clear;
#endif

        internal unsafe RollbackComponentContainer container
        {
            get
            {
                RollbackComponentContainer container;
                container._countAndStartIndex = __countAndStartIndex;
                container._values = UnsafeUtility.AsRef<NativeList<byte>>(UnsafeUtility.AddressOf(ref __values));

                return container;
            }
        }

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

            __countAndStartIndex = new NativeArray<int>(2, allocator, NativeArrayOptions.ClearMemory);
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
                return RollbackComponentSaveFunction<T>.Delegate(__countAndStartIndex, __values, __type.UpdateAsRef(ref systemState), ref data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RollbackComponentClearFunction<T> DelegateClear()
        {
#if ENABLE_PROFILER
            using (__clear.Auto())
#endif
                return new RollbackComponentClearFunction<T>(__values);
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
        private NativeArray<int> __countAndStartIndex;

        [ReadOnly]
        private ComponentTypeHandle<T> __type;

        public static RollbackComponentSaveFunction<T> Delegate(
            NativeArray<int> countAndStartIndex,
            NativeList<T> values,
            in ComponentTypeHandle<T> type,
            ref RollbackSaveData data)
        {
            RollbackResize<T> resize;
            resize.entityCount = data.entityCount;
            resize.countAndStartIndex = countAndStartIndex;
            resize.values = values;
            var jobHandle = resize.ScheduleByRef(data.lookupJobManager.readOnlyJobHandle);

            data.lookupJobManager.AddReadOnlyDependency(jobHandle);

            return new RollbackComponentSaveFunction<T>(values.AsDeferredJobArray(), countAndStartIndex, type);
        }

        private RollbackComponentSaveFunction(NativeArray<T> values, in NativeArray<int> countAndStartIndex, in ComponentTypeHandle<T> type)
        {
            __values = values;
            __countAndStartIndex = countAndStartIndex;
            __type = type;
        }

        public void Invoke(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var values = chunk.GetNativeArray(ref __type);
            if (useEnabledMask)
            {
                int index = firstEntityIndex + __countAndStartIndex[1];
                var iterator = new ChunkEntityEnumerator(true, chunkEnabledMask, chunk.Count);
                while(iterator.NextEntityIndex(out int i))
                    __values[index++] = values[i];
            }
            else
                NativeArray<T>.Copy(values, 0, __values, firstEntityIndex + __countAndStartIndex[1], chunk.Count);

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

    public struct RollbackBufferContainer : IRollbackContainer
    {
        internal NativeCounter _counter;
        internal NativeArray<int> _countAndStartIndex;
        internal NativeList<RollbackChunk> _chunks;
        internal NativeList<byte> _values;

        public void Clear()
        {
            _counter.count = 0;
            _countAndStartIndex[0] = 0;
            _countAndStartIndex[1] = -1;
            _values.Clear();
            _chunks.Clear();
        }

        public void Dispose()
        {
            _counter.Dispose();
            _countAndStartIndex.Dispose();
            _values.Dispose();
            _chunks.Dispose();
        }
    }

    public struct RollbackBuffer<T> where T : unmanaged, IBufferElementData
    {
        private NativeCounter __counter;
        private NativeArray<int> __countAndStartIndex;
        private NativeList<RollbackChunk> __chunks;
        private NativeList<T> __values;

        private BufferTypeHandle<T> __type;
        private BufferLookup<T> __targets;

#if ENABLE_PROFILER
        private ProfilerMarker __restore;
        private ProfilerMarker __save;
        private ProfilerMarker __clear;
#endif

        internal unsafe RollbackBufferContainer container
        {
            get
            {
                RollbackBufferContainer container;
                container._counter = __counter;
                container._countAndStartIndex = __countAndStartIndex;
                container._chunks = __chunks;
                container._values = UnsafeUtility.AsRef<NativeList<byte>>(UnsafeUtility.AddressOf(ref __values));

                return container;
            }
        }

        public RollbackBuffer(int capacity, Allocator allocator, ref SystemState systemState)
        {
            BurstUtility.InitializeJob<RollbackResize<RollbackChunk>>();
            BurstUtility.InitializeJob<RollbackSaveBufferResizeValues<T>>();

            __counter = new NativeCounter(allocator);
            __countAndStartIndex = new NativeArray<int>(2, allocator, NativeArrayOptions.ClearMemory);
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
                    ref __counter,
                    ref __countAndStartIndex, 
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

        /*public void Clear()
        {
            container.Clear();
        }

        public void Dispose()
        {
            container.Dispose();
        }*/
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
        private NativeArray<int> __countAndStartIndex;

        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;

        private NativeArray<T> __values;

        public static RollbackBufferSaveFunction<T> Deletege(
            ref NativeCounter counter,
            ref NativeArray<int> countAndStartIndex,
            in NativeList<T> values,
            in NativeList<RollbackChunk> chunks,
            in BufferTypeHandle<T> type,
            in EntityQuery group,
            ref RollbackSaveData data)
        {
            RollbackResize<RollbackChunk> resize;
            resize.entityCount = data.entityCount;
            resize.countAndStartIndex = countAndStartIndex;
            resize.values = chunks;
            var jobHandle = resize.ScheduleByRef(data.lookupJobManager.readOnlyJobHandle);

            var frameChunks = chunks.AsDeferredJobArray();

            RollbackSaveBufferCount<T> count;
            count.type = type;
            count.countAndStartIndex = countAndStartIndex;
            count.chunkBaseEntityIndices = data.chunkBaseEntityIndices;
            count.chunks = frameChunks;
            count.counter = counter;
            jobHandle = count.ScheduleParallelByRef(group, jobHandle);

            RollbackSaveBufferResizeValues<T> resizeValues;
            resizeValues.counter = counter;
            resizeValues.values = values;
            jobHandle = resizeValues.ScheduleByRef(jobHandle);

            data.lookupJobManager.AddReadOnlyDependency(jobHandle);

            return new RollbackBufferSaveFunction<T>(type, countAndStartIndex, frameChunks, values.AsDeferredJobArray());
        }

        private RollbackBufferSaveFunction(
            in BufferTypeHandle<T> type,
            in NativeArray<int> countAndStartIndex,
            in NativeArray<RollbackChunk> chunks,
            in NativeArray<T> values)
        {
            __type = type;
            __chunks = chunks;
            __countAndStartIndex = countAndStartIndex;
            __values = values;
        }

        public void Invoke(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var bufferAccessor = chunk.GetBufferAccessor(ref __type);

            RollbackChunk frameChunk;
            int index = firstEntityIndex + __countAndStartIndex[1];
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
            [ReadOnly]
            public NativeArray<int> count;

            public NativeParallelHashMap<Entity, int> value;

            public void Execute()
            {
                value.Clear();
                value.Capacity = math.max(value.Capacity, count[0]);
            }
        }

        [BurstCompile]
        private struct BuildEntityIndices : IJobParallelForDefer
        {
            [ReadOnly]
            public NativeArray<int> countAndStartIndex;
            [ReadOnly]
            public NativeArray<Entity> entities;
            public NativeParallelHashMap<Entity, int>.ParallelWriter results;

            public void Execute(int index)
            {
                results.TryAdd(entities[countAndStartIndex[1] + index], index);
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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NativeArray<int> countAndStartIndex => __group.countAndStartIndex;

        public RollbackManager(ref SystemState systemState, Allocator allocator, int innerloopBatchCount = 1/*, uint maxFrameCount = 256*/)
        {
            InnerloopBatchCount = innerloopBatchCount;
            /*MaxFrameCount = maxFrameCount;

            __flagGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<RollbackFlag>());
            __restoreFrameGroup = systemState.GetEntityQuery(ComponentType.ReadOnly<RollbackRestoreFrame>());*/
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __frameGroup = builder
                        .WithAll<RollbackFrame, RollbackFrameRestore, RollbackFrameSave, RollbackFrameClear>()
                        .WithOptions(EntityQueryOptions.IncludeSystems)
                        .Build(ref systemState);

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

        //public int GetEntityCount(uint frameIndex) => __group.GetEntityCount(frameIndex);

        public int IndexOf(uint frameIndex, in Entity entity) => __group.IndexOf(frameIndex, entity);

        public JobHandle GetChunk(uint frameIndex, in JobHandle jobHandle) => __group.GetChunk(frameIndex, jobHandle);

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

        public JobHandle AddOrRemoveComponentIfNotSaved(
#if UNITY_EDITOR
            bool isAddOrRemove, 
#endif
            uint frameIndex,
            in ComponentType componentType,
            in EntityQuery group,
            in EntityTypeHandle entityType, 
            in EntityCommandPool<EntityCommandStructChange> commander,
            in JobHandle inputDeps, 
            bool needChunk)
        {
#if ENABLE_PROFILER
            using (__addOrRemoveComponentIfNotSaved.Auto())
#endif

            {
                var jobHandle = needChunk ? __group.GetChunk(frameIndex, inputDeps) : inputDeps;

                //if (__group.GetEntities(frameIndex, out var entities))
                //{
                var entityIndicesParallelWriter = __entityIndices.AsParallelWriter();
                var countAndStartIndex = __group.countAndStartIndex;

                ClearHashMap clearHashMap;
                clearHashMap.count = countAndStartIndex;
                clearHashMap.value = __entityIndices;
                jobHandle = clearHashMap.ScheduleByRef(jobHandle);

                BuildEntityIndices buildEntityIndices;
                buildEntityIndices.countAndStartIndex = countAndStartIndex;
                buildEntityIndices.entities = __group.entities;
                buildEntityIndices.results = entityIndicesParallelWriter;
                jobHandle = buildEntityIndices.ScheduleUnsafeIndex0ByRef(countAndStartIndex, InnerloopBatchCount, JobHandle.CombineDependencies(jobHandle, __group.entityJobHandle));

                __group.entityJobHandle = jobHandle;

                var entityManager = commander.Create();

                RestoreChunk restoreChunk;
#if UNITY_EDITOR
                restoreChunk.isAddOrRemove = isAddOrRemove;
                restoreChunk.frameIndex = frameIndex;
#endif
                restoreChunk.componentType = componentType;
                restoreChunk.entityType = entityType;
                restoreChunk.entityIndices = __entityIndices;
                restoreChunk.entityManager = entityManager.parallelWriter;

                jobHandle = restoreChunk.ScheduleParallelByRef(group, jobHandle);

                entityManager.AddJobHandleForProducer<RestoreChunk>(jobHandle);

                return jobHandle;
                //systemState.Dependency = jobHandle;

                //return true;
                //}

                //return false;
            }
        }

        public JobHandle AddComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityTypeHandle entityType,
            in EntityCommandPool<EntityCommandStructChange> addComponentCommander,
            in JobHandle inputDeps,
            bool needChunk) where T : struct, IComponentData
        {
#if ENABLE_PROFILER
            using (__addComponentIfNotSaved.Auto())
#endif
            {
                if (frameIndex < minSaveFrameIndex)
                {
                    UnityEngine.Debug.LogError($"Frame Index {frameIndex} Less Than Range {minSaveFrameIndex}");

                    return inputDeps;
                }
                else
                {
                    return AddOrRemoveComponentIfNotSaved(
#if UNITY_EDITOR
                        true,
#endif
                        frameIndex,
                        ComponentType.ReadWrite<T>(),
                        group,
                        entityType,
                        addComponentCommander,
                        inputDeps, 
                        needChunk);
                    /*{
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
                        systemState.EntityManager.AddComponent<T>(group);*/
                }
            }
        }

        public JobHandle RemoveComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityTypeHandle entityType,
            in EntityCommandPool<EntityCommandStructChange> removeComponentCommander,
            in JobHandle inputDeps,
            bool needChunk) where T : struct, IComponentData
        {
#if ENABLE_PROFILER
            using (__removeComponentIfNotSaved.Auto())
#endif
            {
                if (frameIndex < minSaveFrameIndex)
                {
                    UnityEngine.Debug.LogError($"Frame Index {frameIndex} Less Than Range {minSaveFrameIndex}");

                    return inputDeps;
                }
                else
                {
                    return AddOrRemoveComponentIfNotSaved(
#if UNITY_EDITOR
                        false,
#endif
                        frameIndex,
                        ComponentType.ReadWrite<T>(),
                        group,
                        entityType,
                        removeComponentCommander,
                        inputDeps,
                        needChunk);
                }
                /*{
                    //jobHandle.Complete();
                    //systemState.CompleteDependency();

                    //jobHandle = endFrameBarrier.RemoveComponent<T>(group, inputDeps);
                    systemState.EntityManager.RemoveComponent<T>(group);
                }*/
            }
        }

        public JobHandle ScheduleParallel<T>(
            in T instance,
            uint frameIndex,
            in JobHandle dependsOn,
            bool needChunk) where T : struct, IRollbackRestore
        {
#if ENABLE_PROFILER
            using (__scheduleRestore.Auto())
#endif
                return __group.ScheduleParallel(
                    instance,
                    frameIndex,
                    InnerloopBatchCount,
                    dependsOn,
                    needChunk);
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

    public struct RollbackManager<TRestore, TSave, TClear> 
        where TRestore : struct, IRollbackRestore
        where TSave : struct, IRollbackSave
        where TClear : struct, IRollbackClear
    {
        private RollbackManager __imp;

        internal RollbackManager container => __imp;

        public NativeArray<int> countAndStartIndex => __imp.countAndStartIndex;

        public RollbackManager(ref SystemState systemState, Allocator allocator, int innerloopBatchCount = 1/*, uint maxFrameCount = 256*/)
        {
            __imp = new RollbackManager(ref systemState, allocator, innerloopBatchCount/*, maxFrameCount*/);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle GetChunk(uint frameIndex, in JobHandle jobHandle) => __imp.GetChunk(frameIndex, jobHandle);

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
        public JobHandle AddComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityTypeHandle entityType,
            in EntityCommandPool<EntityCommandStructChange> addComponentCommander,
            in JobHandle inputDeps, 
            bool needChunk)
            where T : struct, IComponentData => __imp.AddComponentIfNotSaved<T>(
                frameIndex, 
                group, 
                entityType, 
                addComponentCommander, 
                inputDeps, 
                needChunk);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle RemoveComponentIfNotSaved<T>(
            uint frameIndex,
            in EntityQuery group,
            in EntityTypeHandle entityType,
            in EntityCommandPool<EntityCommandStructChange> removeComponentCommander,
            in JobHandle inputDeps,
            bool needChunk)
            where T : struct, IComponentData => __imp.RemoveComponentIfNotSaved<T>(
                frameIndex, 
                group, 
                entityType, 
                removeComponentCommander, 
                inputDeps,
                needChunk);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public JobHandle ScheduleParallel(
            in TRestore instance,
            uint frameIndex,
            in JobHandle dependsOn, 
            bool needChunk) => __imp.ScheduleParallel(instance, frameIndex, dependsOn, needChunk);

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

    public struct RollbackContainerManager
    {
        private struct Container
        {
            public UnsafeBlock value;
            public FunctionPointer<RollbackContainerDelegate> clearFunction;
            public FunctionPointer<RollbackContainerDelegate> disposeFunction;

            public void Clear()
            {
                clearFunction.Invoke(value);
            }

            public void Dispose()
            {
                disposeFunction.Invoke(value);
            }
        }

        private NativeBuffer __buffer;
        private NativeList<Container> __containers;

        public RollbackContainerManager(in AllocatorManager.AllocatorHandle allocator)
        {
            __buffer = new NativeBuffer(allocator, 1);

            __containers = new NativeList<Container>(allocator);
        }

        public RollbackManager<TRestore, TSave, TClear> CreateManager<TRestore, TSave, TClear>(ref SystemState systemState, int innerloopBatchCount = 4/*, uint maxFrameCount = 256*/)
            where TRestore : struct, IRollbackRestore
            where TSave : struct, IRollbackSave
            where TClear : struct, IRollbackClear
        {
            var result = new RollbackManager<TRestore, TSave, TClear>(ref systemState, Allocator.Persistent, innerloopBatchCount/*, maxFrameCount*/);

            Manage(result.container, _ClearManagerFunction, _DisposeManagerFunction.Data);

            return result;
        }

        public RollbackManager CreateManager(ref SystemState systemState, int innerloopBatchCount = 1/*, uint maxFrameCount = 256*/)
        {
            var result = new RollbackManager(ref systemState, Allocator.Persistent, innerloopBatchCount/*, maxFrameCount*/);

            Manage(result, _ClearManagerFunction, _DisposeManagerFunction.Data);

            return result;
        }

        public RollbackGroup CreateGroup()
        {
            var result = new RollbackGroup(1, Allocator.Persistent);

            Manage(result, _ClearGroupFunction, _DisposeGroupFunction.Data);

            return result;
        }

        public RollbackComponent<T> CreateComponent<T>(ref SystemState systemState) where T : unmanaged, IComponentData
        {
            var result = new RollbackComponent<T>(1, Allocator.Persistent, ref systemState);

            Manage(result.container, _ClearComponentFunction, _DisposeComponentFunction.Data);

            return result;
        }

        public RollbackBuffer<T> CreateBuffer<T>(ref SystemState systemState) where T : unmanaged, IBufferElementData
        {
            var result = new RollbackBuffer<T>(1, Allocator.Persistent, ref systemState);

            Manage(result.container, _ClearBufferFunction, _DisposeBufferFunction.Data);

            return result;
        }

        public void Manage<T>(
            in T value, 
            in FunctionPointer<RollbackContainerDelegate> clearFunction , 
            FunctionPointer<RollbackContainerDelegate> disposeFunction) where T : unmanaged, IRollbackContainer
        {
            var writer = __buffer.writer;

            Container container;
            container.value = (UnsafeBlock)writer.WriteBlock(value);
            container.clearFunction = clearFunction;
            container.disposeFunction = disposeFunction;

            __containers.Add(container);
        }

        public void Clear()
        {
            foreach (var container in __containers)
                container.Clear();
        }

        public void Dispose()
        {
            foreach (var container in __containers)
                container.Dispose();

            __containers.Dispose();
        }
    }

    [BurstCompile]
    public static partial class RollbackUtility
    {
        internal static readonly FunctionPointer<RollbackContainerDelegate> _ClearManagerFunction = BurstCompiler.CompileFunctionPointer<RollbackContainerDelegate>(__ClearManager);

        internal static readonly FunctionPointer<RollbackContainerDelegate> _ClearGroupFunction = BurstCompiler.CompileFunctionPointer<RollbackContainerDelegate>(__ClearGroup);

        internal static readonly FunctionPointer<RollbackContainerDelegate> _ClearComponentFunction = BurstCompiler.CompileFunctionPointer<RollbackContainerDelegate>(__ClearComponent);

        internal static readonly FunctionPointer<RollbackContainerDelegate> _ClearBufferFunction = BurstCompiler.CompileFunctionPointer<RollbackContainerDelegate>(__ClearBuffer);

        internal static readonly SharedStatic<FunctionPointer<RollbackContainerDelegate>> _DisposeManagerFunction = SharedStatic<FunctionPointer<RollbackContainerDelegate>>.GetOrCreate<RollbackManager>();

        internal static readonly SharedStatic<FunctionPointer<RollbackContainerDelegate>> _DisposeGroupFunction = SharedStatic<FunctionPointer<RollbackContainerDelegate>>.GetOrCreate<RollbackGroup>();

        internal static readonly SharedStatic<FunctionPointer<RollbackContainerDelegate>> _DisposeComponentFunction = SharedStatic<FunctionPointer<RollbackContainerDelegate>>.GetOrCreate<RollbackComponentContainer>();

        internal static readonly SharedStatic<FunctionPointer<RollbackContainerDelegate>> _DisposeBufferFunction = SharedStatic<FunctionPointer<RollbackContainerDelegate>>.GetOrCreate<RollbackBufferContainer>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void Init()
        {
            _DisposeManagerFunction.Data = FunctionWrapperUtility.CompileManagedFunctionPointer<RollbackContainerDelegate>(__DisposeManager);

            _DisposeGroupFunction.Data = FunctionWrapperUtility.CompileManagedFunctionPointer<RollbackContainerDelegate>(__DisposeGroup);

            _DisposeComponentFunction.Data = FunctionWrapperUtility.CompileManagedFunctionPointer<RollbackContainerDelegate>(__DisposeComponent);

            _DisposeBufferFunction.Data = FunctionWrapperUtility.CompileManagedFunctionPointer<RollbackContainerDelegate>(__DisposeBuffer);
        }

    #region Clear
    [BurstCompile]
        [MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        private static void __ClearManager(in UnsafeBlock value)
        {
            value.As<RollbackManager>().Clear();
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        private static void __ClearGroup(in UnsafeBlock value)
        {
            value.As<RollbackGroup>().Clear();
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        private static void __ClearComponent(in UnsafeBlock value)
        {
            value.As<RollbackComponentContainer>().Clear();
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        private static void __ClearBuffer(in UnsafeBlock value)
        {
            value.As<RollbackBufferContainer>().Clear();
        }
        #endregion

        #region Dispose
        //[BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        private static void __DisposeManager(in UnsafeBlock value)
        {
            value.As<RollbackManager>().Dispose();
        }

        //[BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        internal static void __DisposeGroup(in UnsafeBlock value)
        {
            value.As<RollbackGroup>().Dispose();
        }

        //[BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        internal static void __DisposeComponent(in UnsafeBlock value)
        {
            value.As<RollbackComponentContainer>().Dispose();
        }

        //[BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(RollbackContainerDelegate))]
        internal static void __DisposeBuffer(in UnsafeBlock value)
        {
            value.As<RollbackBufferContainer>().Dispose();
        }
        #endregion

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
