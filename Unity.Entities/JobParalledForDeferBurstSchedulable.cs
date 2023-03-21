using System;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace ZG
{
    [JobProducerType(typeof(JobParalledForDeferBurstSchedulableExtensions.JobProducer<>))]
    public interface IJobParalledForDeferBurstSchedulable
    {
        void Execute(int index);
    }

    public static class JobParalledForDeferBurstSchedulableExtensions
    {
        internal struct JobProducer<T> where T : struct, IJobParalledForDeferBurstSchedulable
        {
            public static readonly SharedStatic<IntPtr> JobReflectionData = SharedStatic<IntPtr>.GetOrCreate<JobProducer<T>>();

            [UnityEngine.Scripting.Preserve]
            public static unsafe void Initialize()
            {
                if (JobReflectionData.Data == IntPtr.Zero)
                {
#if UNITY_2020_2_OR_NEWER || UNITY_DOTSRUNTIME
                    JobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), (ExecuteJobFunction)Execute);
#else
                    JobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), JobType.ParallelFor, (ExecuteJobFunction)Execute);
#endif
                }
            }

            internal delegate void ExecuteJobFunction(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static unsafe void Execute(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                while (true)
                {
                    int begin;
                    int end;
                    if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out begin, out end))
                        break;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobData), begin, end - begin);
#endif

                    var endThatCompilerCanSeeWillNeverChange = end;
                    for (var i = begin; i < endThatCompilerCanSeeWillNeverChange; ++i)
                        jobData.Execute(i);
                }
            }
        }

        public static unsafe JobHandle ScheduleParallel<T, U>(this T jobData, NativeList<U> list, int innerloopBatchCount,
            JobHandle dependsOn = new JobHandle())
            where T : struct, IJobParalledForDeferBurstSchedulable
            where U : unmanaged
        {
            var reflectionData = JobProducer<T>.JobReflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);

            void* atomicSafetyHandlePtr = null;

            // Calculate the deferred atomic safety handle before constructing JobScheduleParameters so
            // DOTS Runtime can validate the deferred list statically similar to the reflection based
            // validation in Big Unity.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = NativeListUnsafeUtility.GetAtomicSafetyHandle(ref list);
            atomicSafetyHandlePtr = UnsafeUtility.AddressOf(ref safety);
#endif

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobData),
                reflectionData, 
                dependsOn,
#if UNITY_2020_2_OR_NEWER
                ScheduleMode.Parallel
#else
                ScheduleMode.Batched
#endif
                );

            return JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, innerloopBatchCount,
                NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref list), atomicSafetyHandlePtr);
        }

        public static unsafe JobHandle ScheduleUnsafeIndex0<T>(
            this T jobData, 
            NativeArray<int> forEachCount, 
            int innerloopBatchCount,
            in JobHandle dependsOn = new JobHandle())
            where T : struct, IJobParalledForDeferBurstSchedulable
        {
            var reflectionData = JobProducer<T>.JobReflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);

            void* atomicSafetyHandlePtr = null;

/*#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(forEachCount);
            atomicSafetyHandlePtr = UnsafeUtility.AddressOf(ref safety);
#endif*/

#if UNITY_2020_2_OR_NEWER || UNITY_DOTSRUNTIME
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, dependsOn, ScheduleMode.Parallel);
#else
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, dependsOn, ScheduleMode.Batched);
#endif

            var forEachListPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(forEachCount) - sizeof(void*);
            return JobsUtility.ScheduleParallelForDeferArraySize(
                ref scheduleParams, 
                innerloopBatchCount,
                forEachListPtr,
                atomicSafetyHandlePtr);
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckReflectionDataCorrect(IntPtr reflectionData)
        {
            if (reflectionData == IntPtr.Zero)
                throw new InvalidOperationException("Reflection data was not set up by a call to Initialize()");
        }
    }

    public static partial class BurstUtility
    {
        public static void InitializeJobParalledForDefer<T>() where T : struct, IJobParalledForDeferBurstSchedulable
        {
            JobParalledForDeferBurstSchedulableExtensions.JobProducer<T>.Initialize();
        }
    }
}
