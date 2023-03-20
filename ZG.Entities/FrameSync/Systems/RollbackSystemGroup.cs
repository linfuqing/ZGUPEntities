using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Profiling;

//[assembly: RegisterGenericJobType(typeof(ZG.RollbackResize<Entity>))]
//[assembly: RegisterGenericJobType(typeof(ZG.RollbackResize<ZG.RollbackChunk>))]

namespace ZG
{
    /*public interface IRollbackSystem
    {
        void Clear();
    }*/

    /*public struct RollbackFlag : IComponentData
    {
        public enum Flag
        {
            Restore = 0x01,
            Clear = 0x02
        }

        public Flag value;
    }*/

/*[AlwaysUpdateSystem, UpdateInGroup(typeof(RollbackSystemGroup))]
public abstract class RollbackSystem : JobComponentSystem, IRollbackSystem
{
    private interface IManagedObject : IDisposable
    {
        void Clear();
    }

    public struct Group : IManagedObject
    {
        [BurstCompile]
        public struct SaveChunks : IJob
        {
            public uint frameIndex;
            public int entityCount;

            [ReadOnly]
            public NativeReference<int> startIndex;

            public NativeHashMap<uint, RollbackChunk> chunks;

            public void Execute()
            {
                RollbackChunk chunk;
                chunk.startIndex = startIndex.Value;
                chunk.count = entityCount;

                chunks[frameIndex] = chunk;
            }
        }

        private NativeArray<JobHandle> __jobHandles;
        private NativeReference<int> __startIndex;
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

        internal Group(int capacity)
        {
            __jobHandles = new NativeArray<JobHandle>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            __startIndex = new NativeReference<int>(Allocator.Persistent);
            __entities = new NativeList<Entity>(Allocator.Persistent);
            __chunks = new NativeHashMap<uint, RollbackChunk>(capacity, Allocator.Persistent);

#if ENABLE_PROFILER
            __resize = new ProfilerMarker("Resize");
            __saveEntities = new ProfilerMarker("Save Entities");
            __saveChunks = new ProfilerMarker("Save Chunks");
#endif
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

        public JobHandle Schedule<T>(in T instance, uint frameIndex, int innerloopBatchCount, in JobHandle dependsOn) where T : struct, IRollbackRestore
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            if (!__chunks.TryGetValue(frameIndex, out var chunk))
                return dependsOn;

            RollbackRestore<T> restore;
            restore.startIndex = chunk.startIndex;
            restore.entityArray = __entities;
            restore.instance = instance;

            var jobHandle = innerloopBatchCount > 1 ? restore.Schedule(chunk.count, innerloopBatchCount, dependsOn) : restore.ScheduleSingle(chunk.count, dependsOn);

            entityJobHandle = jobHandle;

            return jobHandle;
        }

        public JobHandle Schedule<T>(
            in T instance, 
            int entityCount, 
            uint frameIndex, 
            in EntityTypeHandle entityType, 
            in EntityQuery group, 
            in JobHandle dependsOn) where T : struct, IRollbackSave
        {
            if (entityCount < 1)
                return dependsOn;

            JobHandle resizeJobHandle;

#if ENABLE_PROFILER
            using(__resize.Auto())
#endif
            {
                RollbackResize<Entity> resize;
                resize.entityCount = entityCount;
                resize.startIndex = __startIndex;
                resize.values = __entities;
                resizeJobHandle = resize.Schedule(this.entityJobHandle);
            }

            JobHandle entityJobHandle;

#if ENABLE_PROFILER
            using (__saveEntities.Auto())
#endif
            {
                RollbackSave<T> save;
                save.entityType = entityType;
                save.startIndex = __startIndex;
                save.entityArray = __entities.AsDeferredJobArray();
                save.instance = instance;

                entityJobHandle = save.ScheduleParallel(group, 1, JobHandle.CombineDependencies(resizeJobHandle, dependsOn));
            }

            this.entityJobHandle = entityJobHandle;

            JobHandle chunkJobHandle;

#if ENABLE_PROFILER
            using (__saveChunks.Auto())
#endif
            {
                SaveChunks saveChunks;
                saveChunks.frameIndex = frameIndex;
                saveChunks.entityCount = entityCount;
                saveChunks.startIndex = __startIndex;
                saveChunks.chunks = __chunks;

                chunkJobHandle = saveChunks.Schedule(resizeJobHandle);
            }

            this.chunkJobHandle = chunkJobHandle;

            return JobHandle.CombineDependencies(entityJobHandle, chunkJobHandle);
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

        void IManagedObject.Clear()
        {
            chunkJobHandle.Complete();
            chunkJobHandle = default;

            entityJobHandle.Complete();
            entityJobHandle = default;

            __entities.Clear();
            __chunks.Clear();
        }

        void IDisposable.Dispose()
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

    public struct Component<T> : IManagedObject where T : struct, IComponentData
    {
        private NativeReference<int> __startIndex;
        private NativeList<T> __values;

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

        internal Component(int capacity)
        {
            __startIndex = new NativeReference<int>(Allocator.Persistent);
            __values = new NativeList<T>(capacity, Allocator.Persistent);

#if ENABLE_PROFILER
            __restore = new ProfilerMarker($"{typeof(T).Name} Restore");
            __save = new ProfilerMarker($"{typeof(T).Name} Save");
            __clear = new ProfilerMarker($"{typeof(T).Name} Clear");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentRestoreChunkFunction<T> DelegateRestoreChunk(in ComponentTypeHandle<T> type)
        {
            return new ComponentRestoreChunkFunction<T>(__values, type);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentRestoreFunction<T> DelegateRestore(in ComponentLookup<T> targets)
        {
#if ENABLE_PROFILER
            using(__restore.Auto())
#endif
            return new ComponentRestoreFunction<T>(__values, targets);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentSaveFunction<T> DelegateSave(int entityCount, in ComponentTypeHandle<T> type, ref JobHandle jobHandle)
        {
#if ENABLE_PROFILER
            using (__save.Auto())
#endif
                return ComponentSaveFunction<T>.Delegate(entityCount, __startIndex, __values, type, ref jobHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ComponentClearFunction<T> DelegateClear()
        {
#if ENABLE_PROFILER
            using (__clear.Auto())
#endif
                return new ComponentClearFunction<T>(__values);
        }

        void IManagedObject.Clear()
        {
            __values.Clear();
        }

        void IDisposable.Dispose()
        {
            __startIndex.Dispose();

            __values.Dispose();
        }
    }

    public struct ComponentRestoreChunkFunction<T> where T : struct, IComponentData
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

        public ComponentRestoreChunkFunction(in NativeArray<T> values, ComponentTypeHandle<T> type)
        {
            __values = values;
            __type = type;
        }

        public ComponentRestoreEntityFunction<T> Invoke(ArchetypeChunk chunk)
        {
            return new ComponentRestoreEntityFunction<T>(__values, chunk.GetNativeArray(__type));
        }
    }

    public struct ComponentRestoreEntityFunction<T> where T : struct, IComponentData
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

        public ComponentRestoreEntityFunction(in NativeArray<T> values, NativeArray<T> targets)
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

    public struct ComponentRestoreFunction<T> where T : struct, IComponentData
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

        public ComponentRestoreFunction(in NativeArray<T> values, ComponentLookup<T> targets)
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

    public struct ComponentSaveFunction<T> where T : struct, IComponentData
    {
        private NativeArray<T> __values;

        [ReadOnly]
        private NativeReference<int> __startIndex;

        [ReadOnly]
        private ComponentTypeHandle<T> __type;

        public static ComponentSaveFunction<T> Delegate(
            int entityCount, 
            NativeReference<int> startIndex, 
            NativeList<T> values, 
            in ComponentTypeHandle<T> type, 
            ref JobHandle jobHandle)
        {
            if (entityCount > 0)
            {
                RollbackResize<T> resize;
                resize.entityCount = entityCount;
                resize.startIndex = startIndex;
                resize.values = values;
                jobHandle = resize.Schedule(jobHandle);

                return new ComponentSaveFunction<T>(values.AsDeferredJobArray(), startIndex, type);
            }

            return default;
        }

        private ComponentSaveFunction(NativeArray<T> values, in NativeReference<int> startIndex, in ComponentTypeHandle<T> type)
        {
            __values = values;
            __startIndex = startIndex;
            __type = type;
        }

        public void Invoke(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            NativeArray<T>.Copy(chunk.GetNativeArray(__type), 0, __values, firstEntityIndex + __startIndex.Value, chunk.Count);

            //__values.AddRange(chunk.GetNativeArray(__type));
        }
    }

    public struct ComponentClearFunction<T> where T : struct, IComponentData
    {
        private NativeList<T> __values;

        public ComponentClearFunction(NativeList<T> values)
        {
            __values = values;
        }

        public void Move(int fromIndex, int toIndex, int count)
        {
            __values.AsArray().Slice().MemMove(fromIndex, toIndex, count);
        }

        public void Resize(int count)
        {
            __values.ResizeUninitialized(count);
        }
    }

    public struct Buffer<T> : IManagedObject where T : struct, IBufferElementData
    {
        private NativeCounter __counter;
        private NativeReference<int> __startIndex;
        private NativeList<T> __values;
        private NativeList<RollbackChunk> __chunks;

#if ENABLE_PROFILER
        private ProfilerMarker __restore;
        private ProfilerMarker __save;
        private ProfilerMarker __clear;
#endif

        internal Buffer(int capacity)
        {
            __counter = new NativeCounter(Allocator.Persistent);
            __startIndex = new NativeReference<int>(Allocator.Persistent);
            __values = new NativeList<T>(capacity, Allocator.Persistent);
            __chunks = new NativeList<RollbackChunk>(capacity, Allocator.Persistent);

#if ENABLE_PROFILER
            __restore = new ProfilerMarker($"{typeof(T).Name} Restore");
            __save = new ProfilerMarker($"{typeof(T).Name} Save");
            __clear = new ProfilerMarker($"{typeof(T).Name} Clear");
#endif
        }

        public void CopyTo(int entityIndex, in DynamicBuffer<T> targets)
        {
            //targets.Clear();

            var frameChunk = __chunks[entityIndex];
            for (int i = 0; i < frameChunk.count; ++i)
                targets.Add(__values[frameChunk.startIndex + i]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferRestoreChunkFunction<T> DelegateRestoreChunk(in BufferTypeHandle<T> type)
        {
                return new BufferRestoreChunkFunction<T>(__values, __chunks, type);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferRestoreFunction<T> DelegateRestore(in BufferLookup<T> targets)
        {
#if ENABLE_PROFILER
            using (__restore.Auto())
#endif
                return new BufferRestoreFunction<T>(__values, __chunks, targets);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferSaveFunction<T> DelegateSave(
            int entityCount, 
            in BufferTypeHandle<T> type, 
            in EntityQuery group, 
            ref JobHandle jobHandle)
        {
#if ENABLE_PROFILER
            using (__save.Auto())
#endif
                return BufferSaveFunction<T>.Deletege(
                entityCount, 
                __counter,
                __startIndex, 
                __values, 
                __chunks, 
                type,
                group, 
                ref jobHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BufferClearFunction<T> DelegateClear()
        {
#if ENABLE_PROFILER
            using (__clear.Auto())
#endif
                return new BufferClearFunction<T>(__counter, __values, __chunks);
        }

        void IManagedObject.Clear()
        {
            __counter.count = 0;
            __values.Clear();
            __chunks.Clear();
        }

        void IDisposable.Dispose()
        {
            __counter.Dispose();
            __startIndex.Dispose();
            __values.Dispose();
            __chunks.Dispose();
        }
    }

    public struct BufferRestoreChunkFunction<T> where T : struct, IBufferElementData
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

        public BufferRestoreChunkFunction(in NativeArray<T> values, in NativeArray<RollbackChunk> chunks, in BufferTypeHandle<T> type)
        {
            __values = values;
            __chunks = chunks;
            __type = type;
        }

        public BufferRestoreEntityFunction<T> Invoke(in ArchetypeChunk chunk)
        {
            return new BufferRestoreEntityFunction<T>(__values, __chunks, chunk.GetBufferAccessor(__type));
        }
    }

    public struct BufferRestoreEntityFunction<T> where T : struct, IBufferElementData
    {
        [ReadOnly]
        private NativeArray<T> __values;
        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;
        private BufferAccessor<T> __targets;

        public BufferRestoreEntityFunction(in NativeArray<T> values, in NativeArray<RollbackChunk> chunks, in BufferAccessor<T> targets)
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

    public struct BufferRestoreFunction<T> where T : struct, IBufferElementData
    {
        [ReadOnly]
        private NativeArray<T> __values;

        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;

        [NativeDisableParallelForRestriction]
        private BufferLookup<T> __targets;

        public bool IsExists(Entity entity) => __targets.HasComponent(entity);

        public BufferRestoreFunction(in NativeArray<T> values, in NativeArray<RollbackChunk> chunks, in BufferLookup<T> targets)
        {
            __values = values;
            __chunks = chunks;
            __targets = targets;
        }

        public void CopyTo(int entityIndex, in DynamicBuffer<T> targets)
        {
            var chunk = __chunks[entityIndex];

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

    public struct BufferSaveFunction<T> where T : struct, IBufferElementData
    {
        [ReadOnly]
        private BufferTypeHandle<T> __type;

        [ReadOnly]
        private NativeReference<int> __startIndex;

        [ReadOnly]
        private NativeArray<RollbackChunk> __chunks;

        private NativeArray<T> __values;

        public static BufferSaveFunction<T> Deletege(
            int entityCount,
            NativeCounter counter,
            NativeReference<int> startIndex, 
            in NativeList<T> values,
            in NativeList<RollbackChunk> chunks,
            in BufferTypeHandle<T> type,
            in EntityQuery group, 
            ref JobHandle jobHandle)
        {
            //UnityEngine.Assertions.Assert.AreEqual(entityCount, group.CalculateEntityCount());
            if (entityCount > 0)
            {
                RollbackResize<RollbackChunk> resize;
                resize.entityCount = entityCount;
                resize.startIndex = startIndex;
                resize.values = chunks;
                jobHandle = resize.Schedule(jobHandle);

                var frameChunks = chunks.AsDeferredJobArray();

                RollbackSaveBufferCount<T> count;
                count.type = type;
                count.startIndex = startIndex;
                count.chunks = frameChunks;
                count.counter = counter;
                jobHandle = count.ScheduleParallel(group, 1, jobHandle);

                RollbackSaveBufferResizeValues<T> resizeValues;
                resizeValues.counter = counter;
                resizeValues.values = values;
                jobHandle = resizeValues.Schedule(jobHandle);

                return new BufferSaveFunction<T>(type, startIndex, frameChunks, values.AsDeferredJobArray());
            }

            return default;
        }

        private BufferSaveFunction(
            in BufferTypeHandle<T> type,
            in NativeReference<int> startIndex, 
            in NativeArray<RollbackChunk> chunks,
            in NativeArray<T> values)
        {
            __type = type;
            __chunks = chunks;
            __startIndex = startIndex;
            __values = values;
        }

        public void Invoke(in ArchetypeChunk chunk, int firstEntityIndex)
        {
            var bufferAccessor = chunk.GetBufferAccessor(__type);

            firstEntityIndex += __startIndex.Value;

            int count = chunk.Count;
            RollbackChunk frameChunk;
            for (int i = 0; i < count; ++i)
            {
                frameChunk = __chunks[firstEntityIndex + i];
                if(frameChunk.count > 0)
                    NativeArray<T>.Copy(bufferAccessor[i].AsNativeArray(), 0, __values, frameChunk.startIndex, frameChunk.count);
            }
        }
    }

    public struct BufferClearFunction<T> where T : struct, IBufferElementData
    {
        private NativeCounter __counter;
        private NativeList<T> __values;
        private NativeList<RollbackChunk> __chunks;

        private RollbackChunk __chunk;

        public BufferClearFunction(NativeCounter counter, in NativeList<T> values, in NativeList<RollbackChunk> chunks)
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
            if(__chunk.count != 0)
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

                __values.AsArray().Slice().MemMove(offset, __chunk.startIndex, length - offset);

                __chunk.startIndex = length - __chunk.count;
            }
#if DEBUG
            else
                UnityEngine.Assertions.Assert.AreEqual(length, offset);

#endif

            UnityEngine.Assertions.Assert.IsTrue(__chunk.startIndex <= __values.Length);

            __values.ResizeUninitialized(__chunk.startIndex);

            UnityEngine.Assertions.Assert.IsTrue(count <= __chunks.Length);

            __chunks.ResizeUninitialized(count);
        }
    }

#if ENABLE_PROFILER
    private ProfilerMarker __restore;
    private ProfilerMarker __save;
#endif

    private NativeList<JobHandle> __saveJobHandles;

    private List<IManagedObject> __managedObjects;

    public JobHandle saveJobHandle
    {
        get
        {
            var jobHandle = JobHandle.CombineDependencies(__saveJobHandles);

            __saveJobHandles.Clear();

            return jobHandle;
        }
    }

    public RollbackSystemGroup rollbackSystemGroup
    {
        get;

        private set;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentRestoreFunction<T> DelegateRestore<T>(in Component<T> instance) where T : struct, IComponentData
    {
        return instance.DelegateRestore(GetComponentLookup<T>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentSaveFunction<T> DelegateSave<T>(int entityCount, in Component<T> instance, in JobHandle inputDeps) where T : struct, IComponentData
    {
        if (entityCount < 1)
            return default;

        var jobHandle = inputDeps;
        var result = instance.DelegateSave(entityCount, GetComponentTypeHandle<T>(true), ref jobHandle);

        __saveJobHandles.Add(jobHandle);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentClearFunction<T> DelegateClear<T>(in Component<T> instance) where T : struct, IComponentData
    {
        return instance.DelegateClear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BufferRestoreFunction<T> DelegateRestore<T>(in Buffer<T> instance) where T : struct, IBufferElementData
    {
        return instance.DelegateRestore(GetBufferLookup<T>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BufferSaveFunction<T> DelegateSave<T>(
        int entityCount, 
        in Buffer<T> instance, 
        EntityQuery group,
        in JobHandle inputDeps) where T : struct, IBufferElementData
    {
        if (entityCount < 1)
            return default;

        var jobHandle = inputDeps;

        var result = instance.DelegateSave(
            entityCount,
            GetBufferTypeHandle<T>(true), 
            group, 
            ref jobHandle);

        __saveJobHandles.Add(jobHandle);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BufferClearFunction<T> DelegateClear<T>(in Buffer<T> instance) where T : struct, IBufferElementData
    {
        return instance.DelegateClear();
    }

    protected override void OnCreate()
    {
        base.OnCreate();

        rollbackSystemGroup = World.GetOrCreateSystem<RollbackSystemGroup>();

#if ENABLE_PROFILER
        __restore = new ProfilerMarker("Restore");
        __save = new ProfilerMarker("Save");
#endif

        __saveJobHandles = new NativeList<JobHandle>(Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        __saveJobHandles.Dispose();

        if (__managedObjects != null)
        {
            foreach (IManagedObject managedObject in __managedObjects)
                managedObject.Dispose();
        }

        base.OnDestroy();
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (rollbackSystemGroup.isActive && (rollbackSystemGroup.frameSyncSystemGroup.flag & FrameSyncSystemGroup.Flag.Rollback) == FrameSyncSystemGroup.Flag.Rollback)
#if ENABLE_PROFILER
            using (__restore.Auto())
#endif
                return _Restore(inputDeps);

#if ENABLE_PROFILER
        using (__save.Auto())
#endif
            return _Save(inputDeps);
    }

    protected abstract JobHandle _Restore(JobHandle inputDeps);

    protected abstract JobHandle _Save(JobHandle inputDeps);

    protected virtual void _Clear()
    {

    }

    protected Group _GetGroup(int capacity = 1)
    {
        var group = new Group(capacity);
        _Set(group);
        return group;
    }

    protected Component<T> _GetComponent<T>(int capacity = 1) where T : struct, IComponentData
    {
        var component = new Component<T>(capacity);
        _Set(component);
        return component;
    }

    protected Buffer<T> _GetBuffer<T>(int capacity = 1) where T : struct, IBufferElementData
    {
        var buffer = new Buffer<T>(capacity);
        _Set(buffer);
        return buffer;
    }

    private void _Set(IManagedObject managedObject)
    {
        if (__managedObjects == null)
            __managedObjects = new List<IManagedObject>();

        __managedObjects.Add(managedObject);
    }

    void IRollbackSystem.Clear()
    {
        if (__managedObjects != null)
        {
            foreach (IManagedObject managedObject in __managedObjects)
                managedObject.Clear();
        }

        _Clear();
    }
}

public abstract class RollbackSystemEx : RollbackSystem, IRollbackSystem
{
    [BurstCompile]
    private struct BuildEntityIndices : IJobParallelFor
    {
        [ReadOnly]
        public NativeSlice<Entity> entities;
        public NativeHashMap<Entity, int>.ParallelWriter results;

        public void Execute(int index)
        {
            results.TryAdd(entities[index], index);
        }
    }

    [BurstCompile]
    private struct RestoreChunk : IJobChunk
    {
        public ComponentType componentType;

        [ReadOnly]
        public EntityTypeHandle entityType;
        [ReadOnly]
        public NativeHashMap<Entity, int> entityIndices;

        public EntityCommandQueue<EntityCommandStructChange>.ParallelWriter entityManager;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            EntityCommandStructChange command;
            command.componentType = componentType;

            var entityArray = batchInChunk.GetNativeArray(entityType);
            Entity entity;
            int count = batchInChunk.Count;//, numEntities = entities.Length, i, j;
            for (int i = 0; i < count; ++i)
            {
                entity = entityArray[i];
                if (!entityIndices.ContainsKey(entity))
                {
                    command.entity = entity;
                    entityManager.Enqueue(command);
                }
            }
        }
    }

    public int innerloopBatchCount = 64;

    public uint maxFrameCount = 256;

    private uint __minFrameIndex = 1;
    private uint __frameIndex;

    private Group __group;

    private NativeHashMap<Entity, int> __entityIndices;

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

    public EndRollbackSystemGroupEntityCommandSystem endFrameBarrier 
    { 
        get; 
        private set;
    }

    protected int _IndexOf(uint frameIndex, Entity entity) => __group.IndexOf(frameIndex, entity);

    protected bool _AddOrRemoveComponentIfNotSaved(
        uint frameIndex, 
        in ComponentType componentType, 
        in EntityQuery group, 
        EntityCommandPool<EntityCommandStructChange> commander, 
        ref JobHandle inputDeps)
    {
#if ENABLE_PROFILER
        using (__addOrRemoveComponentIfNotSaved.Auto())
#endif

        {
            if (__group.GetEntities(frameIndex, out var entities))
            {
                var entityIndices = __entityIndices.AsParallelWriter();

                inputDeps = __entityIndices.Clear(entities.Length, inputDeps);

                BuildEntityIndices buildEntityIndices;
                buildEntityIndices.entities = entities;
                buildEntityIndices.results = entityIndices;
                inputDeps = buildEntityIndices.Schedule(entities.Length, innerloopBatchCount, inputDeps);

                __group.entityJobHandle = inputDeps;

                var entityManager = commander.Create();

                RestoreChunk restoreChunk;
                restoreChunk.componentType = componentType;
                restoreChunk.entityType = GetEntityTypeHandle();
                restoreChunk.entityIndices = __entityIndices;
                restoreChunk.entityManager = entityManager.parallelWriter;

                inputDeps = restoreChunk.ScheduleParallel(group, 1, inputDeps);

                entityManager.AddJobHandleForProducer(inputDeps);

                return true;
            }

            return false;
        }
    }

    protected JobHandle _AddComponentIfNotSaved<T>(
        uint frameIndex, 
        in EntityQuery group, 
        EntityCommandPool<EntityCommandStructChange> addComponentCommander, 
        in JobHandle inputDeps) where T : struct, IComponentData
    {
#if ENABLE_PROFILER
        using (__addComponentIfNotSaved.Auto())
#endif
        {
            JobHandle jobHandle = inputDeps;
            if (frameIndex < __minFrameIndex)
                UnityEngine.Debug.LogError($"Frame Index {frameIndex} Less Than Range {__minFrameIndex}");
            else if (!_AddOrRemoveComponentIfNotSaved(
                frameIndex,
                ComponentType.ReadWrite<T>(),
                group, 
                addComponentCommander, 
                ref jobHandle))
                //jobHandle = endFrameBarrier.AddComponent<T>(group, inputDeps);
                EntityManager.AddComponent<T>(group);

            return jobHandle;
        }
    }

    protected JobHandle _RemoveComponentIfNotSaved<T>(
        uint frameIndex, 
        EntityQuery group, 
        EntityCommandPool<EntityCommandStructChange> removeComponentCommander, 
        in JobHandle inputDeps) where T : struct, IComponentData
    {
#if ENABLE_PROFILER
        using (__removeComponentIfNotSaved.Auto())
#endif
        {
            JobHandle jobHandle = inputDeps;
            if (frameIndex < __minFrameIndex)
                UnityEngine.Debug.LogError($"Frame Index {frameIndex} Less Than Range {__minFrameIndex}");
            else if (!_AddOrRemoveComponentIfNotSaved(
                frameIndex, 
                ComponentType.ReadWrite<T>(), 
                group, 
                removeComponentCommander, 
                ref jobHandle))
                //jobHandle = endFrameBarrier.RemoveComponent<T>(group, inputDeps);
                EntityManager.RemoveComponent<T>(group);

            return jobHandle;
        }
    }

    protected JobHandle _Schedule<T>(
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
            innerloopBatchCount, 
            dependsOn);
    }

    protected JobHandle _ScheduleSingle<T>(
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
            1,
            dependsOn);
    }

    protected JobHandle _Schedule<T>(
            in T instance,
            int entityCount,
            uint frameIndex,
            in EntityTypeHandle entityType,
            EntityQuery group,
            in JobHandle dependsOn) where T : struct, IRollbackSave
    {
#if ENABLE_PROFILER
        using (__scheduleSave.Auto())
#endif
            return __group.Schedule(
            instance, 
            entityCount, 
            frameIndex, 
            entityType, 
            group, 
            JobHandle.CombineDependencies(saveJobHandle, dependsOn));
    }

    protected JobHandle _Schedule<T>(
        in T instance, 
        uint maxFrameIndex, 
        uint frameIndex, 
        uint frameCount, 
        JobHandle dependsOn) where T : struct, IRollbackClear
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

    protected override void OnCreate()
    {
        base.OnCreate();

        __group = _GetGroup();

        endFrameBarrier = World.GetOrCreateSystem<EndRollbackSystemGroupEntityCommandSystem>();

        __entityIndices = new NativeHashMap<Entity, int>(1, Allocator.Persistent);

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
    }

    protected override void OnDestroy()
    {
        __entityIndices.Dispose();

        base.OnDestroy();
    }

    protected override void _Clear()
    {
        __minFrameIndex = 1;
        __frameIndex = 0;

        base._Clear();
    }

    protected sealed override JobHandle _Restore(JobHandle inputDeps)
    {
        uint frameIndex = rollbackSystemGroup.restoreFrameIndex;
        if (frameIndex > __frameIndex)
        {
#if ENABLE_PROFILER
            using (__save.Auto())
#endif
                return _Save(inputDeps);
        }

#if ENABLE_PROFILER
        using (__restore.Auto())
#endif
            inputDeps = _Restore(frameIndex, inputDeps);

        uint frameCount = __frameIndex - frameIndex;
        if (frameCount > 0)
#if ENABLE_PROFILER
            using (__clear.Auto())
#endif
                inputDeps = _Clear(__frameIndex, frameIndex + 1, frameCount, inputDeps);

        __frameIndex = frameIndex;

        return inputDeps;
    }

    protected sealed override JobHandle _Save(JobHandle inputDeps)
    {
        var frameSyncSystemGroup = rollbackSystemGroup.frameSyncSystemGroup;
        uint frameIndex = frameSyncSystemGroup.frameIndex;

        UnityEngine.Assertions.Assert.IsTrue(frameIndex > __frameIndex);

        if (frameSyncSystemGroup.isClear && frameIndex > __minFrameIndex + maxFrameCount)
        {
            uint minFrameIndex = frameIndex - (maxFrameCount >> 1);
            if (minFrameIndex > __frameIndex)
            {
                if(__frameIndex > 0)
#if ENABLE_PROFILER
                    using (__clear.Auto())
#endif
                        inputDeps = _Clear(__frameIndex, 1, __frameIndex, inputDeps);

                __frameIndex = __minFrameIndex = minFrameIndex;
            }
            else if (minFrameIndex > __minFrameIndex)
            {
#if ENABLE_PROFILER
                using (__clear.Auto())
#endif
                    inputDeps = _Clear(__frameIndex, __minFrameIndex, minFrameIndex - __minFrameIndex, inputDeps);

                __minFrameIndex = minFrameIndex;
            }
        }

        var entityType = GetEntityTypeHandle();
        for (uint i = __frameIndex + 1; i <= frameIndex; ++i)
#if ENABLE_PROFILER
            using (__save.Auto())
#endif
                inputDeps = _Save(i, entityType, inputDeps);

        __frameIndex = frameIndex;

        return inputDeps;
    }

    protected abstract JobHandle _Restore(uint frameIndex, JobHandle inputDeps);

    protected abstract JobHandle _Save(uint frameIndex, EntityTypeHandle entityType, JobHandle inputDeps);

    protected abstract JobHandle _Clear(uint maxFrameIndex, uint frameIndex, uint frameCount, JobHandle inputDeps);
}*/

#if ZG_LEGACY_ROLLBACK
    [DisableAutoCreation, UpdateInGroup(typeof(FrameSyncSystemGroup), OrderFirst = true), UpdateBefore(typeof(RollbackSystemGroup))]
    public partial class BeginRollbackSystemGroupEntityCommandSystem : EntityCommandSystemHybrid
    {
    }
#endif

