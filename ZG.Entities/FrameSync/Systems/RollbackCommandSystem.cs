using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using ZG.Unsafe;

namespace ZG
{
    public enum RollbackFrameIndexType
    {
        Min, 
        Restore, 
        Command, 

        Count
    }

    [Flags]
    public enum RollbackEntryType
    {
        Init = 0x01,
        Restore = 0x02, 
        //InitAndRestore = 0x03, 
        Local = 0x04
    }

    public interface IRollbackEntryTester
    {
        //�����������ж�̬�����Ƿ���ײ
        bool Test(uint frameIndex, in Entity entity);
    }

    public struct RollbackEntryKey : IComparable<RollbackEntryKey>
    {
        public uint frameIndex;
        public int entryIndex;

        public int CompareTo(RollbackEntryKey other)
        {
            if (frameIndex == other.frameIndex)
                return entryIndex - other.entryIndex;

            return (int)(frameIndex - other.frameIndex);
        }
    }

    public struct RollbackEntry
    {
        public RollbackEntryType type;
        public long key;
        public Entity entity;
    }

    public struct RollbackObject : IComponentData
    {

    }

    /*[BurstCompile]
    public struct RollbackEntryTest<T> : IJob where T : struct, IRollbackEntryTester
    {
        public NativeArray<uint> frameIndices;

        public NativeList<RollbackEntry> values;

        public NativeList<RollbackEntryKey> keys;

        public NativeHashMap<uint, RollbackEntryType> frameEntryTypes;

        public NativeParallelMultiHashMap<uint, RollbackEntry> frameEntries;

        public T tester;

        public void Execute()
        {
            int numKeys = keys.Length;
            if (numKeys < 1)
                return;

            NativeParallelMultiHashMapIterator<uint> iterator;
            RollbackEntry entry;
            RollbackEntryType entryType;
            uint restoreFrameIndex = frameIndices[(int)RollbackFrameIndexType.Restore],
                       minFrameIndex = math.max(frameIndices[(int)RollbackFrameIndexType.Min], 1),
                       i;
            for (int j = 0; j < numKeys; ++j)
            {
                ref var key = ref keys.ElementAt(j);
                if (key.frameIndex >= minFrameIndex)
                {
                    ref var value = ref values.ElementAt(key.entryIndex);

                    if (frameEntries.TryGetFirstValue(key.frameIndex, out entry, out iterator))
                    {
                        do
                        {
                            if (value.key == entry.key)
                            {
                                frameEntries.Remove(iterator);

                                break;
                            }
                        } while (frameEntries.TryGetNextValue(out entry, ref iterator));
                    }

                    frameEntries.Add(key.frameIndex, value);

                    if (!frameEntryTypes.TryGetValue(key.frameIndex, out entryType) || entryType < value.type)
                        frameEntryTypes[key.frameIndex] = value.type;
                }
            }

            keys.AsArray().Sort();

            uint maxFrameIndex = math.min(restoreFrameIndex, keys[numKeys - 1].frameIndex + 1);
            //int i, numDirtyFrames = dirtyFrames.Length;
            for (i = math.max(keys[0].frameIndex, minFrameIndex); i < maxFrameIndex; ++i)
            {
                if (frameEntryTypes.TryGetValue(i, out entryType) && entryType == RollbackEntryType.Restore)
                {
                    restoreFrameIndex = i;

                    break;
                }
            }

            for (int j = 0; j < numKeys; ++j)
            {
                ref var key = ref keys.ElementAt(j);
                if (key.frameIndex >= restoreFrameIndex)
                    break;

                if (key.frameIndex >= minFrameIndex)
                {
                    ref var value = ref values.ElementAt(key.entryIndex);

                    for (i = key.frameIndex; i < restoreFrameIndex; ++i)
                    {
                        if (tester.Test(i, value.entity))
                        {
                            restoreFrameIndex = i;

                            frameEntryTypes[i] = RollbackEntryType.Restore;

                            break;
                        }
                    }
                }
            }

            values.Clear();
            keys.Clear();

            uint commandFrameIndex = restoreFrameIndex;
            if (restoreFrameIndex > minFrameIndex)
            {
                for (i = restoreFrameIndex - 1; i >= minFrameIndex; --i)
                {
                    if (frameEntryTypes.TryGetValue(i, out entryType))
                    {
                        if (entryType == RollbackEntryType.Restore)
                            break;

                        commandFrameIndex = i;
                    }
                }
            }

            frameIndices[(int)RollbackFrameIndexType.Restore] = restoreFrameIndex;
            frameIndices[(int)RollbackFrameIndexType.Command] = commandFrameIndex;
        }
    }*/


    [BurstCompile]
    public struct RollbackEntryTest<T> : IJobParalledForDeferBurstSchedulable where T : struct, IRollbackEntryTester
    {
        [ReadOnly]
        public NativeArray<RollbackEntry> values;

        [ReadOnly]
        public NativeArray<RollbackEntryKey> keys;

        [NativeDisableParallelForRestriction]
        public NativeArray<uint> frameIndices;

        public T tester;

        public static void CompareExchange(ref NativeArray<int> values, int destination)
        {
            int source, temp = values[(int)RollbackFrameIndexType.Restore];
            do
            {
                source = temp;
                if (source <= destination)
                    break;

                temp = System.Threading.Interlocked.CompareExchange(ref values.ElementAt((int)RollbackFrameIndexType.Restore), destination, source);
            }
            while (temp != source);
        }

