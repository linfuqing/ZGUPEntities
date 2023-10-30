using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using ZG.Unsafe;
using static ZG.EntityComponentAssigner;
using System.Diagnostics;

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

    public struct RollbackObject : IComponentData, IEnableableComponent
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
    public struct RollbackEntryTest<T> : IJobParallelForDefer where T : struct, IRollbackEntryTester
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

            public NativeHashMap<uint, RollbackEntryType> frameEntryTypes;

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

            public NativeHashMap<uint, RollbackEntryType> frameEntryTypes;

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

            private NativeHashMap<uint, RollbackEntryType> __frameEntryTypes;
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

        private NativeArray<JobHandle> __jobHandle;
        private NativeArray<uint> __frameIndices;
        private NativeList<RollbackEntry> __values;
        private NativeList<RollbackEntryKey> __keys;
        //private NativeList<MoveCommand> __moveCommands;
        private NativeHashMap<uint, RollbackEntryType> __frameEntryTypes;
        private NativeParallelMultiHashMap<uint, RollbackEntry> __frameEntries;

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

            __jobHandle = new NativeArray<JobHandle>(1, allocator, NativeArrayOptions.ClearMemory);
            __frameIndices = new NativeArray<uint>((int)RollbackFrameIndexType.Count, allocator, NativeArrayOptions.UninitializedMemory);
            __frameIndices[(int)RollbackFrameIndexType.Min] = 0; 
            __frameIndices[(int)RollbackFrameIndexType.Restore] = 1;
            __frameIndices[(int)RollbackFrameIndexType.Command] = 0;

            __values = new NativeList<RollbackEntry>(allocator);
            __keys = new NativeList<RollbackEntryKey>(allocator);
            //__moveCommands = new NativeListLite<MoveCommand>(allocator);
            __frameEntryTypes = new NativeHashMap<uint, RollbackEntryType>(1, allocator);
            __frameEntries = new NativeParallelMultiHashMap<uint, RollbackEntry>(1, allocator);
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

            __frameIndices[(int)RollbackFrameIndexType.Min] = 0;
            __frameIndices[(int)RollbackFrameIndexType.Restore] = 1;
            __frameIndices[(int)RollbackFrameIndexType.Command] = 0;

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
            Init init;
            init.frameIndices = __frameIndices;
            init.values = __values;
            init.keys = __keys;
            init.frameEntryTypes = __frameEntryTypes;
            init.frameEntries = __frameEntries;

            var jobHandle = init.ScheduleByRef(JobHandle.CombineDependencies(inputDeps, __jobHandle[0]));

            //restoreFrameIndex = frameIndex;

            RollbackEntryTest<T> test;
            test.frameIndices = __frameIndices;
            test.values = __values.AsDeferredJobArray();
            test.keys = __keys.AsDeferredJobArray();
            test.tester = tester;

            jobHandle = test.ScheduleByRef(__keys, innerloopBatchCount, jobHandle);

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
            //public EntityCommander value;
            public int commanderIndex;

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
            //public EntityCommander value;
            public int commanderIndex;

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

        /*private struct Frame
        {
            public EntityCommander commander;
            //任何指令都不能丢弃
            //public EntityCommander initCommander;
        }*/

        private struct CommanderPool
        {
            private interface ICommanderManagerWrapper
            {
                EntityCommander this[int commandIndex] { get; }

                EntityCommander Alloc(out int commanderIndex);
            }

            private struct FrameCommander : IComparable<FrameCommander>
            {
                public int index;

                public int order;

                public int CompareTo(FrameCommander other)
                {
                    return order.CompareTo(other.order);
                }
            }

            private struct CommanderManager
            {
                private int __usedCount;
                private UnsafeList<int> __indicesToAlloc;
                private UnsafeList<int> __indicesToFree;

                public unsafe int length
                {
                    get
                    {
                        return __indicesToAlloc.Length + __indicesToFree.Length;
                    }

                    set
                    {
                        /*int length = __indicesToFree.Length;
                        for (int i = 0; i < length; ++i)
                            __commanders[__indicesToFree[i]].Clear();*/

                        __indicesToAlloc.AddRange(__indicesToFree.Ptr, __indicesToFree.Length);

                        __indicesToFree.Clear();

                        int commanderLength = __usedCount + __indicesToAlloc.Length;
                        for (int i = __indicesToAlloc.Length; i < value; ++i)
                        {
                            __indicesToAlloc.Add(commanderLength++);

                            //__commanders.Add(new EntityCommander(Allocator.Persistent));
                        }

                    }
                }

                public NativeArray<int> indicesToFree => __indicesToFree.AsArray();

                public CommanderManager(in AllocatorManager.AllocatorHandle allocator)
                {
                    __usedCount = 0;
                    __indicesToAlloc = new UnsafeList<int>(0, allocator, NativeArrayOptions.UninitializedMemory);
                    __indicesToFree = new UnsafeList<int>(0, allocator, NativeArrayOptions.UninitializedMemory);
                }

                public void Dispose()
                {
                    __usedCount = 0;
                    __indicesToAlloc.Dispose();
                    __indicesToFree.Dispose();
                }


                public int Alloc()
                {
                    int index;
                    int length = __indicesToAlloc.Length;
                    UnityEngine.Assertions.Assert.IsTrue(length > 0);
                    //if (length > 0)
                    {
                        index = __indicesToAlloc[--length];

                        //commander.writer.Clear();

                        __indicesToAlloc.Resize(length, NativeArrayOptions.UninitializedMemory);
                    }
                    /*else
                        commander = new EntityCommander(Allocator.Persistent);*/

                    ++__usedCount;

                    return index;
                }

                public void Free(int index)
                {
                    --__usedCount;
                    //commander.writer.Clear();

                    __indicesToFree.Add(index);
                }

            }

            private struct FrameManager
            {
                //private NativeHashMap<uint, Frame> __frames;
                private UnsafeHashMap<uint, int> __frameIndices;
                private UnsafeParallelMultiHashMap<uint, FrameCommander> __frameCommanders;

                public FrameManager(in AllocatorManager.AllocatorHandle allocator)
                {
                    __frameIndices = new UnsafeHashMap<uint, int>(1, allocator);

                    __frameCommanders = new UnsafeParallelMultiHashMap<uint, FrameCommander>(1, allocator);
                }

                public void Dispose()
                {
                    __frameIndices.Dispose();
                    __frameCommanders.Dispose();
                }

                public void Clear(ref CommanderManager commanderManager)
                {
                    /*var enumerator = __frameCommanders.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        Free(enumerator.Current.Value.index);

                        //Free(frame.initCommander);
                    }*/

                    foreach (var frameIndex in __frameIndices)
                        commanderManager.Free(frameIndex.Value);

                    __frameIndices.Clear();

                    __frameCommanders.Clear();
                }

                public void Clear(uint frameIndex, ref CommanderManager commanderManager)
                {
                    /*if (__frameCommanders.TryGetFirstValue(frameIndex, out var frameCommander, out var iterator))
                    {
                        do
                        {
                            Free(frameCommander.index);
                            //Free(frame.initCommander);

                        } while (__frameCommanders.TryGetNextValue(out frameCommander, ref iterator));

                        __frameCommanders.Remove(frameIndex);

                        return true;
                    }*/

                    if (__frameIndices.TryGetValue(frameIndex, out int index))
                    {
                        commanderManager.Free(index);

                        __frameIndices.Remove(frameIndex);
                    }

                    __frameCommanders.Remove(frameIndex);
                }

                public void Clear(uint frameStartIndex, uint frameCount, ref CommanderManager commanderManager)
                {
                    for (uint i = 0; i < frameCount; ++i)
                        Clear(i + frameStartIndex, ref commanderManager);
                }

                public void Append(uint frameIndex, int index)
                {
                    FrameCommander frameCommander;
                    frameCommander.index = index;
                    frameCommander.order = __frameCommanders.CountValuesForKey(frameIndex);
                    __frameCommanders.Add(frameIndex, frameCommander);
                }

                public bool Apply<T>(
                    bool isInitOnly, 
                    uint frameIndex, 
                    ref SystemState systemState, 
                    ref T commanderManager) where T : ICommanderManagerWrapper
                {
                    EntityCommander commander;
                    if (__frameIndices.TryGetValue(frameIndex, out int commanderIndex))
                        commander = commanderManager[commanderIndex];
                    else if (!__frameCommanders.ContainsKey(frameIndex))
                        return false;
                    else
                    {
                        commander = commanderManager.Alloc(out commanderIndex);

                        var frameCommanders = new NativeList<FrameCommander>(systemState.WorldUpdateAllocator);
                        foreach (var frameCommander in __frameCommanders.GetValuesForKey(frameIndex))
                            frameCommanders.Add(frameCommander);

                        frameCommanders.Sort();

                        foreach (var frameCommander in frameCommanders)
                        {
                            UnityEngine.Assertions.Assert.AreNotEqual(commanderIndex, frameCommander.index);

                            commanderManager[frameCommander.index].AsReadOnly().AppendTo(ref commander);
                        }

                        //frameCommanders.Dispose();

                        __frameIndices[frameIndex] = commanderIndex;
                    }

                    return commander.Apply(ref systemState);
                }

            }

            private struct Data : ICommanderManagerWrapper
            {
                private UnsafeList<EntityCommander> __commanders;
                internal CommanderManager _commanderManager;
                internal FrameManager _frameManager;

                public AllocatorManager.AllocatorHandle allocator => __commanders.Allocator;

                public EntityCommander this[int commandIndex] => __commanders[commandIndex];

                public Data(in AllocatorManager.AllocatorHandle allocator)
                {
                    __commanders = new UnsafeList<EntityCommander>(0, allocator);
                    _commanderManager = new CommanderManager(allocator);
                    _frameManager = new FrameManager(allocator);
                }

                public EntityCommander Alloc(out int commanderIndex)
                {
                    Flush(1);

                    commanderIndex = _commanderManager.Alloc();

                    return __commanders[commanderIndex];
                }

                public void Free(int commanderIndex)
                {
                    __commanders[commanderIndex].Clear();

                    _commanderManager.Free(commanderIndex);
                }

                public void Dispose()
                {
                    foreach (var commander in __commanders)
                        commander.Dispose();
                    //frame.initCommander.Dispose();

                    __commanders.Dispose();

                    _commanderManager.Dispose();

                    _frameManager.Dispose();
                }

                public void Clear()
                {
                    _frameManager.Clear(ref _commanderManager);
                }

                /*public Writer AsWriter(int capacity)
                {
                    __Flush(capacity);

                    return new Writer(ref this);
                }*/

                public bool Apply(bool isInitOnly, uint frameIndex, ref SystemState systemState)
                {
                    return _frameManager.Apply(isInitOnly, frameIndex, ref systemState, ref this);
                }

                public void Flush(int capacity)
                {
                    var indicesToFree = _commanderManager.indicesToFree;
                    foreach(int indexToFree in indicesToFree)
                        __commanders[indexToFree].Clear();

                    int length = _commanderManager.length;
                    if (length < capacity)
                    {
                        for (int i = length; i < capacity; ++i)
                            __commanders.Add(new EntityCommander(Allocator.Persistent));

                        _commanderManager.length = capacity;
                    }
                    else
                        _commanderManager.length = length;
                }
            }

            [NativeContainer]
            public struct Writer
            {
                [NativeDisableUnsafePtrRestriction]
                private unsafe CommanderManager* __commanderManager;
                [NativeDisableUnsafePtrRestriction]
                private unsafe FrameManager* __frameManager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                internal AtomicSafetyHandle m_Safety;

                internal static readonly SharedStatic<int> StaticSafetyID = SharedStatic<int>.GetOrCreate<Writer>();
#endif

                /*private NativeList<int> __indicesToAlloc;
                private NativeList<int> __indicesToFree;
                //private NativeHashMap<uint, Frame> __frames;
                private NativeHashMap<uint, int> __frameIndices;
                private NativeParallelMultiHashMap<uint, FrameCommander> __frameCommanders;*/


                public unsafe Writer(ref CommanderPool commanderPool)
                {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    m_Safety = commanderPool.m_Safety;

                    CollectionHelper.SetStaticSafetyId<Writer>(ref m_Safety, ref StaticSafetyID.Data);
#endif

                    __commanderManager = (CommanderManager*)UnsafeUtility.AddressOf(ref commanderPool.__data->_commanderManager);
                    __frameManager = (FrameManager*)UnsafeUtility.AddressOf(ref commanderPool.__data->_frameManager);

                    /*__indicesToAlloc = commanderPool.__indicesToAlloc;
                    __indicesToFree = commanderPool.__indicesToFree;
                    __frameIndices = commanderPool.__frameIndices;
                    __frameCommanders = commanderPool.__frameCommanders;*/
                }

                public unsafe void Clear(uint frameIndex)
                {
                    __CheckWrite();

                    __frameManager->Clear(frameIndex, ref UnsafeUtility.AsRef<CommanderManager>(__commanderManager));
                    /*if (__frameIndices.TryGetValue(frameIndex, out int index))
                    {
                        Free(index);

                        __frameIndices.Remove(frameIndex);
                    }

                    __frameCommanders.Remove(frameIndex);*/
                }

                public unsafe void Clear(uint frameStartIndex, uint frameCount)
                {
                    /*for (uint i = 0; i < frameCount; ++i)
                        Clear(i + frameStartIndex);*/

                    __CheckWrite();

                    __frameManager->Clear(frameStartIndex, frameCount, ref UnsafeUtility.AsRef<CommanderManager>(__commanderManager));
                }

                public unsafe void Append(uint frameIndex, int index)
                {
                    __CheckWrite();

                    __frameManager->Append(frameIndex, index);

                    /*FrameCommander frameCommander;
                    frameCommander.index = index;
                    frameCommander.order = __frameCommanders.CountValuesForKey(frameIndex);
                    __frameCommanders.Add(frameIndex, frameCommander);*/
                }

                /*public int Alloc()
                {
                    int index;
                    int length = __indicesToAlloc.Length;
                    UnityEngine.Assertions.Assert.IsTrue(length > 0);
                    //if (length > 0)
                    {
                        index = __indicesToAlloc[--length];

                        //commander.writer.Clear();

                        __indicesToAlloc.ResizeUninitialized(length);
                    }

                    return index;
                }*/

                public unsafe void Free(int index)
                {
                    __CheckWrite();

                    __commanderManager->Free(index);

                    //__indicesToFree.Add(index);
                }


                [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                void __CheckWrite()
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                }
            }

            /*private NativeList<EntityCommander> __commanders;
            private NativeList<int> __indicesToAlloc;
            private NativeList<int> __indicesToFree;
            private NativeHashMap<uint, int> __frameIndices;
            private NativeParallelMultiHashMap<uint, FrameCommander> __frameCommanders;*/

            private unsafe Data* __data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public unsafe CommanderPool(in AllocatorManager.AllocatorHandle allocator)
            {
                /*__commanders = new NativeList<EntityCommander>(allocator);
                __indicesToAlloc = new NativeList<int>(allocator);
                __indicesToFree = new NativeList<int>(allocator);

                __frameIndices = new NativeHashMap<uint, int>(1, allocator);

                __frameCommanders = new NativeParallelMultiHashMap<uint, FrameCommander>(1, allocator);*/

                __data = AllocatorManager.Allocate<Data>(allocator);
                *__data = new Data(allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
#endif
            }

            public unsafe EntityCommander Alloc(out int commanderIndex)
            {
                /*commanderIndex = AsWriter(1).Alloc();

                return __commanders[commanderIndex];*/

                __CheckWrite();

                return __data->Alloc(out commanderIndex);
            }

            public unsafe void Free(int commanderIndex)
            {
                /*__commanders[commanderIndex].Clear();

                __indicesToAlloc.Add(commanderIndex);*/

                __CheckWrite();

                __data->Free(commanderIndex);
            }

            public unsafe void Dispose()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif

                var allocator = __data->allocator;

                __data->Dispose();

                AllocatorManager.Free(allocator, __data);

                __data = null;

                /*foreach(var commander in __commanders)
                    commander.Dispose();
                //frame.initCommander.Dispose();

                __commanders.Dispose();

                __indicesToAlloc.Dispose();

                __indicesToFree.Dispose();

                __frameIndices.Dispose();

                __frameCommanders.Dispose();*/
            }

            public unsafe void Clear()
            {
                __CheckWrite();

                __data->Clear();

                /*foreach(var frameIndex in __frameIndices)
                    Free(frameIndex.Value);

                __frameIndices.Clear();

                __frameCommanders.Clear();*/
            }

            public unsafe Writer AsWriter(int capacity)
            {
                __CheckWrite();

                /*int length = __indicesToFree.Length;
                for (int i = 0; i < length; ++i)
                    __commanders[__indicesToFree[i]].Clear();

                __indicesToAlloc.AddRange(__indicesToFree.AsArray());

                __indicesToFree.Clear();

                for (int i = __indicesToAlloc.Length; i < capacity; ++i)
                {
                    __indicesToAlloc.Add(__commanders.Length);

                    __commanders.Add(new EntityCommander(Allocator.Persistent));
                }*/

                __data->Flush(capacity);

                return new Writer(ref this);
            }

            public unsafe bool Apply(bool isInitOnly, uint frameIndex, ref SystemState systemState)
            {
                __CheckWrite();

                return __data->Apply(isInitOnly, frameIndex, ref systemState);
                /*EntityCommander commander;
                if (__frameIndices.TryGetValue(frameIndex, out int commanderIndex))
                    commander = __commanders[commanderIndex];
                else if (!__frameCommanders.ContainsKey(frameIndex))
                    return false;
                else
                {
                    commander = Alloc(out commanderIndex);

                    var frameCommanders = new NativeList<FrameCommander>(systemState.WorldUpdateAllocator);
                    foreach(var frameCommander in __frameCommanders.GetValuesForKey(frameIndex))
                        frameCommanders.Add(frameCommander);

                    frameCommanders.Sort();

                    foreach (var frameCommander in frameCommanders)
                    {
                        UnityEngine.Assertions.Assert.AreNotEqual(commanderIndex, frameCommander.index);

                        __commanders[frameCommander.index].AsReadOnly().AppendTo(ref commander);
                    }

                    //frameCommanders.Dispose();

                    __frameIndices[frameIndex] = commanderIndex;
                }

                return commander.Apply(ref systemState);*/
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void __CheckWrite()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
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

                            commanderPool.Free(callback.value.commanderIndex);

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
                                commanderPool.Free(callback.value.commanderIndex);

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
                        callback.value.commanderIndex = invokeCommand.commanderIndex;
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
                    //EntityCommander.ReadOnly commander;
                    //Frame frame;
                    uint dirtyFrameIndex;
                    int i, j, numCallbacks, numDirtyFrameIndices = dirtyFrameIndices.Length;
                    for (i = 0; i < numDirtyFrameIndices; ++i)
                    {
                        dirtyFrameIndex = dirtyFrameIndices[i];

                        commanderPool.Clear(dirtyFrameIndex);

                        if (frameCallbacks.TryGetFirstValue(dirtyFrameIndex, out callback, out iterator))
                        {
                            callbacks.Clear();

                            do
                            {
                                callbacks.Add(callback.value);
                            } while (frameCallbacks.TryGetNextValue(out callback, ref iterator));

                            callbacks.Sort();

                            //frame = commanderPool.Alloc(dirtyFrameIndex);

                            //UnityEngine.Assertions.Assert.IsTrue(frameCommander.jobHandle.IsCompleted);

                            numCallbacks = callbacks.Length;
                            for (j = 0; j < numCallbacks; ++j)
                            {
                                //UnityEngine.Assertions.Assert.IsTrue(callbacks.ElementAt(j).value.jobHandle.IsCompleted);
                                //ref var callbackEntry = ref callbacks.ElementAt(j);
                                //commander = callbackEntry.value.AsReadOnly();
                                //commander.AppendTo(ref frame.commander);

                                commanderPool.Append(dirtyFrameIndex, callbacks.ElementAt(j).commanderIndex);

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
                                commanderPool.Free(callback.value.commanderIndex);

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

        private NativeArray<JobHandle> __jobHandles;
        private NativeList<CallbackHandle> __callbackHandles;
        private NativeList<InvokeCommand> __invokeCommands;
        private NativeList<MoveCommand> __moveCommands;
        private NativeParallelMultiHashMap<uint, CallbackEx> __frameCallbacks;
        private CommanderPool __commanderPool;
        private RollbackCommander __commander;

        public bool isCreated => __jobHandles.IsCreated;

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

            __jobHandles = new NativeArray<JobHandle>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            __callbackHandles = new NativeList<CallbackHandle>(allocator);
            __invokeCommands = new NativeList<InvokeCommand>(allocator);
            __moveCommands = new NativeList<MoveCommand>(allocator);
            __frameCallbacks = new NativeParallelMultiHashMap<uint, CallbackEx>(1, allocator);
            __commander = new RollbackCommander(allocator);
            __commanderPool = new CommanderPool(allocator);
        }

        public void Dispose()
        {
            playbackJobHandle.Complete();
            updateJobHandle.Complete();

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

            foreach (var frameCallback in __frameCallbacks)
                __commanderPool.Free(frameCallback.Value.value.commanderIndex);

            foreach (var invokeCommand in __invokeCommands)
                __commanderPool.Free(invokeCommand.commanderIndex);

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

        public EntityCommander Invoke(uint frameIndex, in RollbackEntry entry, in CallbackHandle clear)
        {
            __commander.Record(frameIndex, entry);

            CompleteUpdateDependency();

            InvokeCommand invokeCommand;
            invokeCommand.rollbackEntryType = entry.type;
            invokeCommand.frameIndex = frameIndex;
            invokeCommand.key = entry.key;
            invokeCommand.clear = clear;

            var frameCommander = __commanderPool.Alloc(out invokeCommand.commanderIndex);

            __invokeCommands.Add(invokeCommand);

            /*CallbackEx callback;
            callback.orginFrameIndex = 0;
            callback.clear = clear == null ? CallbackHandle.Null : clear.Register();
            callback.value.key = entry.key;
            callback.value.value = frameCommander;
            __frameCallbacks.Add(frameIndex, callback);*/

            return frameCommander;
        }

        public EntityCommander Invoke(uint frameIndex, in RollbackEntry entry)
        {
            return Invoke(frameIndex, entry, CallbackHandle.Null);
        }

        public EntityCommander Invoke(uint frameIndex, in RollbackEntry entry, Action clear)
        {
            return Invoke(frameIndex, entry, clear == null ? CallbackHandle.Null : clear.Register());
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

        public bool Playback(bool isRollback, uint frameIndex, ref SystemState systemState)
        {
            systemState.Dependency = JobHandle.CombineDependencies(systemState.Dependency, updateJobHandle);

            playbackJobHandle.Complete();

            if (isRollback)
            {
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

                    /*if (systemState.World.Name.Contains("Client"))
                    {
                        UnityEngine.Debug.LogError($"Play {frameIndex} : {__commander.commandFrameIndex}");
                    }*/

                    __commander.commandFrameIndex = frameIndex + 1;

                    return result;
                }
            }

            if(__commanderPool.Apply(false, frameIndex, ref systemState))
            {
                //UnityEngine.Debug.Log($"{systemState.World.Name} Do_ {frameIndex}");

                playbackJobHandle = systemState.Dependency;

                return true;
            }

            if (frameIndex == __commander.commandFrameIndex)
                __commander.commandFrameIndex = frameIndex + 1;

            return false;
        }

        public JobHandle Update(uint minFrameIndex, in JobHandle inputDeps)
        {
            //updateJobHandle.Complete();

            Command command;
            command.minFrameIndex = minFrameIndex;
            command.invokeCommands = __invokeCommands;
            command.moveCommands = __moveCommands;
            command.callbackHandles = __callbackHandles;
            command.frameCallbacks = __frameCallbacks;
            command.commanderPool = __commanderPool.AsWriter((__invokeCommands.Length + (__moveCommands.Length << 1)) << 1);
            command.commander = __commander.writer;

            var jobHandle = JobHandle.CombineDependencies(__commander.jobHandle, playbackJobHandle, inputDeps);

            jobHandle = command.ScheduleByRef(JobHandle.CombineDependencies(jobHandle, updateJobHandle));

            updateJobHandle = jobHandle;

            return jobHandle;
        }

        public JobHandle Test<T>(T tester, uint minRestoreFrameIndex, int innerloopBatchCount, in JobHandle inputDeps) where T : struct, IRollbackEntryTester => 
            __commander.Test(tester, minRestoreFrameIndex, innerloopBatchCount, JobHandle.CombineDependencies(updateJobHandle, inputDeps));
    }

    [BurstCompile, 
        UpdateInGroup(typeof(FrameSyncSystemGroup), OrderFirst = true), /*UpdateInGroup(typeof(RollbackSystemGroup), OrderLast = true), */
        UpdateAfter(typeof(EndRollbackSystemGroupEntityCommandSystemGroup))]
    public partial struct RollbackCommandSystem : ISystem
    {
        private EntityQuery __frameGroup;
        //private EntityQuery __group;

        public RollbackCommanderManaged commander
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using(var builder = new EntityQueryBuilder(Allocator.Temp))
                __frameGroup = builder
                        .WithAll<FrameSyncFlag>()
                        .WithAll<RollbackFrame>()
                        .WithOptions(EntityQueryOptions.IncludeSystems)
                        .Build(ref state);

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

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            commander.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            commander.Playback(
                __frameGroup.GetSingleton<FrameSyncFlag>().isRollback, 
                __frameGroup.GetSingleton<RollbackFrame>().index, 
                ref state);
        }
    }
}