    [BurstCompile, UpdateInGroup(typeof(RollbackSystemGroup), OrderLast = true)]
    public partial struct EndRollbackSystemGroupEntityCommandSystemGroup : ISystem
    {
        private SystemGroup __systemGroup;

        public void OnCreate(ref SystemState state)
        {
            __systemGroup = state.World.GetOrCreateSystemGroup(typeof(EndRollbackSystemGroupEntityCommandSystemGroup));
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var world = state.WorldUnmanaged;
            __systemGroup.Update(ref world);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(EndRollbackSystemGroupEntityCommandSystemGroup))]
    public partial struct EndRollbackSystemGroupStructChangeSystem : ISystem
    {
        public EntityComponentAssigner assigner
        {
            get;

            private set;
        }

        public EntityCommandStructChangeManager manager
        {
            get;

            private set;
        }

        public EntityAddDataPool addDataCommander => new EntityAddDataPool(manager.addComponentPool, assigner);

        public void OnCreate(ref SystemState state)
        {
            /*state.SetAlwaysUpdateSystem(true);

            __group = state.GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RollbackObject>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });*/

            assigner = new EntityComponentAssigner(Allocator.Persistent);

            manager = new EntityCommandStructChangeManager(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            manager.Dispose();

            assigner.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            manager.Playback(ref state);

            assigner.Playback(ref state);
        }
    }

