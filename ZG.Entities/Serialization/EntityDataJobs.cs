using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using ZG;

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
        bool Fallback(int index);

        void Deserialize(int index, ref EntityDataReader reader);
    }

    [BurstCompile]
    public struct EntityDataContainerDeserialize<T> : IJob where T : struct, IEntityDataContainerDeserializer
    {
        public int typeHandle;

        [ReadOnly]
        public EntityDataDeserializationStatusQuery.Container status;

        [ReadOnly]
        public SharedHashMap<int, UnsafeBlock>.Reader blocks;
        public T container;

        public void Execute()
        {
            if (status.value != EntityDataDeserializationStatus.Value.Created)
                return;

            if (!blocks.TryGetValue(typeHandle, out var block))
                return;

            container.Deserialize(block);
        }
    }

    [BurstCompile]
    public struct EntityDataComponentDeserialize<TDeserializer, TDeserializerFactory> : IJobChunk
        where TDeserializer : struct, IEntityDataDeserializer
        where TDeserializerFactory : struct, IEntityDataFactory<TDeserializer>
    {
#if DEBUG
        public NativeText.ReadOnly systemTypeName;
#endif

        public int typeHandle;

        [ReadOnly]
        public SharedHashMap<EntityDataIdentityBlockKey, UnsafeBlock>.Reader identityBlocks;

        [ReadOnly]
        public SharedHashMap<Hash128, int>.Reader identityGUIDIndices;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataIdentity> identityType;

        [ReadOnly]
        public ComponentTypeHandle<EntityDataDeserializable> deserializableType;

        public TDeserializerFactory factory;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            EntityDataIdentityBlockKey key;
            key.typeHandle = typeHandle;

            var deserializer = factory.Create(chunk, unfilteredChunkIndex);

            var identities = chunk.GetNativeArray(ref identityType);
            UnsafeBlock block;
            EntityDataReader reader;
            var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (iterator.NextEntityIndex(out int i))
            {
                if (!chunk.IsComponentEnabled(ref deserializableType, i))
                    continue;

                if (!identityGUIDIndices.TryGetValue(identities[i].guid, out key.guidIndex) || !identityBlocks.TryGetValue(key/*identities[i].guid*/, out block))
                {
                    if (!deserializer.Fallback(i))
                    {
#if DEBUG
                        UnityEngine.Debug.LogError($"Deserialize Fail. SystemType : {systemTypeName}, TypeHandle : {typeHandle}, GUID: {identities[i].guid} : ");
#else
                        UnityEngine.Debug.LogError($"Deserialize Fail. TypeHandle : {typeHandle}, GUID: {identities[i].guid} : ");
#endif
                    }

                    continue;
                }

                reader = new EntityDataReader(block);

                //���������ref
                deserializer.Deserialize(i, ref reader);
            }
        }
    }

    #endregion
}