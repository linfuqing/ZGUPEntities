using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities.LowLevel.Unsafe;

namespace ZG
{
    public interface IEntityDataFactory<T>
    {
        T Create(in ArchetypeChunk chunk, int unfilteredChunkIndex);
    }

    #region Serialization
    public interface IEntityDataContainerSerializer
    {
        void Serialize(ref NativeBuffer.Writer writer);
    }

    public interface IEntityDataSerializer
    {
        void Serialize(int index, in SharedHashMap<Hash128, int>.Reader entityIndices, ref EntityDataWriter writer);
    }

    [BurstCompile]
    public struct EntityDataContainerSerialize<T> : IJob where T : struct, IEntityDataContainerSerializer
    {
        public int typeHandle;
        public NativeBuffer.Writer writer;
        public T container;

        public void Execute()
        {
            writer.Write(typeHandle);

            var block = writer.WriteBlock(0);
            int position = writer.position;
            container.Serialize(ref writer);
            block.value = writer.position - position;
        }
    }

    [BurstCompile]
    public struct EntityDataComponentSerialize<TSerializer, TSerializerFactory> : IJobChunk
        where TSerializer : struct, IEntityDataSerializer
        where TSerializerFactory : struct, IEntityDataFactory<TSerializer>
    {
        public int typeHandle;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityType;

        [ReadOnly]
        public SharedHashMap<Hash128, int>.Reader entityIndices;

        [NativeDisableParallelForRestriction]
        public EntityDataSerializationBufferManager bufferManager;

        public TSerializerFactory factory;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var serializer = factory.Create(chunk, unfilteredChunkIndex);

            var identities = chunk.GetNativeArray(ref identityType);
            UnsafeBlock<int> block;
            EntityDataWriter writer;
            EntityDataSerializationBufferManager.Buffer buffer;
            int index, position;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(iterator.NextEntityIndex(out int i))
            {
                if (!entityIndices.TryGetValue(identities[i].guid, out index))
                    continue;

                buffer = bufferManager.BeginWrite(index);

                writer = buffer.AsWriter();//new EntityDataWriter(ref buffer);
                writer.Write(typeHandle);

                block = writer.WriteBlock(0);
                position = writer.position;
                serializer.Serialize(i, entityIndices, ref writer);
                block.value = writer.position - position;

                bufferManager.EndWrite(buffer);
                //buffers[index] = buffer;
            }
        }
    }

    #endregion

    #region Deserialization
    public interface IEntityDataContainerDeserializer
    {
        void Deserialize(in UnsafeBlock block);
    }

    public interface IEntityDataDeserializer
    {
        void Deserialize(int index, ref EntityDataReader reader);
    }

    [BurstCompile]
    public struct EntityDataContainerDeserialize<T> : IJob where T : struct, IEntityDataContainerDeserializer
    {
        public int typeIndex;
        [ReadOnly]
        public NativeParallelHashMap<int, UnsafeBlock> blocks;
        public T container;

        public void Execute()
        {
            if (!blocks.TryGetValue(typeIndex, out var block))
                return;

            container.Deserialize(block);
        }
    }

    [BurstCompile]
    public struct EntityDataComponentDeserialize<TDeserializer, TDeserializerFactory> : IJobChunk
        where TDeserializer : struct, IEntityDataDeserializer
        where TDeserializerFactory : struct, IEntityDataFactory<TDeserializer>
    {
        public FixedString32Bytes componentTypeName;

        [ReadOnly, DeallocateOnJobCompletion]
        public NativeArray<UnsafeParallelHashMap<Hash128, UnsafeBlock>> blocks;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityType;

        public TDeserializerFactory factory;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var blocks = this.blocks[0];
            if (!blocks.IsCreated)
                return;

            var deserializer = factory.Create(chunk, unfilteredChunkIndex);

            var identities = chunk.GetNativeArray(ref identityType);
            UnsafeBlock block;
            EntityDataReader reader;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (!blocks.TryGetValue(identities[i].guid, out block))
                {
                    //UnityEngine.Debug.LogError($"Deserialize Fail. Component : {componentTypeName}, Guid: {identities[i].guid} : ");

                    continue;
                }

                reader = new EntityDataReader(block);

                //���������ref
                deserializer.Deserialize(i, ref reader);
            }
        }
    }

    [BurstCompile]
    public struct EntityDataDeserializationGetMap : IJob
    {
        public int typeIndex;
        [ReadOnly]
        public NativeParallelHashMap<int, UnsafeParallelHashMap<Hash128, UnsafeBlock>> input;
        public NativeArray<UnsafeParallelHashMap<Hash128, UnsafeBlock>> output;

        public void Execute()
        {
            if (!input.TryGetValue(typeIndex, out var result))
                result = default;

            output[0] = result;
        }
    }
    #endregion
}