    /*[UpdateInGroup(typeof(EndRollbackSystemGroupEntityCommandSystemGroup))]
    public partial class EndRollbackSystemGroupEntityCommandSystem : EntityCommandSystem
    {

    }*/

    [BurstCompile, UpdateInGroup(typeof(FrameSyncSystemGroup), OrderFirst = true)]
    public partial struct RollbackSystemGroup : ISystem
    {
        /*private bool __isActive;
        private uint __restoreFrameIndex;
        //private Entity __entity;
        //private EndRollbackSystemGroupEntityCommandSystemGroup __endFrameBarrier;

        public bool isActive
        {
            get
            {
                return __isActive || clearRestoreFrameIndex < __restoreFrameIndex && frameSyncSystemGroup.isClear;
            }
        }

        public uint restoreFrameIndex
        {
            get
            {
                return frameSyncSystemGroup.isClear ? clearRestoreFrameIndex : __restoreFrameIndex;
            }
        }

        public uint clearRestoreFrameIndex
        {
            get;

            private set;
        }

        public FrameSyncSystemGroup frameSyncSystemGroup
        {
            get;

            internal set;
        }*/

        private SystemGroup __sysetmGroup;

        public void OnCreate(ref SystemState state)
        {
            __sysetmGroup = state.World.GetOrCreateSystemGroup(typeof(RollbackSystemGroup));

            //containerManager = new RollbackContainerManager();
        }