        public void Execute(int index)
        {
            var frameIndices = this.frameIndices.Reinterpret<int>();

            var key = keys[index];
            var value = values[key.entryIndex];
            for (uint i = math.max(key.frameIndex, this.frameIndices[(int)RollbackFrameIndexType.Min]); i < this.frameIndices[(int)RollbackFrameIndexType.Restore]; ++i)
            {
                if (tester.Test(i, value.entity))
                {
                    //restoreFrameIndex = i;

                    CompareExchange(ref frameIndices, (int)i);

                    break;
                }
            }
        }
    }

    public struct RollbackCommander
    {
        /*private struct MoveCommand
        {
            public struct ArrayWrapper : IReadOnlyListWrapper<uint, NativeArray<MoveCommand>>
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public int GetCount(NativeArray<MoveCommand> list) => list.Length;

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public uint Get(NativeArray<MoveCommand> list, int index) => list[index].maxFrameIndex;
            }

            public uint minFrameIndex;
            public uint maxFrameIndex;
        }*/

        [BurstCompile]
        private struct Init : IJob
        {
            public NativeArray<uint> frameIndices;

            public NativeList<RollbackEntry> values;

            public NativeList<RollbackEntryKey> keys;

            public NativeParallelHashMap<uint, RollbackEntryType> frameEntryTypes;

            public NativeParallelMultiHashMap<uint, RollbackEntry> frameEntries;

            public void Execute()
            {
                int numKeys = keys.Length;
                if (numKeys < 1)
                    return;

                NativeParallelMultiHashMapIterator<uint> iterator;
                RollbackEntry entry;
                RollbackEntryType entryType;
                uint minFrameIndex = frameIndices[(int)RollbackFrameIndexType.Min],
                           i;
                for (int j = 0; j < numKeys; ++j)
                {
                    ref var key = ref keys.ElementAt(j);
                    if (key.frameIndex >= minFrameIndex)
                    {
                        ref var value = ref values.ElementAt(key.entryIndex);

                        if (frameEntries.TryGetFirstValue(key.frameIndex, out entry, out iterator))
                        {
                            do
                            {
                                if (value.key == entry.key)
                                {
                                    frameEntries.Remove(iterator);

                                    break;
                                }
                            } while (frameEntries.TryGetNextValue(out entry, ref iterator));
                        }

                        frameEntries.Add(key.frameIndex, value);

                        if (!frameEntryTypes.TryGetValue(key.frameIndex, out entryType) || entryType < value.type)
                            frameEntryTypes[key.frameIndex] = value.type;
                    }
                }

                keys.AsArray().Sort();

                minFrameIndex = math.max(minFrameIndex, keys[0].frameIndex);
                uint commandFrameIndex = frameIndices[(int)RollbackFrameIndexType.Command];
                if (commandFrameIndex > minFrameIndex)
                    frameIndices[(int)RollbackFrameIndexType.Command] = minFrameIndex;
                else
                    minFrameIndex = commandFrameIndex;

                uint maxFrameIndex = frameIndices[(int)RollbackFrameIndexType.Restore];// math.min(restoreFrameIndex, keys[numKeys - 1].frameIndex + 1);
                for (i = minFrameIndex; i < maxFrameIndex; ++i)
                {
                    if (frameEntryTypes.TryGetValue(i, out entryType) && (entryType & RollbackEntryType.Restore) == RollbackEntryType.Restore)
                    {
                        frameIndices[(int)RollbackFrameIndexType.Restore] = i;

                        break;
                    }
                }
            }
        }

        [BurstCompile]
        private struct Command : IJob
        {
            public uint minRestoreFrameIndex;

            /*[ReadOnly]
            public NativeArray<MoveCommand> moveCommands;*/

            public NativeArray<uint> frameIndices;

            public NativeList<RollbackEntry> values;

            public NativeList<RollbackEntryKey> keys;

            public NativeParallelHashMap<uint, RollbackEntryType> frameEntryTypes;

            public void Execute()
            {
                if (keys.IsEmpty)
                    return;

                values.Clear();
                keys.Clear();

                uint restoreFrameIndex = frameIndices[(int)RollbackFrameIndexType.Restore];
                if (frameEntryTypes.TryGetValue(restoreFrameIndex, out var entryType))
                    entryType |= RollbackEntryType.Restore;
                else
                    entryType = RollbackEntryType.Restore;

                frameEntryTypes[restoreFrameIndex] = entryType;

                if (minRestoreFrameIndex > restoreFrameIndex)
                {
                    /*uint frameIndex = minRestoreFrameIndex;
                    //��ѷ�ʽ�������ƶ�ǰ��֡��׼�ȣ�����֡�����ͬ��
                    int index = moveCommands.BinarySearch(frameIndex, new Comparer<uint>(), new MoveCommand.ArrayWrapper()), numMoveCommands = moveCommands.Length;
                    if (index >= 0 && index < numMoveCommands)
                    {
                        var moveCommand = moveCommands[index];

                        if (moveCommand.maxFrameIndex == frameIndex)
                            frameIndex = moveCommand.minFrameIndex - 1;
                        else if(++index < numMoveCommands)
                        {
                            moveCommand = moveCommands[index];
                            if (moveCommand.minFrameIndex <= frameIndex)
                                frameIndex = moveCommand.minFrameIndex - 1;
                        }

                        frameIndex = math.max(frameIndex, restoreFrameIndex);
                    }*/

                    uint frameIndex = restoreFrameIndex;
                    for (uint i = frameIndex; i < minRestoreFrameIndex; ++i)
                    {
                        if (frameEntryTypes.TryGetValue(i, out entryType))
                        {
                            if ((entryType & RollbackEntryType.Local) == RollbackEntryType.Local)
                            {
                                frameIndex = i;

                                break;
                            }
                        }
                    }

                    if (frameIndex > restoreFrameIndex)
                    {
                        UnityEngine.Debug.LogWarning($"Restore Frame Index Out Of Range: {restoreFrameIndex} To {frameIndex}");

                        frameIndices[(int)RollbackFrameIndexType.Restore] = frameIndex;

                        restoreFrameIndex = frameIndex;
                    }
                }

                uint commandFrameIndex = restoreFrameIndex, minFrameIndex = math.max(frameIndices[(int)RollbackFrameIndexType.Min], 1);
                if (commandFrameIndex > minFrameIndex)
                {
                    for (uint i = commandFrameIndex - 1; i >= minFrameIndex; --i)
                    {
                        if (frameEntryTypes.TryGetValue(i, out entryType))
                        {
                            if ((entryType & RollbackEntryType.Restore) == RollbackEntryType.Restore)
                                break;

                            commandFrameIndex = i;
                        }
                    }
                }

                //ʹ�����ϵ�Command��ֹInit��ʧ��ע�⣺�����п��ܻḲ��Restore����ʱ�޷�����
                frameIndices[(int)RollbackFrameIndexType.Command] = math.min(frameIndices[(int)RollbackFrameIndexType.Command], commandFrameIndex);
            }
        }

