using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Entities;

namespace ZG
{
    public static class BlobUtility
    {
        public static unsafe NativeArray<T> AsArray<T>(this ref BlobArray<T> blobArray) where T : struct
        {
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(blobArray.GetUnsafePtr(), blobArray.Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            return result;
        }
    }
}