        public void OnDestroy(ref SystemState state)
        {
            //containerManager.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var world = state.WorldUnmanaged;
            __sysetmGroup.Update(ref world);
        }

        /*public void Clear()
        {
            __isActive = false;

            __restoreFrameIndex = clearRestoreFrameIndex = 0;

            IRollbackSystem rollbackSystem;
            var systems = Systems;
            foreach (var system in systems)
            {
                rollbackSystem = system as IRollbackSystem;
                if (rollbackSystem != null)
                    rollbackSystem.Clear();
            }

            containerManager.Clear();
        }*/

        /*public void Restore(uint frameIndex)
        {
            if (frameIndex < (__isActive ? __restoreFrameIndex : frameSyncSystemGroup.realFrameIndex + 1))
            {
                if(frameSyncSystemGroup.realFrameIndex - frameIndex > 256)
                    UnityEngine.Debug.LogError("Rollback from " + frameIndex + " to " + (frameSyncSystemGroup.realFrameIndex + 1));

                uint clearRestoreFrameIndex = this.clearRestoreFrameIndex;
                if (clearRestoreFrameIndex > 0)
                {
                    UnityEngine.Assertions.Assert.AreNotEqual(0, frameIndex);

                    this.clearRestoreFrameIndex = math.min(clearRestoreFrameIndex, frameIndex);
                }
                else
                    this.clearRestoreFrameIndex = frameIndex;

                __restoreFrameIndex = frameIndex;

                __isActive = true;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            //__entity = EntityManager.CreateEntity(typeof(RollbackFlag), typeof(RollbackRestoreFrame));

            //__endFrameBarrier = World.GetOrCreateSystem<EndRollbackSystemGroupEntityCommandSystemGroup>();

            //AddSystemToUpdateList(__endFrameBarrier);
        }

        protected override void OnDestroy()
        {
            containerManager.Dispose();

            //EntityManager.DestroyEntity(__entity);

            //__entity = Entity.Null;

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var frameSyncSystemGroup = this.frameSyncSystemGroup;

            base.OnUpdate();
            
            if (frameSyncSystemGroup.isClear)
                clearRestoreFrameIndex = __restoreFrameIndex = 0;
            else if(__isActive)
                __restoreFrameIndex = clearRestoreFrameIndex + 1;

            //if ((frameSyncSystemGroup.flag & FrameSyncSystemGroup.Flag.Rollback) == FrameSyncSystemGroup.Flag.Rollback)
            __isActive = false;
        }*/
    }

#if ZG_LEGACY_ROLLBACK
    [DisableAutoCreation, UpdateInGroup(typeof(FrameSyncSystemGroup), OrderFirst = true), UpdateBefore(typeof(BeginFrameSyncSystemGroupEntityCommandSystem)), UpdateAfter(typeof(RollbackSystemGroup))]
    public partial class RollbackCommandManagedSystem : SystemBase
    {
        private enum EventType
        {
            Invoke = 0x01, 
            Unregister = 0x02, 
            InvokeAndUnregister = 0x03
        }

