using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using ZG;

[assembly: RegisterGenericJobType(typeof(RollbackEntryTest<SyncFrameEventSystem.RollbackEntryTester>))]

namespace ZG
{
    [BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))/*, UpdateAfter(typeof(BeginFrameEntityCommandSystem))*/]
#if !USING_NETCODE
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
#endif
    public partial struct SyncFrameCallbackSystem : ISystem
    {
        private struct Comparer : IComparer<uint>
        {
            public int Compare(uint x, uint y)
            {
                return x.CompareTo(y);
            }
        }

        private struct ListWrapper : IReadOnlyListWrapper<uint, NativeArray<SyncFrame>>
        {
            public int GetCount(NativeArray<SyncFrame> list) => list.Length;

            public uint Get(NativeArray<SyncFrame> list, int index) => list[index].sourceIndex;
        }

        private struct Call
        {
            public struct FrameArrayWrapper : IReadOnlyListWrapper<int, DynamicBuffer<SyncFrameArray>>
            {
                public int GetCount(DynamicBuffer<SyncFrameArray> list) => list.Length;

                public int Get(DynamicBuffer<SyncFrameArray> list, int index) => list[index].type;
            }

            public uint frameIndex;
            public uint maxFrameCount;
            [ReadOnly]
            public NativeArray<Entity> entityArray;
            //[ReadOnly]
            public BufferAccessor<SyncFrameCallback> frameCallbacks;
            public BufferAccessor<SyncFrameEvent> frameEvents;
            public BufferAccessor<SyncFrame> frames;
            public BufferAccessor<SyncFrameArray> frameArrays;

            //public EntityCommandQueue<Entity>.ParallelWriter entityManager;

            public static void AppendFrame(ref DynamicBuffer<SyncFrameArray> frameArrays, int startIndex, int endIndex)
            {
                SyncFrameArray frameArray;
                for (int i = startIndex; i <= endIndex; ++i)
                {
                    frameArray = frameArrays[i];

                    ++frameArray.startIndex;

                    frameArrays[i] = frameArray;
                }
            }

            public bool Execute(int index)
            {
                var frames = this.frames[index];
                var frameArrays = this.frameArrays[index];
                SyncFrame frame;
                SyncFrameArray frameArray;
                int numFrameArrays = frameArrays.Length, offset = 0, i, j;
                for (i = 0; i < numFrameArrays; ++i)
                {
                    frameArray = frameArrays[i];
                    frameArray.startIndex -= offset;
                    for (j = frameArray.count - 1; j >= 0; --j)
                    {
                        frame = frames[frameArray.startIndex + j];
                        if (frame.destinationIndex <= this.frameIndex)
                        {
                            frameArray.oldSourceIndex = frame.sourceIndex;
                            frameArray.oldDestinationIndex = frame.destinationIndex;

                            break;
                        }
                    }

                    if (j >= 0)
                    {
                        ++j;

                        frames.RemoveRange(frameArray.startIndex, j);
                        
                        frameArray.count -= j;

                        offset += j;
                    }
                    else if (offset < 1)
                        continue;
                    
#if DEBUG
                    if(i < numFrameArrays - 1)
                        UnityEngine.Assertions.Assert.AreEqual(frameArrays[i + 1].startIndex - offset, frameArray.startIndex + frameArray.count);
                    else
                        UnityEngine.Assertions.Assert.AreEqual(frames.Length, frameArray.startIndex + frameArray.count);
#endif

                    frameArrays[i] = frameArray;
                }

                var frameEvents = this.frameEvents[index];
                frameEvents.Clear();

                var frameCallbacks = this.frameCallbacks[index];
                SyncFrameCallback frameCallback;
                SyncFrameEvent frameEvent;
                Comparer comparer;
                ListWrapper listWrapper;
                uint frameIndex;
                int k, numFrameCallbacks = frameCallbacks.Length;
                for (i = 0; i < numFrameCallbacks; ++i)
                {
                    frameCallback = frameCallbacks[i];
                    frameIndex = frameCallback.frameIndex;
                    for (j = 0; j < numFrameArrays; ++j)
                    {
                        frameArray = frameArrays[j];
                        if (frameArray.type == frameCallback.type)
                        {
                            //frame = default;
                            k = frames.AsNativeArray().GetSubArray(frameArray.startIndex, frameArray.count).BinarySearch(
                                frameCallback.frameIndex, 
                                comparer, 
                                listWrapper) + 1;
                            /*/TODO: Bin
                            for (k = 0; k < frameArray.count; ++k)
                            {
                                frame = frames[frameArray.startIndex + k];
                                if (frame.sourceIndex > frameCallback.frameIndex)
                                    break;
                            }*/

                            if (k < frameArray.count)
                            {
                                k += frameArray.startIndex;

                                frame = frames[k];

                                frameIndex = frameCallback.frameIndex + frame.destinationIndex - frame.sourceIndex;
                                if(frameIndex <= (k > frameArray.startIndex ? frames[k - 1].destinationIndex : frameArray.oldDestinationIndex))
                                {
                                    UnityEngine.Debug.Log($"Discard Callback: {frameCallback.frameIndex} : {this.frameIndex}");

                                    frameIndex = 0;

                                    break;
                                }

                                UnityEngine.Assertions.Assert.IsTrue(math.max(frameIndex, this.frameIndex) < frame.destinationIndex);
                            }
                            else
                            {
                                if (frameArray.count > 0)
                                {
                                    frame = frames[frameArray.startIndex + frameArray.count - 1];
                                    /*frameIndex = frameCallback.frameIndex + frame.destinationIndex - frame.sourceIndex;
                                    frameIndex = math.min(frameIndex, frame.destinationIndex + maxFrameCount);
                                    frameIndex = math.max(frameIndex, frame.destinationIndex + 1);*/
                                    frameIndex = math.max(frameCallback.frameIndex, frame.destinationIndex + 1);
                                }
                                else
                                {
                                    /*frameIndex = frameCallback.frameIndex + frameArray.oldDestinationIndex - frameArray.oldSourceIndex;
                                    frameIndex = math.min(frameIndex, frameArray.oldDestinationIndex + maxFrameCount);
                                    frameIndex = math.max(frameIndex, frameArray.oldDestinationIndex + 1);*/
                                    frameIndex = math.max(frameCallback.frameIndex, frameArray.oldDestinationIndex + 1);
                                }

                                /*if(frameCallback.frameIndex < this.frameIndex + (maxFrameCount >> 1))
                                    frameIndex = math.max(frameIndex, frameCallback.frameIndex);*/
                            }

                            frameIndex = math.max(frameIndex, this.frameIndex);

                            if (frameIndex > this.frameIndex + maxFrameCount)
                            {
                                UnityEngine.Debug.Log($"Discard Callback: {frameCallback.frameIndex} : {frameIndex} : {this.frameIndex}");

                                frameIndex = 0;
                            }
                            else
                            {
                                frame.sourceIndex = frameCallback.frameIndex;
                                frame.destinationIndex = frameIndex;

                                frames.Insert(k + frameArray.startIndex, frame);

                                ++frameArray.count;

                                frameArrays[j] = frameArray;

                                AppendFrame(ref frameArrays, j + 1, numFrameArrays - 1);
/*#if DEBUG
                                offset = frameArray.startIndex + frameArray.count;
#endif
                                for (k = j + 1; k < numFrameArrays; ++k)
                                {
                                    frameArray = frameArrays[k];
                                    ++frameArray.startIndex;
#if DEBUG
                                    UnityEngine.Assertions.Assert.AreEqual(offset, frameArray.startIndex);
                                    offset += frameArray.count;
#endif
                                    frameArrays[k] = frameArray;
                                }

#if DEBUG
                                UnityEngine.Assertions.Assert.AreEqual(offset, frames.Length);
#endif*/
                            }

                            break;
                        }
                    }
                    
                    if (j == numFrameArrays)
                    {
                        frameIndex = math.max(frameIndex, this.frameIndex);

                        k = frameArrays.BinarySearch(frameCallback.type, default(Comparer<int>), default(FrameArrayWrapper));
                        if (k < 0)
                            frameArray.startIndex = 0;
                        else
                        {
                            frameArray = frameArrays[k];
                            frameArray.startIndex = frameArray.startIndex + frameArray.count;
                        }

                        frame.sourceIndex = frameCallback.frameIndex;
                        frame.destinationIndex = frameIndex;
                        frames.Insert(frameArray.startIndex, frame);

                        frameArray.type = frameCallback.type;
                        frameArray.count = 1;
                        frameArray.oldSourceIndex = 0;
                        frameArray.oldDestinationIndex = 0;

                        frameArrays.Insert(++k, frameArray);
                        
                        AppendFrame(ref frameArrays, ++k, numFrameArrays++);

                        //++numFrameArrays;

                        /*#if DEBUG
                                                offset = frameArray.startIndex + frameArray.count;
                        #endif
                                                for (k = k + 1; k < numFrameArrays; ++k)
                                                {
                                                    frameArray = frameArrays[k];
                                                    ++frameArray.startIndex;
                        #if DEBUG
                                                    UnityEngine.Assertions.Assert.AreEqual(offset, frameArray.startIndex);
                                                    offset += frameArray.count;
                        #endif
                                                    frameArrays[k] = frameArray;
                                                }

                        #if DEBUG
                                                UnityEngine.Assertions.Assert.AreEqual(offset, frames.Length);
                        #endif*/
                    }

                    frameEvent.type = frameCallback.type;
                    frameEvent.callbackFrameIndex = this.frameIndex;
                    frameEvent.sourceFrameIndex = frameCallback.frameIndex;
                    frameEvent.destinationFrameIndex = frameIndex;
                    frameEvent.handle = frameCallback.handle;

                    frameEvents.Add(frameEvent);
                }

                frameCallbacks.Clear();

                if (!frameEvents.IsEmpty)
                {
                    frameEvents.AsNativeArray().Sort();

                    //entityManager.Enqueue(entityArray[index]);

                    return true;
                }

                return false;
            }
        }

        [BurstCompile]
        private struct CallEx : IJobChunk, IEntityCommandProducerJob
        {
            public uint frameIndex;
            public uint maxFrameCount;
            [ReadOnly]
            public EntityTypeHandle entityType;
            //[ReadOnly]
            public BufferTypeHandle<SyncFrameCallback> frameCallbackType;
            public BufferTypeHandle<SyncFrameEvent> frameEventType;
            public BufferTypeHandle<SyncFrame> frameType;
            public BufferTypeHandle<SyncFrameArray> frameArrayType;

            //public EntityCommandQueue<Entity>.ParallelWriter entityManager;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Call call;
                call.frameIndex = frameIndex;
                call.maxFrameCount = maxFrameCount;
                call.entityArray = chunk.GetNativeArray(entityType);
                call.frameCallbacks = chunk.GetBufferAccessor(ref frameCallbackType);
                call.frameEvents = chunk.GetBufferAccessor(ref frameEventType);
                call.frames = chunk.GetBufferAccessor(ref frameType);
                call.frameArrays = chunk.GetBufferAccessor(ref frameArrayType);
                //call.entityManager = entityManager;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    if(call.Execute(i))
                        chunk.SetComponentEnabled(ref frameEventType, i, true);

                    chunk.SetComponentEnabled(ref frameCallbackType, i, false);
                }
            }
        }

        /*[BurstCompile]
        private struct Clear : IJobChunk
        {
            public BufferTypeHandle<SyncFrameCallback> callbackType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var callbacks = chunk.GetBufferAccessor(ref callbackType);
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    callbacks[i].Clear();
            }
        }*/

        public const uint MAX_FRAME_COUNT = 4;

        private EntityQuery __rollbackFrameGroup;
        private EntityQuery __group;

        private EntityTypeHandle __entityType;
        private BufferTypeHandle<SyncFrameCallback> __frameCallbackType;
        private BufferTypeHandle<SyncFrameEvent> __frameEventType;
        private BufferTypeHandle<SyncFrame> __frameType;
        private BufferTypeHandle<SyncFrameArray> __frameArrayType;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __rollbackFrameGroup = RollbackFrame.GetEntityQuery(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAllRW<SyncFrameCallback>()
                    .WithAllRW<SyncFrame, SyncFrameArray>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            __entityType = state.GetEntityTypeHandle();
            __frameCallbackType = state.GetBufferTypeHandle<SyncFrameCallback>();
            __frameEventType = state.GetBufferTypeHandle<SyncFrameEvent>();
            __frameType = state.GetBufferTypeHandle<SyncFrame>();
            __frameArrayType = state.GetBufferTypeHandle<SyncFrameArray>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            CallEx call;
            call.frameIndex = __rollbackFrameGroup.GetSingleton<RollbackFrame>().index + 1;
            call.maxFrameCount = MAX_FRAME_COUNT;
            call.entityType = __entityType.UpdateAsRef(ref state);
            call.frameCallbackType = __frameCallbackType.UpdateAsRef(ref state);
            call.frameEventType = __frameEventType.UpdateAsRef(ref state);
            call.frameType = __frameType.UpdateAsRef(ref state);
            call.frameArrayType = __frameArrayType.UpdateAsRef(ref state);
            var jobHandle = call.ScheduleParallelByRef(__group, state.Dependency);

            state.Dependency = jobHandle;
        }
    }

    [UpdateInGroup(typeof(TimeSystemGroup), OrderFirst = true)]
    public partial struct SyncFrameClearSystem : ISystem
    {
        private struct Callback
        {
            public BufferAccessor<SyncFrameCallback> frameCallbacks;

            public void Execute(int index)
            {
                var frameCallbacks = this.frameCallbacks[index];
                int numFrameCallbacks = frameCallbacks.Length;//, destination = source;
                for (int i = 0; i < numFrameCallbacks; ++i)
                    frameCallbacks.ElementAt(i).handle.InvokeAndUnregister(default);

                frameCallbacks.Clear();
            }
        }

        private struct CallbackEx : IJobChunk
        {
            public BufferTypeHandle<SyncFrameCallback> frameCallbackType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Callback callback;
                callback.frameCallbacks = chunk.GetBufferAccessor(ref frameCallbackType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    callback.Execute(i);

                    chunk.SetComponentEnabled(ref frameCallbackType, i, false);
                }
            }
        }

        private EntityQuery __group;

        private BufferTypeHandle<SyncFrameCallback> __frameCallbackType;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAllRW<SyncFrameCallback>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            __frameCallbackType = state.GetBufferTypeHandle<SyncFrameCallback>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        public void OnUpdate(ref SystemState state)
        {
            CallbackEx callback;
            callback.frameCallbackType = __frameCallbackType.UpdateAsRef(ref state);
            callback.RunByRefWithoutJobs(__group);

            __group.SetEnabledBitsOnAllChunks<SyncFrameCallback>(false);
        }
    }

    [AlwaysSynchronizeSystem, 
        CreateAfter(typeof(RollbackCommandSystem)), 
        UpdateInGroup(typeof(TimeSystemGroup), OrderFirst = true)]