        public struct Writer
        {
            private NativeArray<uint> __frameIndices;
            private NativeList<RollbackEntry> __values;
            private NativeList<RollbackEntryKey> __keys;
            //private NativeList<MoveCommand> __moveCommands;

            private NativeParallelHashMap<uint, RollbackEntryType> __frameEntryTypes;
            private NativeParallelMultiHashMap<uint, RollbackEntry> __frameEntries;

            public uint minFrameIndex => __frameIndices[(int)RollbackFrameIndexType.Min];

            internal Writer(ref RollbackCommander commander)
            {
                __frameIndices = commander.__frameIndices;
                __values = commander.__values;
                __keys = commander.__keys;
                //__moveCommands = commander.__moveCommands;
                __frameEntryTypes = commander.__frameEntryTypes;
                __frameEntries = commander.__frameEntries;
            }

            public void Clear(uint frameIndex)
            {
                /*int index = __moveCommands.AsArray().BinarySearch(frameIndex, new Comparer<uint>(), new MoveCommand.ArrayWrapper());
                if (index >= 0 && index < __moveCommands.Length && __moveCommands[index].maxFrameIndex < frameIndex)
                    ++index;

                if (index > 0)
                    __moveCommands.RemoveRange(0, index);*/

                int numDirtyFrames = __keys.Length;
                for(int i = 0; i < numDirtyFrames; ++i)
                {
                    if(__keys[i].frameIndex < frameIndex)
                    {
                        __keys.RemoveAtSwapBack(i--);

                        --numDirtyFrames;
                    }
                }

                for (uint i = __frameIndices[(int)RollbackFrameIndexType.Min]; i < frameIndex; ++i)
                {
                    __frameEntryTypes.Remove(i);

                    __frameEntries.Remove(i);
                }

                __frameIndices[(int)RollbackFrameIndexType.Min] = frameIndex;
            }

            public void Move(long key, uint fromFrameIndex, uint toFrameIndex)
            {
                if (fromFrameIndex > 0 || toFrameIndex > 0)
                {
                    /*if (fromFrameIndex > 0 && toFrameIndex > 0)
                    {
                        uint minFrameIndex = math.min(fromFrameIndex, toFrameIndex), maxFrameIndex = math.max(fromFrameIndex, toFrameIndex);
                        if (maxFrameIndex - minFrameIndex > 2)
                            __Move(minFrameIndex + 1, maxFrameIndex - 1);
                    }*/

                    if (__frameEntries.TryGetFirstValue(fromFrameIndex, out var entry, out var iterator))
                    {
                        do
                        {
                            if (entry.key == key)
                            {
                                __frameEntries.Remove(iterator);

                                RollbackEntryKey entryKey;
                                entryKey.entryIndex = __values.Length;

                                if (fromFrameIndex > 0)
                                {
                                    entryKey.frameIndex = fromFrameIndex;
                                    __keys.Add(entryKey);
                                }

                                if (toFrameIndex > 0)
                                {
                                    entryKey.frameIndex = toFrameIndex;
                                    __keys.Add(entryKey);
                                }

                                __values.Add(entry);
                                //__frameEntries.Add(toFrameIndex, entry);

                                return;
                            }

                        } while (__frameEntries.TryGetNextValue(out entry, ref iterator));
                    }
                }
            }