        private struct MoveCommand
        {
            public long type;
            public uint fromFrameIndex;
            public uint toFrameIndex;
        }

        private struct Event
        {
            public EventType type;
            public CallbackHandle value;
        }

        private struct Callback : IComparable<Callback>
        {
            public long type;
            public CallbackHandle value;

            public int CompareTo(Callback other)
            {
                return type.CompareTo(other.type);
            }
        }

        private struct CallbackEx
        {
            public uint orginFrameIndex;
            public CallbackHandle clear;
            public Callback value;
        }

        [BurstCompile]
        private struct Remove : IJob
        {
            public uint frameIndex;
            public long type;
            public NativeParallelMultiHashMap<uint, CallbackEx> callbacks;
            public NativeList<Event> events;
            
            public void Execute()
            {
                events.Clear();

                if (callbacks.TryGetFirstValue(frameIndex, out var callback, out var iterator))
                {
                    Event result;
                    do
                    {
                        if (callback.value.type == type && callback.orginFrameIndex == 0)
                        {
                            result.type = EventType.Unregister;
                            result.value = callback.value.value;
                            events.Add(result);

                            if (!callback.clear.Equals(CallbackHandle.Null))
                            {
                                result.type = EventType.InvokeAndUnregister;
                                result.value = callback.clear;
                                events.Add(result);
                            }

                            callbacks.Remove(iterator);
                            
                            return;
                        }
                    } while (callbacks.TryGetNextValue(out callback, ref iterator));
                }
            }
        }