#if !USING_NETCODE
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
#endif
    public partial class SyncFrameEventSystem : SystemBase
    {
        public struct RollbackEntryTester : IRollbackEntryTester
        {
            public bool Test(uint frameIndex, in Entity entity)
            {
                return true;
            }
        }

        private struct Callback
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;
            [ReadOnly]
            public BufferAccessor<SyncFrameEvent> frameEvents;

            public RollbackCommanderManaged commander;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];
                var frameEvents = this.frameEvents[index];
                SyncFrameEvent frameEvent;
                RollbackEntry entry;
                SyncFrameCallbackData callbackData;
                int numFrameEvents = frameEvents.Length;//, destination = source;
                for (int i = 0; i < numFrameEvents; ++i)
                {
                    frameEvent = frameEvents[i];
                    /*if (frameEvent.destinationFrameIndex > frameIndex)
                        continue;*/

                    if (frameEvent.destinationFrameIndex == 0)
                        frameEvent.handle.Unregister();
                    else
                    {
                        //UnityEngine.Assertions.Assert.AreEqual(frameIndex, frameEvent.destinationFrameIndex);

                        entry.type = RollbackEntryType.Init;
                        entry.key = frameEvent.type;
                        entry.entity = entity;
                        callbackData.frameIndex = frameEvent.destinationFrameIndex;
                        callbackData.commander = commander.Invoke(frameEvent.destinationFrameIndex, entry);

                        //UnityEngine.Debug.Log($"Invoke {frameEvent.sourceFrameIndex} : {frameEvent.destinationFrameIndex}");

                        frameEvent.handle.InvokeAndUnregister(callbackData);
                    }

                    //frameEvents[i--] = frameEvents[--destination];
                }
            }
        }

        private struct CallbackEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public BufferTypeHandle<SyncFrameEvent> eventType;

            public RollbackCommanderManaged commander;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Callback callback;
                callback.entityArray = chunk.GetNativeArray(entityType);
                callback.frameEvents = chunk.GetBufferAccessor(ref eventType);
                callback.commander = commander;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    callback.Execute(i);
            }
        }

        private EntityQuery __rollbackFrameGroup;

        private EntityQuery __group;

        private RollbackCommanderManaged __commander;

        private EntityTypeHandle __entityType;
        private BufferTypeHandle<SyncFrameEvent> __eventType;

        /*private EntityCommandPool<Entity>.Context __context;

        public EntityCommandPool<Entity> pool => __context.pool;

        public SyncFrameEventSystem()
        { 
            __context = new EntityCommandPool<Entity>.Context(Allocator.Persistent);
        }*/

        protected override void OnCreate()
        {
            base.OnCreate();

            //BurstUtility.InitializeJobParalledForDefer<RollbackEntryTest<RollbackEntryTester>>();

            __rollbackFrameGroup = RollbackFrame.GetEntityQuery(ref this.GetState());

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<SyncFrameEvent>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(this);

            __commander = World.GetExistingSystemUnmanaged<RollbackCommandSystem>().commander;

            __entityType = GetEntityTypeHandle();
            __eventType = GetBufferTypeHandle<SyncFrameEvent>(true);
        }

        /*protected override void OnDestroy()
        {
            __context.Dispose();

            base.OnDestroy();
        }*/

        protected override void OnUpdate()
        {
            if (__group.IsEmpty)
                return;

            ref var state = ref this.GetState();

            CallbackEx callback;
            callback.entityType = __entityType.UpdateAsRef(ref state);
            callback.eventType = __eventType.UpdateAsRef(ref state);
            callback.commander = __commander;
            callback.RunByRefWithoutJobs(__group);

            __group.SetEnabledBitsOnAllChunks<SyncFrameEvent>(false);

            uint frameIndex = __rollbackFrameGroup.GetSingleton<RollbackFrame>().index + 1;
            var jobHandle = __commander.Update(frameIndex, Dependency);

            RollbackEntryTester tester;
            Dependency = __commander.Test(tester, frameIndex, 1, jobHandle);
        }
    }
}