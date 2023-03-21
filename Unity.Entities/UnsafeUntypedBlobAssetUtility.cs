using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Entities.Serialization;

namespace ZG
{
    public static class UnsafeUntypedBlobAssetUtility
    {
        /// <summary>
        /// Serializes the blob asset data and writes the bytes to a <see cref="BinaryWriter"/> instance.
        /// </summary>
        /// <param name="binaryWriter">An implementation of the BinaryWriter interface.</param>
        /// <param name="blob">A reference to the blob asset to serialize.</param>
        /// <typeparam name="T">The blob asset's root data type.</typeparam>
        /// <seealso cref="StreamBinaryWriter"/>
        /// <seealso cref="MemoryBinaryWriter"/>
        unsafe public static void Write(this BinaryWriter binaryWriter, UnsafeUntypedBlobAssetReference blob)
        {
            var blobAssetLength = blob.m_data.Header->Length;
            var serializeReadyHeader = BlobAssetHeader.CreateForSerialize(blobAssetLength, blob.m_data.Header->Hash);

            binaryWriter.WriteBytes(&serializeReadyHeader, sizeof(BlobAssetHeader));
            binaryWriter.WriteBytes(blob.m_data.Header + 1, blobAssetLength);
        }

        /// <summary>
        /// Reads bytes from a <see cref="BinaryReader"/> instance and deserializes them into a new blob asset.
        /// </summary>
        /// <param name="binaryReader">An implementation of the BinaryReader interface.</param>
        /// <typeparam name="T">The blob asset's root data type.</typeparam>
        /// <returns>A reference to the deserialized blob asset.</returns>
        /// <seealso cref="StreamBinaryReader"/>
        /// <seealso cref="MemoryBinaryReader"/>
        unsafe public static UnsafeUntypedBlobAssetReference ReadUnsafeUntypedBlobAssetReference(this BinaryReader binaryReader)
        {
            BlobAssetHeader header;
            binaryReader.ReadBytes(&header, sizeof(BlobAssetHeader));

            var buffer = (byte*)Memory.Unmanaged.Allocate(sizeof(BlobAssetHeader) + header.Length, 16, Allocator.Persistent);
            binaryReader.ReadBytes(buffer + sizeof(BlobAssetHeader), header.Length);

            var bufferHeader = (BlobAssetHeader*)buffer;
            bufferHeader->Allocator = Allocator.Persistent;
            bufferHeader->Length = header.Length;
            bufferHeader->ValidationPtr = buffer + sizeof(BlobAssetHeader);

            // @TODO use 64bit hash
            bufferHeader->Hash = header.Hash;

            UnsafeUntypedBlobAssetReference blobAssetReference;
            blobAssetReference.m_data.m_Align8Union = 0;
            blobAssetReference.m_data.m_Ptr = buffer + sizeof(BlobAssetHeader);

            return blobAssetReference;
        }
    }
}