        [BurstCompile]
        private struct MoveJob : IJob
        {
            public NativeList<MoveCommand> commands;

            public NativeList<Event> events;

            public NativeParallelMultiHashMap<uint, CallbackEx> callbacks;

            public void Execute(int index)
            {
                var command = commands[index];

                if (callbacks.TryGetFirstValue(command.fromFrameIndex, out var callback, out var iterator))
                {
                    Event result;
                    do
                    {
                        if (callback.orginFrameIndex == 0 && callback.value.type == command.type)
                        {
                            callbacks.Remove(iterator);

                            if (command.toFrameIndex > 0)
                            {
                                callback.orginFrameIndex = command.fromFrameIndex;
                                callbacks.Add(command.toFrameIndex, callback);
                            }
                            else
                            {
                                result.type = EventType.Unregister;
                                result.value = callback.value.value;
                                events.Add(result);

                                if (!callback.clear.Equals(CallbackHandle.Null))
                                {
                                    result.type = EventType.InvokeAndUnregister;
                                    result.value = callback.clear;
                                    events.Add(result);
                                }
                            }

                            return;
                        }
                    } while (callbacks.TryGetNextValue(out callback, ref iterator));
                }

                UnityEngine.Debug.LogError($"Move Fail: Type {command.type} From Frame Index {command.fromFrameIndex} To {command.toFrameIndex}");
            }