            /*private void __Move(uint minFrameIndex, uint maxFrameIndex)
            {
                MoveCommand moveCommand;
                if (__moveCommands.IsEmpty)
                {
                    moveCommand.minFrameIndex = minFrameIndex;
                    moveCommand.maxFrameIndex = maxFrameIndex;

                    __moveCommands.Add(moveCommand);
                }
                else
                {
                    var moveCommandArray = __moveCommands.AsArray();
                    int index = moveCommandArray.BinarySearch(minFrameIndex, new Comparer<uint>(), new MoveCommand.ArrayWrapper()),
                        numMoveCommands = __moveCommands.Length;

                    int target = index + 1;
                    for (int i = target; i < numMoveCommands; ++i)
                    {
                        moveCommand = __moveCommands[i];
                        if (moveCommand.minFrameIndex > maxFrameIndex)
                        {
                            __moveCommands.RemoveRange(target, i - target);

                            break;
                        }
                    }

                    if (index >= 0 && index < __moveCommands.Length)
                    {
                        moveCommand = __moveCommands[index];
                        if (moveCommand.maxFrameIndex < minFrameIndex)
                        {
                            moveCommand.minFrameIndex = minFrameIndex;
                            moveCommand.maxFrameIndex = maxFrameIndex;

                            if (++index == numMoveCommands)
                                __moveCommands.Add(moveCommand);
                            else
                            {
                                __moveCommands.InsertRangeWithBeginEnd(index, index + 1);

                                __moveCommands[index] = moveCommand;
                            }
                        }
                        else
                        {
                            moveCommand.minFrameIndex = math.min(moveCommand.minFrameIndex, minFrameIndex);
                            moveCommand.maxFrameIndex = math.max(moveCommand.maxFrameIndex, maxFrameIndex);
                            __moveCommands[index] = moveCommand;
                        }
                    }
                    else
                    {
                        moveCommand = __moveCommands[0];
                        if (moveCommand.minFrameIndex > maxFrameIndex)
                        {
                            __moveCommands.InsertRangeWithBeginEnd(0, 1);

                            moveCommand.minFrameIndex = minFrameIndex;
                            moveCommand.maxFrameIndex = maxFrameIndex;
                            __moveCommands[0] = moveCommand;
                        }
                        else
                        {
                            moveCommand.minFrameIndex = math.min(moveCommand.minFrameIndex, minFrameIndex);
                            moveCommand.maxFrameIndex = math.max(moveCommand.maxFrameIndex, maxFrameIndex);
                            __moveCommands[0] = moveCommand;
                        }
                    }
                }
            }*/
        }

        private NativeArrayLite<JobHandle> __jobHandle;
        private NativeArrayLite<uint> __frameIndices;
        private NativeListLite<RollbackEntry> __values;
        private NativeListLite<RollbackEntryKey> __keys;
        //private NativeListLite<MoveCommand> __moveCommands;
        private NativeHashMapLite<uint, RollbackEntryType> __frameEntryTypes;
        private NativeMultiHashMapLite<uint, RollbackEntry> __frameEntries;

        public JobHandle jobHandle
        {
            get
            {
                return __jobHandle[0];
            }

            set
            {
                __jobHandle[0] = value;
            }
        }

        public uint restoreFrameIndex
        {
            get
            {
                CompleteDependency();

                return __frameIndices[(int)RollbackFrameIndexType.Restore];
            }

            private set
            {
                __frameIndices[(int)RollbackFrameIndexType.Restore] = value;
            }
        }

        public uint commandFrameIndex
        {
            get
            {
                CompleteDependency();

                return __frameIndices[(int)RollbackFrameIndexType.Command];
            }

            
            set
            {
                CompleteDependency();

                __frameIndices[(int)RollbackFrameIndexType.Command] = value;

                __frameIndices[(int)RollbackFrameIndexType.Restore] = math.max(__frameIndices[(int)RollbackFrameIndexType.Restore], value);
            }
        }

        public Writer writer
        {
            get
            {
                return new Writer(ref this);
            }
        }

        public RollbackCommander(Allocator allocator)
        {
            BurstUtility.InitializeJob<Init>();
            BurstUtility.InitializeJob<Command>();

            __jobHandle = new NativeArrayLite<JobHandle>(1, allocator, NativeArrayOptions.ClearMemory);
            __frameIndices = new NativeArrayLite<uint>((int)RollbackFrameIndexType.Count, allocator, NativeArrayOptions.ClearMemory);
            __values = new NativeListLite<RollbackEntry>(allocator);
            __keys = new NativeListLite<RollbackEntryKey>(allocator);
            //__moveCommands = new NativeListLite<MoveCommand>(allocator);
            __frameEntryTypes = new NativeHashMapLite<uint, RollbackEntryType>(1, allocator);
            __frameEntries = new NativeMultiHashMapLite<uint, RollbackEntry>(1, allocator);
        }

        public void Dispose()
        {
            CompleteDependency();

            __jobHandle.Dispose();
            __frameIndices.Dispose();
            __values.Dispose();
            __keys.Dispose();
            //__moveCommands.Dispose();
            __frameEntryTypes.Dispose();
            __frameEntries.Dispose();
        }

        public void Clear()
        {
            CompleteDependency();

            for (int i = 0; i < (int)RollbackFrameIndexType.Count; ++i)
                __frameIndices[i] = 0;

            __values.Clear();
            __keys.Clear();

            //__moveCommands.Clear();

            __frameEntryTypes.Clear();
            __frameEntries.Clear();
        }

        /*public void Clear(uint frameIndex)
        {
            CompleteDependency();

            var writer = this.writer;

            for (uint i = __frameIndices[(int)RollbackFrameIndexType.Min]; i < frameIndex; ++i)
                writer.Free(i);

            __frameIndices[(int)RollbackFrameIndexType.Min] = frameIndex;
        }*/

        public void Record(uint frameIndex, in RollbackEntry entry)
        {
            CompleteDependency();

            RollbackEntryKey key;
            key.frameIndex = frameIndex;
            key.entryIndex = __values.Length;
            __keys.Add(key);

            __values.Add(entry);
        }

