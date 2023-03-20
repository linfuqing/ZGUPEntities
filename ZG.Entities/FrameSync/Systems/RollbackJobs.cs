using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    /*public interface IRollbackRestoreChunk<out T> where T : IRollbackRestore
    {
        T Execute(in ArchetypeChunk chunk);
    }*/

    public interface IRollbackRestore
    {
        void Execute(int index, int entityIndex, in Entity entity);
    }

    public interface IRollbackSave
    {
        void Execute(in ArchetypeChunk chunk, int firstEntityIndex, bool useEnabledMask, in v128 chunkEnabledMask);
    }

    public interface IRollbackClear
    {
        void Remove(int startIndex, int count);

        void Move(int fromIndex, int toIndex, int count);

        void Resize(int count);
    }

    public struct RollbackChunk
    {
        public int startIndex;
        public int count;
    }

    [BurstCompile]
    public struct RollbackResize<T> : IJob where T : unmanaged
    {
        [ReadOnly]
        public NativeArray<int> entityCount;
        public NativeArray<int> startIndex;
        public NativeList<T> values;

        public void Execute()
        {
            int startIndex = values.Length;
            this.startIndex[0] = startIndex;
            values.ResizeUninitialized(startIndex + entityCount[0]);
        }
    }

    /// <summary>
    /// ����Ҫ,��Ϊ��ʵ�ʹ�����һ֡�Ż�ɼ�
    /// </summary>
    /*[BurstCompile]
    public struct RollbackRestoreChunk<TChunk, TEntity> : IJobChunk
        where TChunk : struct, IRollbackRestoreChunk<TEntity>
        where TEntity : struct, IRollbackRestore
    {
        public int startIndex;
        [ReadOnly]
        public NativeArray<Entity> entities;
        [ReadOnly]
        public EntityTypeHandle entityType;
        public TChunk factory;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var instance = factory.Execute(batchInChunk);

            var entityArray = batchInChunk.GetNativeArray(entityType);
            Entity entity;
            int count = batchInChunk.Count, numEntities = entities.Length, entityIndex, i, j;
            for (i = 0; i < count; ++i)
            {
                entity = entityArray[i];
                entityIndex = -1;
                for (j = startIndex; j < numEntities; ++j)
                {
                    if (entities[j] == entity)
                    {
                        entityIndex = -1;

                        break;
                    }
                }

                if (entityIndex == -1)
                    continue;

                instance.Execute(i, entityIndex, entity);
            }
        }
    }*/

    [BurstCompile]
    public struct RollbackRestore<T> : IJobParallelFor where T : struct, IRollbackRestore
    {
        public int startIndex;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        public T instance;

        public void Execute(int index)
        {
            int entityIndex = index + startIndex;
            Entity entity = entityArray[entityIndex];

            instance.Execute(index, entityIndex, entity);
        }
    }

    [BurstCompile]
    public struct RollbackRestoreSingle<T> : IJob where T : struct, IRollbackRestore
    {
        public uint frameIndex;
        [ReadOnly]
        public NativeArray<Entity> entityArray;
        [ReadOnly]
        public NativeParallelHashMap<uint, RollbackChunk> chunks;
        public T instance;

        public void Execute()
        {
            if (!chunks.TryGetValue(frameIndex, out var chunk))
                return;

            RollbackRestore<T> executor;
            executor.startIndex = chunk.startIndex;
            executor.entityArray = entityArray;
            executor.instance = instance;
            for (int i = 0; i < chunk.count; ++i)
                executor.Execute(i);
        }
    }

    [BurstCompile]
    public struct RollbackSave<T> : IJobChunk where T : struct, IRollbackSave
    {
        [ReadOnly]
        public EntityTypeHandle entityType;

        [ReadOnly]
        public NativeArray<int> startIndex;

        [ReadOnly]
        public NativeArray<int> chunkBaseEntityIndices;

        [NativeDisableParallelForRestriction]
        public NativeArray<Entity> entityArray;

        public T instance;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            int count = chunk.Count, indexOfFirstEntityInQuery = chunkBaseEntityIndices[unfilteredChunkIndex];

            var entities = chunk.GetNativeArray(entityType);

            if(useEnabledMask)
            {
                int index = indexOfFirstEntityInQuery + startIndex[0];
                var iterator = new ChunkEntityEnumerator(true, chunkEnabledMask, count);
                while (iterator.NextEntityIndex(out int i))
                    entityArray[index++] = entities[i];
            }
            else
                NativeArray<Entity>.Copy(entities, 0, entityArray, indexOfFirstEntityInQuery + startIndex[0], count);

            instance.Execute(chunk, indexOfFirstEntityInQuery, useEnabledMask, chunkEnabledMask);
        }
    }

    /*[BurstCompile]
    private struct Save<T> : IJob where T : struct, ISave
    {
        public uint frameIndex;
        public NativeList<Entity> entities;
        public NativeHashMap<uint, FrameChunk> frameChunks;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<ArchetypeChunk> chunks;

        [ReadOnly]
        public EntityTypeHandle entityType;

        public T instance;

        public void Execute()
        {
            int length = chunks.Length, count;
            ArchetypeChunk chunk;
            FrameChunk frameChunk;
            NativeArray<Entity> targetEntities;
            frameChunk.startIndex = entities.Length;
            frameChunk.count = 0;
            for (int i = 0; i < length; ++i)
            {
                chunk = chunks[i];
                count = chunk.Count;

                targetEntities = chunk.GetNativeArray(entityType);

                entities.AddRange(targetEntities);

                instance.Execute(/*frameChunk.startIndex + frameChunk.count, /chunk);

                frameChunk.count += count;
            }

            if(frameChunk.count > 0)
                frameChunks[frameIndex] = frameChunk;
        }
    }*/

    [BurstCompile]
    public struct RollbackClear<T> : IJob where T : struct, IRollbackClear
    {
        public uint maxFrameIndex;
        public uint frameIndex;
        public uint frameCount;

        public NativeList<Entity> entities;
        public NativeParallelHashMap<uint, RollbackChunk> chunks;

        public T instance;

        public void Execute()
        {
            UnityEngine.Assertions.Assert.IsFalse(chunks.ContainsKey(maxFrameIndex + 1));

#if DEBUG
            uint finalFrameIndex = maxFrameIndex + 1;
#endif

            int length = entities.Length;
            uint i, currentFrameIndex;
            RollbackChunk chunk, temp;
            chunk.startIndex = length;
            chunk.count = 0;
            for (i = 0; i < frameCount; ++i)
            {
                currentFrameIndex = frameIndex + i;
                if (!chunks.TryGetValue(currentFrameIndex, out temp))
                    continue;

                if (chunk.startIndex == length)
                    chunk.startIndex = temp.startIndex;
#if DEBUG
                else
                {
                    UnityEngine.Assertions.Assert.IsTrue(chunk.startIndex < temp.startIndex);
                    UnityEngine.Assertions.Assert.AreEqual(chunk.startIndex + chunk.count, temp.startIndex);
                }

                finalFrameIndex = currentFrameIndex;
#endif

                chunk.count += temp.count;

                chunks.Remove(currentFrameIndex);
            }

            instance.Remove(chunk.startIndex, chunk.count);

            int offset = chunk.startIndex + chunk.count;
            if (offset < length)
            {
                UnityEngine.Assertions.Assert.AreEqual(0, chunk.startIndex);
                UnityEngine.Assertions.Assert.IsFalse(chunks.ContainsKey(frameIndex - 1));

                int count = length - offset;

#if DEBUG
                var debugChunk = chunk;
#endif
                for (i = frameIndex + frameCount; i <= maxFrameIndex; ++i)
                {
                    if (!chunks.TryGetValue(i, out temp))
                        continue;

#if DEBUG
                    UnityEngine.Assertions.Assert.IsTrue(debugChunk.startIndex < temp.startIndex);
                    UnityEngine.Assertions.Assert.AreEqual(debugChunk.startIndex + debugChunk.count, temp.startIndex);

                    debugChunk.count += temp.count;
#endif

                    //if (chunks.TryGetValue(i, out temp) && temp.startIndex >= offset)
                    {
                        temp.startIndex -= chunk.count;

                        chunks[i] = temp;
                    }
                }

                entities.AsArray().MemMove(offset, chunk.startIndex, count);

                instance.Move(offset, chunk.startIndex, count);

                chunk.startIndex = length - chunk.count;
            }
#if DEBUG
            else
            {
                UnityEngine.Assertions.Assert.AreEqual(length, offset);

                if (chunk.count != 0)
                {
                    //��������֡��û��ֵ�����ʧЧ
                    //UnityEngine.Assertions.Assert.AreEqual(maxFrameIndex, frameIndex + frameCount - 1);
                    UnityEngine.Assertions.Assert.IsFalse(chunks.ContainsKey(frameIndex + frameCount));
                }
            }

            if (chunk.startIndex == 0)
                UnityEngine.Assertions.Assert.AreEqual(0, chunks.Count());
#endif

            entities.ResizeUninitialized(chunk.startIndex);

            instance.Resize(chunk.startIndex);
        }
    }

    [BurstCompile]
    public struct RollbackSaveBufferCount<T> : IJobChunk where T : unmanaged, IBufferElementData
    {
        [ReadOnly]
        public BufferTypeHandle<T> type;

        [ReadOnly]
        public NativeArray<int> startIndex;

        [ReadOnly]
        public NativeArray<int> chunkBaseEntityIndices;

        [NativeDisableParallelForRestriction]
        public NativeArray<RollbackChunk> chunks;

        public NativeCounter.Concurrent counter;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            RollbackChunk result;
            var bufferAccessor = chunk.GetBufferAccessor(ref type);
            int index = chunkBaseEntityIndices[unfilteredChunkIndex] + startIndex[0];
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                result.count = bufferAccessor[i].Length;
                result.startIndex = result.count > 0 ? counter.Add(result.count) - result.count : -1;

                chunks[index++] = result;
            }
        }
    }

    [BurstCompile]
    public struct RollbackSaveBufferResizeValues<T> : IJob where T : unmanaged, IBufferElementData
    {
        [ReadOnly]
        public NativeCounter counter;

        public NativeList<T> values;

        public void Execute()
        {
            values.ResizeUninitialized(counter.count);
        }
    }

    [BurstCompile]
    public struct RollbackBuildEntityIndices : IJobParallelFor
    {
        [ReadOnly]
        public NativeSlice<Entity> entities;
        public NativeParallelHashMap<Entity, int>.ParallelWriter results;

        public void Execute(int index)
        {
            results.TryAdd(entities[index], index);
        }
    }
}