            public void Execute()
            {
                events.Clear();

                int length = commands.Length;
                for (int i = 0; i < length; ++i)
                    Execute(i);

                commands.Clear();
            }
        }

        [BurstCompile]
        private struct SortJob : IJob
        {
            public uint frameIndex;

            [ReadOnly]
            public NativeParallelMultiHashMap<uint, CallbackEx> inputs;

            public NativeList<Callback> outputs; 

            public void Execute()
            {
                outputs.Clear();

                if (inputs.TryGetFirstValue(frameIndex, out var callback, out var iterator))
                {
                    do
                    {
                        outputs.Add(callback.value);
                    } while (inputs.TryGetNextValue(out callback, ref iterator));
                }

                outputs.Sort();
            }
        }

        [BurstCompile]
        private struct ClearJob : IJob
        {
            public uint minFrameIndex;
            
            public NativeParallelMultiHashMap<uint, CallbackEx> inputs;

            public NativeList<Event> outputs;

            public void Execute()
            {
                using (var keys = inputs.GetKeyArray(Allocator.Temp))
                {
                    NativeParallelMultiHashMapIterator<uint> iterator;
                    CallbackEx value;
                    Event result;
                    uint key;
                    int length = keys.ConvertToUniqueArray();
                    for (int i = 0; i < length; ++i)
                    {
                        key = keys[i];
                        if (key < minFrameIndex)
                        {
                            if (inputs.TryGetFirstValue(key, out value, out iterator))
                            {
                                do
                                {
                                    result.type = EventType.Unregister;
                                    result.value = value.value.value;
                                    outputs.Add(result);

                                    if (!value.clear.Equals(CallbackHandle.Null))
                                    {
                                        result.type = EventType.InvokeAndUnregister;
                                        result.value = value.clear;
                                        outputs.Add(result);
                                    }
                                } while (inputs.TryGetNextValue(out value, ref iterator));

                                inputs.Remove(key);
                            }
                        }
                    }
                }
            }
        }

        /*private class Frame
        {
            public struct Handler
            {
                public Action value;
                public Action clear;
            }

            public event Action onClear;

            private Dictionary<long, Handler> __sources;
            private SortedList<long, Handler> __destinations;

            public void Set(long type, Action value, Action clear)
            {
                if (__sources != null && __sources.TryGetValue(type, out var handler))
                {
                    if (handler.clear != null)
                    {
                        onClear -= handler.clear;

                        handler.clear();
                    }
#if DEBUG
                    UnityEngine.Debug.LogWarning("Replace type: " + type);
#endif
                    handler.value = value;
                    handler.clear = clear;

                    __sources[type] = handler;

                    if (clear != null)
                        onClear += clear;

                    return;
                }

                if (__destinations == null)
                    __destinations = new SortedList<long, Handler>();

                if (__destinations.TryGetValue(type, out handler))
                {
                    if (handler.clear != null)
                    {
                        onClear -= handler.clear;

                        handler.clear();
                    }
#if DEBUG
                    else

                        UnityEngine.Debug.LogWarning("Replace type: " + type);
#endif
                }

                handler.value = value;
                handler.clear = clear;

                __destinations[type] = handler;
                
                if (clear != null)
                    onClear += clear;
            }

            public void Add(long type, Handler value)
            {
                if (__destinations == null)
                    __destinations = new SortedList<long, Handler>();
                
                if (__destinations.TryGetValue(type, out var temp))
                {
                    if (__sources == null)
                        __sources = new Dictionary<long, Handler>();

                    __sources.Add(type, temp);
                }

                if (value.clear != null)
                    onClear += value.clear;
                
                __destinations[type] = value;
            }

            public bool Remove(long type, out Handler value)
            {
                if (__sources != null && __sources.TryGetValue(type, out value))
                {
                    if (value.clear != null)
                        onClear -= value.clear;

                    __sources.Remove(type);

                    return true;
                }

                if (__destinations != null && __destinations.TryGetValue(type, out value))
                {
                    if (value.clear != null)
                        onClear -= value.clear;

                    __destinations.Remove(type);
                    
                    return true;
                }

                value = default;

                return false;
            }

            public void Invoke()
            {
                foreach (var handler in __destinations.Values)
                    handler.value();
            }

            public void Clear()
            {
                if (onClear != null)
                {
                    onClear();

                    onClear = null;
                }
                
                if (__sources != null)
                    __sources.Clear();

                if (__destinations != null)
                    __destinations.Clear();
            }
        }*/
        
        public uint maxFrameCount = 256;
        
        private NativeList<MoveCommand> __moveCommands;
        private NativeList<Event> __events;
        private NativeList<Callback> __callbacks;
        private NativeParallelMultiHashMap<uint, CallbackEx> __frameCallbacks;

        public FrameSyncSystemGroup frameSyncSystemGroup
        {
            get;

            internal set;
        }

        public void Invoke(uint frameIndex, long type, Action value, Action clear = null)
        {
            Remove remove;
            remove.frameIndex = frameIndex;
            remove.type = type;
            remove.callbacks = __frameCallbacks;
            remove.events = __events;
            remove.Run();

            __Invoke();

            CallbackEx callback;
            callback.orginFrameIndex = 0;
            callback.clear = clear == null ? CallbackHandle.Null : clear.Register();
            callback.value.type = type;
            callback.value.value = value.Register();
            __frameCallbacks.Add(frameIndex, callback);
            
            frameSyncSystemGroup.Restore(frameIndex);
        }

        public void Move(long type, uint fromFrameIndex, uint toFrameIndex)
        {
            MoveCommand moveCommand;
            moveCommand.type = type;
            moveCommand.fromFrameIndex = fromFrameIndex;
            moveCommand.toFrameIndex = toFrameIndex;
            __moveCommands.Add(moveCommand);
            
            frameSyncSystemGroup.Restore(toFrameIndex == 0 ? fromFrameIndex : math.min(fromFrameIndex, toFrameIndex));
        }