        public void CompleteDependency()
        {
            __jobHandle[0].Complete();
            __jobHandle[0] = default;
        }

        public JobHandle Test<T>(/*uint frameIndex, */T tester, uint minRestoreFrameIndex, int innerloopBatchCount, in JobHandle inputDeps) where T : struct, IRollbackEntryTester
        {
            __jobHandle[0].Complete();

            Init init;
            init.frameIndices = __frameIndices;
            init.values = __values;
            init.keys = __keys;
            init.frameEntryTypes = __frameEntryTypes;
            init.frameEntries = __frameEntries;

            var jobHandle = init.Schedule(inputDeps);

            //restoreFrameIndex = frameIndex;

            RollbackEntryTest<T> test;
            test.frameIndices = __frameIndices;
            test.values = __values.AsDeferredJobArray();
            test.keys = __keys.AsDeferredJobArray();
            test.tester = tester;

            jobHandle = test.ScheduleParallel((NativeList<RollbackEntryKey>)__keys, innerloopBatchCount, jobHandle);

            Command command;
            command.minRestoreFrameIndex = minRestoreFrameIndex;
            command.frameIndices = __frameIndices;
            command.values = __values;
            command.keys = __keys;
            //command.moveCommands = __moveCommands.AsDeferredJobArray();
            command.frameEntryTypes = __frameEntryTypes;

            jobHandle = command.Schedule(jobHandle);

            __jobHandle[0] = jobHandle;

            return jobHandle;
        }
    }

    public struct RollbackCommanderManaged
    {
        private struct InvokeCommand
        {
            public RollbackEntryType rollbackEntryType;

            public uint frameIndex;

            public long key;
            public EntityCommander value;

            public CallbackHandle clear;
        }

        private struct MoveCommand
        {
            public long key;
            public uint fromFrameIndex;
            public uint toFrameIndex;
        }

        private struct Callback : IComparable<Callback>
        {
            public RollbackEntryType rollbackEntryType;
            public int index;
            public long key;
            public EntityCommander value;

            public int CompareTo(Callback other)
            {
                int result = key.CompareTo(other.key);
                if (result == 0 && index != other.index)
                {
                    result = index.CompareTo(other.index);
                    if(result == 0 && rollbackEntryType != other.rollbackEntryType)
                        return ((int)rollbackEntryType).CompareTo((int)other.rollbackEntryType);
                }

                return result;
            }
        }

        private struct CallbackEx
        {
            public uint orginFrameIndex;
            public CallbackHandle clear;
            public Callback value;
        }

        private struct Frame
        {
            public EntityCommander commander;
            //任何指令都不能丢弃
            //public EntityCommander initCommander;
        }

        private struct CommanderPool
        {
            public struct Writer
            {
                private NativeList<EntityCommander> __pool;
                private NativeList<EntityCommander> __poolBuffer;
                private NativeParallelHashMap<uint, Frame> __frames;

                public Writer(ref CommanderPool commanderPool)
                {
                    __pool = commanderPool.__pool;
                    __poolBuffer = commanderPool.__poolBuffer;
                    __frames = commanderPool.__frames;
                }

                public void Clear(uint frameStartIndex, uint frameCount)
                {
                    uint index;
                    Frame frame;
                    for (uint i = 0; i < frameCount; ++i)
                    {
                        index = i + frameStartIndex;
                        if (__frames.TryGetValue(index, out frame))
                        {
                            Free(frame.commander);
                            //Free(frame.initCommander);

                            __frames.Remove(index);
                        }
                    }
                }

                public EntityCommander Alloc()
                {
                    EntityCommander commander;
                    int length = __pool.Length;
                    UnityEngine.Assertions.Assert.IsTrue(length > 0);
                    //if (length > 0)
                    {
                        commander = __pool[--length];

                        //commander.writer.Clear();

                        __pool.ResizeUninitialized(length);
                    }
                    /*else
                        commander = new EntityCommander(Allocator.Persistent);*/

                    return commander;
                }

                public Frame Alloc(uint frameIndex)
                {
                    if (!__frames.TryGetValue(frameIndex, out var frame))
                    {
                        frame.commander = Alloc();
                        //frame.initCommander = Alloc();

                        __frames[frameIndex] = frame;
                    }

                    return frame;
                }

                public bool Free(uint frameIndex)
                {
                    if (__frames.TryGetValue(frameIndex, out var frame))
                    {
                        Free(frame.commander);
                        //Free(frame.initCommander);

                        __frames.Remove(frameIndex);

                        return true;
                    }

                    return false;
                }

                public void Free(EntityCommander commander)
                {
                    //commander.writer.Clear();

                    __poolBuffer.Add(commander);
                }
            }

            private NativeListLite<EntityCommander> __pool;
            private NativeListLite<EntityCommander> __poolBuffer;
            private NativeHashMapLite<uint, Frame> __frames;

            public CommanderPool(Allocator allocator)
            {
                __frames = new NativeHashMapLite<uint, Frame>(1, allocator);

                __pool = new NativeListLite<EntityCommander>(allocator);

                __poolBuffer = new NativeListLite<EntityCommander>(allocator);
            }

            public EntityCommander Alloc()
            {
                return AsWriter(1).Alloc();
            }

            public void Free(EntityCommander commander)
            {
                commander.Clear();

                __pool.Add(commander);
            }

            public void Dispose()
            {
                Frame frame;
                var enumerator = __frames.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    frame = enumerator.Current.Value;
                    frame.commander.Dispose();
                    //frame.initCommander.Dispose();
                }

