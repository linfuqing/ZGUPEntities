using System;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace ZG
{
    public static class JobParallelForDeferUtility
    {
        public static unsafe JobHandle ScheduleUnsafeIndex0<T>(
            this T jobData, 
            NativeArray<int> forEachCount, 
            int innerloopBatchCount,
            in JobHandle dependsOn = new JobHandle())
            where T : struct, IJobParallelForDefer
        {
            return IJobParallelForDeferExtensions.ScheduleByRef(
                ref jobData, 
                (int*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(forEachCount), 
                innerloopBatchCount, 
                dependsOn);
        }

        public static unsafe JobHandle ScheduleUnsafeIndex0ByRef<T>(
            ref this T jobData,
            NativeArray<int> forEachCount,
            int innerloopBatchCount,
            in JobHandle dependsOn = new JobHandle())
            where T : struct, IJobParallelForDefer
        {
            return IJobParallelForDeferExtensions.ScheduleByRef(
                ref jobData,
                (int*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(forEachCount),
                innerloopBatchCount,
                dependsOn);
        }
    }
}