        public void Clear()
        {
            __moveCommands.Clear();

            __Clear();
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            __moveCommands = new NativeList<MoveCommand>(Allocator.Persistent);
            __events = new NativeList<Event>(Allocator.Persistent);
            __callbacks = new NativeList<Callback>(Allocator.Persistent);
            __frameCallbacks = new NativeParallelMultiHashMap<uint, CallbackEx>((int)maxFrameCount, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            //__Clear();
            foreach (var temp in __events)
                temp.value.Unregister();

            __moveCommands.Dispose();
            __events.Dispose();
            __callbacks.Dispose();
            __frameCallbacks.Dispose();
        }

        protected override void OnUpdate()
        {
            MoveJob moveJob;
            moveJob.commands = __moveCommands;
            moveJob.events = __events;
            moveJob.callbacks = __frameCallbacks;
            moveJob.Run();

            var frameSyncSystemGroup = this.frameSyncSystemGroup;
            uint frameIndex = frameSyncSystemGroup.frameIndex;

            SortJob sortJob;
            sortJob.frameIndex = frameIndex;
            sortJob.inputs = __frameCallbacks;
            sortJob.outputs = __callbacks;
            sortJob.Run();

            bool result;
            foreach (var callback in __callbacks)
            {
                result = callback.value.Invoke();
                UnityEngine.Assertions.Assert.IsTrue(result);
            }

            if (frameSyncSystemGroup.isClear)
            {
                uint realFrameIndex = frameSyncSystemGroup.realFrameIndex;
                if (realFrameIndex == frameIndex && realFrameIndex > maxFrameCount)
                    __Clear(realFrameIndex - maxFrameCount);
            }

            __Invoke();

            //UpdateSharedSystemGroup();
        }

        private void __Clear(uint minFrameIndex)
        {
            ClearJob clearJob;
            clearJob.minFrameIndex = minFrameIndex;
            clearJob.inputs = __frameCallbacks;
            clearJob.outputs = __events;
            clearJob.Run();
        }

        private void __Clear()
        {
            __events.Clear();

            __Clear(uint.MaxValue);

            UnityEngine.Assertions.Assert.AreEqual(0, __frameCallbacks.Count());

            __Invoke();
        }

        private void __Invoke()
        {
            bool result;
            foreach (var temp in __events)
            {
                result = false;
                switch (temp.type)
                {
                    case EventType.Unregister:
                        result = temp.value.Unregister();
                        break;
                    case EventType.InvokeAndUnregister:
                        result = temp.value.InvokeAndUnregister();
                        break;
                }
                
                UnityEngine.Assertions.Assert.IsTrue(result);
            }
        }
        
        /*private SortedList<uint, Frame> __frames;
        private List<Frame> __pool;
        
        public void Invoke(uint frameIndex, long type, Action value, Action clear = null)
        {
            if (__frames == null)
                __frames = new SortedList<uint, Frame>();

            if(!__frames.TryGetValue(frameIndex, out var frame))
            {
                frame = __CreateFrame();

                __frames[frameIndex] = frame;
            }

            //result.Add(type, handler);
            frame.Set(type, value, clear);

            frameSyncSystemGroup.Restore(frameIndex);
        }

        public bool Move(long type, uint fromFrameIndex, uint toFrameIndex)
        {
            if(!__frames.TryGetValue(fromFrameIndex, out var frame))
            {
#if DEBUG
                UnityEngine.Debug.LogError("Move Fail: Type " + type + " From Frame Index " + fromFrameIndex + " To " + toFrameIndex + ", now: " + frameSyncSystemGroup.realFrameIndex);
#endif
                return false;
            }
            
            if(!frame.Remove(type, out var value))
            {
#if DEBUG
                UnityEngine.Debug.LogError("Move Fail: Type " + type + " From Frame Index " + fromFrameIndex + " To " + toFrameIndex + ", now: " + frameSyncSystemGroup.realFrameIndex);
#endif
                return false;
            }

            if (toFrameIndex > 0)
            {
                if (!__frames.TryGetValue(toFrameIndex, out frame))
                {
                    frame = __CreateFrame();

                    __frames[toFrameIndex] = frame;
                }

                frame.Add(type, value);
            }
            else
                UnityEngine.Debug.LogError("Discard " + type + " Frame Index " + fromFrameIndex);

            //Invoke(toFrameIndex, type, action);

            frameSyncSystemGroup.Restore(fromFrameIndex);

            return true;
        }

        public void Clear()
        {
            if (__frames == null)
                return;

            foreach (var frame in __frames.Values)
            {
                frame.Clear();
                
                if (__pool == null)
                    __pool = new List<Frame>();

                __pool.Add(frame);
            }

            __frames.Clear();
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            onUpdate += __Update;
        }

        private void __Update()
        {
            if (__frames != null)
            {
                uint frameIndex = frameSyncSystemGroup.frameIndex;
                if (__frames.TryGetValue(frameIndex, out var handler))
                    handler.Invoke();

                if ((frameSyncSystemGroup.flag & FrameSyncSystemGroup.Flag.Rollback) == FrameSyncSystemGroup.Flag.Rollback)
                {
                    uint realFrameIndex = frameSyncSystemGroup.realFrameIndex;
                    if (realFrameIndex == frameIndex)
                    {
                        Frame frame;
                        uint minFrameIndex;
                        int count = __frames.Count;
                        while (count-- > 0)
                        {
                            minFrameIndex = __frames.Keys[0];
                            if (minFrameIndex + maxFrameCount < realFrameIndex)
                            {
                                frame = __frames.Values[0];
                                frame.Clear();

                                __frames.RemoveAt(0);

                                if (__pool == null)
                                    __pool = new List<Frame>();

                                __pool.Add(frame);
                            }
                            else
                                break;
                        }
                    }
                }
            }
        }

        private Frame __CreateFrame()
        {
            int count = __pool == null ? 0 : __pool.Count;
            if (count < 1)
                return new Frame();

            Frame frame = __pool[--count];
            __pool.RemoveAt(count);

            return frame;
        }*/
    }

    /*public static partial class RollbackUtility
    {
        public static bool InvokeDiff<T>(this RollbackSystem.ComponentRestoreFunction<T> function, int entityIndex, Entity entity, out T value) where T : struct, IEquatable<T>, IComponentData
        {
            value = function[entityIndex]; ;
            if (value.Equals(function[entity]))
                return false;

            function[entity] = value;

            return true;
        }

        public static bool InvokeDiff<T>(this RollbackSystem.ComponentRestoreFunction<T> function, int entityIndex, Entity entity) where T : struct, IEquatable<T>, IComponentData
        {
            return InvokeDiff(function, entityIndex, entity, out var value);
        }

    }*/
#endif
}