                __frames.Dispose();

                int length = __pool.Length;
                for (int i = 0; i < length; ++i)
                    __pool[i].Dispose();

                __pool.Dispose();

                length = __poolBuffer.Length;
                for (int i = 0; i < length; ++i)
                    __poolBuffer[i].Dispose();

                __poolBuffer.Dispose();
            }

            public void Clear()
            {
                Frame frame;
                var enumerator = __frames.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    frame = enumerator.Current.Value;

                    Free(frame.commander);
                    //Free(frame.initCommander);
                }

                __frames.Clear();
            }

            public Writer AsWriter(int capacity)
            {
                int length = __poolBuffer.Length;
                for (int i = 0; i < length; ++i)
                    __poolBuffer[i].Clear();

                __pool.AddRange(__poolBuffer.AsArray());

                __poolBuffer.Clear();

                for (int i = __pool.Length; i < capacity; ++i)
                    __pool.Add(new EntityCommander(Allocator.Persistent));

                return new Writer(ref this);
            }

            public bool Apply(bool isInitOnly, uint frameIndex, ref SystemState systemState)
            {
                if (__frames.TryGetValue(frameIndex, out var frame))
                {
                    /*if (isInitOnly)
                        return frame.initCommander.Apply(group, ref systemState);*/

                    return frame.commander.Apply(ref systemState);
                }

                return false;
            }
        }

        [BurstCompile]
        private struct Command : IJob
        {
            public uint minFrameIndex;

            public NativeList<InvokeCommand> invokeCommands;
            public NativeList<MoveCommand> moveCommands;
            public NativeList<CallbackHandle> callbackHandles;
            public NativeParallelMultiHashMap<uint, CallbackEx> frameCallbacks;

            public CommanderPool.Writer commanderPool;

            public RollbackCommander.Writer commander;

            public void Remove(
                uint frameIndex, 
                long key, 
                ref NativeList<uint> dirtyFrameIndices)
            {
                if (frameCallbacks.TryGetFirstValue(frameIndex, out var callback, out var iterator))
                {
                    do
                    {
                        if (callback.value.key == key && callback.orginFrameIndex == 0)
                        {
                            __MaskDirty(frameIndex, ref dirtyFrameIndices);

                            commanderPool.Free(callback.value.value);

                            if (!callback.clear.Equals(CallbackHandle.Null))
                                callbackHandles.Add(callback.clear);

                            /*callback.orginFrameIndex = frameIndex;
                            callback.Clear(ref events);*/

                            frameCallbacks.Remove(iterator);

                            break;
                        }
                    } while (frameCallbacks.TryGetNextValue(out callback, ref iterator));
                }
            }

            public void Move(
                int index, 
                ref NativeList<uint> dirtyFrameIndices)
            {
                ref readonly var command = ref moveCommands.ElementAt(index);

                commander.Move(command.key, command.fromFrameIndex, command.toFrameIndex);

                if (frameCallbacks.TryGetFirstValue(command.fromFrameIndex, out var callback, out var iterator))
                {
                    do
                    {
                        if (callback.orginFrameIndex == 0 && callback.value.key == command.key)
                        {
                            frameCallbacks.Remove(iterator);

                            __MaskDirty(command.fromFrameIndex, ref dirtyFrameIndices);

                            //callback.orginFrameIndex = command.fromFrameIndex;

                            if (command.toFrameIndex > 0)
                            {
                                callback.orginFrameIndex = command.fromFrameIndex;

                                frameCallbacks.Add(command.toFrameIndex, callback);

                                __MaskDirty(command.toFrameIndex, ref dirtyFrameIndices);
                            }
                            else
                            {
                                commanderPool.Free(callback.value.value);

                                if (!callback.clear.Equals(CallbackHandle.Null))
                                    callbackHandles.Add(callback.clear);
                            }
                            //callback.Clear(ref events);

                            return;
                        }
                    } while (frameCallbacks.TryGetNextValue(out callback, ref iterator));
                }

                UnityEngine.Debug.LogError($"Move Fail: Type {command.key} From Frame Index {command.fromFrameIndex} To {command.toFrameIndex}");
            }

            public void Execute()
            {
                //events.Clear();

                NativeList<uint> dirtyFrameIndices = default;

                CallbackEx callback;
                int numInvokeCommands = invokeCommands.Length;
                if (numInvokeCommands > 0)
                {
                    for (int i = 0; i < numInvokeCommands; ++i)
                    {
                        ref readonly var invokeCommand = ref invokeCommands.ElementAt(i);

                        Remove(
                            invokeCommand.frameIndex, 
                            invokeCommand.key, 
                            ref dirtyFrameIndices);

                        callback.orginFrameIndex = 0;
                        callback.value.rollbackEntryType = invokeCommand.rollbackEntryType;
                        callback.value.index = frameCallbacks.CountValuesForKey(invokeCommand.frameIndex);
                        callback.value.key = invokeCommand.key;
                        callback.value.value = invokeCommand.value;
                        callback.clear = invokeCommand.clear;
                        frameCallbacks.Add(invokeCommand.frameIndex, callback);

                        __MaskDirty(invokeCommand.frameIndex, ref dirtyFrameIndices);
                    }

                    invokeCommands.Clear();
                }

                int numMoveCommands = moveCommands.Length;
                if (numMoveCommands > 0)
                {
                    for (int i = 0; i < numMoveCommands; ++i)
                        Move(i, ref dirtyFrameIndices);

                    moveCommands.Clear();
                }

                NativeParallelMultiHashMapIterator<uint> iterator;
                if (dirtyFrameIndices.IsCreated)
                {
                    var callbacks = new NativeList<Callback>(Allocator.Temp);
                    EntityCommander.ReadOnly commander;
                    Frame frame;
                    uint dirtyFrameIndex;
                    int i, j, numCallbacks, numDirtyFrameIndices = dirtyFrameIndices.Length;
                    for (i = 0; i < numDirtyFrameIndices; ++i)
                    {
                        dirtyFrameIndex = dirtyFrameIndices[i];

                        commanderPool.Free(dirtyFrameIndex);

                        if (frameCallbacks.TryGetFirstValue(dirtyFrameIndex, out callback, out iterator))
                        {
                            callbacks.Clear();

                            do
                            {
                                callbacks.Add(callback.value);
                            } while (frameCallbacks.TryGetNextValue(out callback, ref iterator));

                            callbacks.Sort();

                            frame = commanderPool.Alloc(dirtyFrameIndex);

                            //UnityEngine.Assertions.Assert.IsTrue(frameCommander.jobHandle.IsCompleted);

                            numCallbacks = callbacks.Length;
                            for (j = 0; j < numCallbacks; ++j)
                            {
                                //UnityEngine.Assertions.Assert.IsTrue(callbacks.ElementAt(j).value.jobHandle.IsCompleted);
                                ref var callbackEntry = ref callbacks.ElementAt(j);
                                commander = callbackEntry.value.AsReadOnly();
                                commander.AppendTo(ref frame.commander);

                                /*if ((callbackEntry.rollbackEntryType & RollbackEntryType.Init) == RollbackEntryType.Init)
                                    commander.AppendTo(ref frame.initCommander);*/
                            }
                        }
                    }

                    callbacks.Dispose();

                    dirtyFrameIndices.Dispose();
                }

                uint minFrameIndex = commander.minFrameIndex;
                if (minFrameIndex < this.minFrameIndex)
                {
                    for (uint i = minFrameIndex; i < this.minFrameIndex; ++i)
                    {
                        if (frameCallbacks.TryGetFirstValue(i, out callback, out iterator))
                        {
                            do
                            {
                                commanderPool.Free(callback.value.value);

                                if (!callback.clear.Equals(CallbackHandle.Null))
                                    callbackHandles.Add(callback.clear);

                            } while (frameCallbacks.TryGetNextValue(out callback, ref iterator));

                            frameCallbacks.Remove(i);
                        }
                    }

                    commanderPool.Clear(minFrameIndex, this.minFrameIndex - minFrameIndex);

                    commander.Clear(this.minFrameIndex);
                }

                /*if(callbacks.IsCreated)
                {
                    using(var keyValueArrays = callbacks.GetKeyValueArrays(Allocator.Temp))
                    {
                        Callback callback;
                        int numCallbacks = keyValueArrays.Length;
                        for (int i = 0; i < numCallbacks; ++i)
                        {
                            callback = keyValueArrays.Values[i];

                            result.frameIndex = keyValueArrays.Keys[i];
                            result.commandType = callback.commandType;
                            result.value = callback.value.value;
                            events.Add(result);
                        }
                    }

                    callbacks.Dispose();
                }*/
            }

            public void __MaskDirty(uint frameIndex, ref NativeList<uint> dirtyFrameIndices)
            {
                if (dirtyFrameIndices.IsCreated)
                {
                    if (dirtyFrameIndices.IndexOf(frameIndex) != -1)
                        return;
                }
                else
                    dirtyFrameIndices = new NativeList<uint>(Allocator.Temp);

                dirtyFrameIndices.Add(frameIndex);
            }
        }

        private NativeArrayLite<JobHandle> __jobHandles;
        private NativeListLite<CallbackHandle> __callbackHandles;
        private NativeListLite<InvokeCommand> __invokeCommands;
        private NativeListLite<MoveCommand> __moveCommands;
        private NativeMultiHashMapLite<uint, CallbackEx> __frameCallbacks;
        private CommanderPool __commanderPool;
        private RollbackCommander __commander;

        public bool isCreated => __jobHandles.isCreated;

        public uint restoreFrameIndex => __commander.restoreFrameIndex;

        public JobHandle playbackJobHandle
        {
            get => __jobHandles[0];

            private set => __jobHandles[0] = value;
        }

        public JobHandle updateJobHandle
        {
            get => __jobHandles[1];

            private set => __jobHandles[1] = value;
        }

        public RollbackCommanderManaged(Allocator allocator)
        {
            BurstUtility.InitializeJob<Command>();

            __jobHandles = new NativeArrayLite<JobHandle>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            __callbackHandles = new NativeListLite<CallbackHandle>(allocator);
            __invokeCommands = new NativeListLite<InvokeCommand>(allocator);
            __moveCommands = new NativeListLite<MoveCommand>(allocator);
            __frameCallbacks = new NativeMultiHashMapLite<uint, CallbackEx>(1, allocator);
            __commander = new RollbackCommander(allocator);
            __commanderPool = new CommanderPool(allocator);
        }

        public void Dispose()
        {
            playbackJobHandle.Complete();
            updateJobHandle.Complete();

            foreach (var frameCallback in (NativeParallelMultiHashMap<uint, CallbackEx>)__frameCallbacks)
                frameCallback.Value.value.value.Dispose();

            __jobHandles.Dispose();
            __callbackHandles.Dispose();
            __invokeCommands.Dispose();
            __moveCommands.Dispose();
            __frameCallbacks.Dispose();
            __commander.Dispose();
            __commanderPool.Dispose();
        }

        public void Clear()
        {
            CompleteUpdateDependency();

            foreach (var frameCallback in (NativeParallelMultiHashMap<uint, CallbackEx>)__frameCallbacks)
                __commanderPool.Free(frameCallback.Value.value.value);

            __callbackHandles.Clear();
            __invokeCommands.Clear();
            __moveCommands.Clear();
            __frameCallbacks.Clear();
            __commander.Clear();
            __commanderPool.Clear();
        }

        public void CompleteUpdateDependency()
        {
            updateJobHandle.Complete();
            updateJobHandle = default;
        }

        public void InvokeAll()
        {
            CompleteUpdateDependency();

            int length = __callbackHandles.Length;
            for (int i = 0; i < length; ++i)
                __callbackHandles[i].InvokeAndUnregister();

            __callbackHandles.Clear();
        }

        public EntityCommander Invoke(uint frameIndex, in RollbackEntry entry, Action clear = null)
        {
            __commander.Record(frameIndex, entry);

            CompleteUpdateDependency();

            var frameCommander = __commanderPool.Alloc();

            InvokeCommand invokeCommand;
            invokeCommand.rollbackEntryType = entry.type;
            invokeCommand.frameIndex = frameIndex;
            invokeCommand.key = entry.key;
            invokeCommand.value = frameCommander;
            invokeCommand.clear = clear == null ? CallbackHandle.Null : clear.Register();

            __invokeCommands.Add(invokeCommand);

            /*CallbackEx callback;
            callback.orginFrameIndex = 0;
            callback.clear = clear == null ? CallbackHandle.Null : clear.Register();
            callback.value.key = entry.key;
            callback.value.value = frameCommander;
            __frameCallbacks.Add(frameIndex, callback);*/

            return frameCommander;
        }

        public void Move(long key, uint fromFrameIndex, uint toFrameIndex)
        {
            CompleteUpdateDependency();

            MoveCommand moveCommand;
            moveCommand.key = key;
            moveCommand.fromFrameIndex = fromFrameIndex;
            moveCommand.toFrameIndex = toFrameIndex;
            __moveCommands.Add(moveCommand);
        }

        public bool Playback(uint frameIndex, ref SystemState systemState)
        {
            systemState.Dependency = JobHandle.CombineDependencies(systemState.Dependency, updateJobHandle);

            playbackJobHandle.Complete();

            uint restoreFrameIndex = __commander.restoreFrameIndex;
            if (restoreFrameIndex == 0 || restoreFrameIndex == frameIndex)
            {
                bool result = false;
                for (uint i = __commander.commandFrameIndex; i <= frameIndex; ++i)
                {
                    if (__commanderPool.Apply(i < restoreFrameIndex, i, ref systemState))
                    {
                        //UnityEngine.Debug.Log($"{systemState.World.Name} Do {i}");

                        //JobHandle.ScheduleBatchedJobs();

                        result = true;
                    }
                }

                /*if (systemState.World.Name.Contains("Client"))
                    UnityEngine.Debug.Log($"Command Frame Index {frameIndex + 1}");*/

                playbackJobHandle = result ? systemState.Dependency : default;

                __commander.commandFrameIndex = frameIndex + 1;

                return result;
            }

            if(__commanderPool.Apply(false, frameIndex, ref systemState))
            {
                //UnityEngine.Debug.Log($"{systemState.World.Name} Do_ {frameIndex}");

                playbackJobHandle = systemState.Dependency;

                return true;
            }

            return false;
        }

        public JobHandle Update(uint minFrameIndex, in JobHandle inputDeps)
        {
            updateJobHandle.Complete();

            Command command;
            command.minFrameIndex = minFrameIndex;
            command.invokeCommands = __invokeCommands;
            command.moveCommands = __moveCommands;
            command.callbackHandles = __callbackHandles;
            command.frameCallbacks = __frameCallbacks;
            command.commanderPool = __commanderPool.AsWriter((__invokeCommands.Length + (__moveCommands.Length << 1)) << 1);
            command.commander = __commander.writer;

            var jobHandle = JobHandle.CombineDependencies(__commander.jobHandle, playbackJobHandle, inputDeps);

            jobHandle = command.Schedule(jobHandle);

            updateJobHandle = jobHandle;

            return jobHandle;
        }

        public JobHandle Test<T>(T tester, uint minRestoreFrameIndex, int innerloopBatchCount, in JobHandle inputDeps) where T : struct, IRollbackEntryTester => 
            __commander.Test(tester, minRestoreFrameIndex, innerloopBatchCount, JobHandle.CombineDependencies(updateJobHandle, inputDeps));
    }

    [BurstCompile, UpdateInGroup(typeof(RollbackSystemGroup), OrderLast = true), UpdateAfter(typeof(EndRollbackSystemGroupEntityCommandSystemGroup))]
    public partial struct RollbackCommandSystem : ISystem
    {
        private EntityQuery __frameGroup;
        //private EntityQuery __group;

        public RollbackCommanderManaged commander
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            __frameGroup = RollbackFrame.GetEntityQuery(ref state);

            /*__group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<RollbackObject>()
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });*/

            commander = new RollbackCommanderManaged(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            commander.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            commander.Playback(__frameGroup.GetSingleton<RollbackFrame>().index, ref state);
        }
    }
}