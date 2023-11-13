using System;
using System.Diagnostics;
using Unity.Burst.CompilerServices;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Assert = UnityEngine.Assertions.Assert;

namespace Unity.Entities
{
    public partial struct EntityManager
    {
        public unsafe void AddComponentBurstCompatible(in NativeArray<Entity> entities, ComponentType componentType)
        {
            this.AddComponent(entities, componentType);
        }

        public void AddComponentBurstCompatible<T>(NativeArray<Entity> entities)
        {
            AddComponent<T>(entities);
        }

        public unsafe void AddComponentDataBurstCompatible<T>(in EntityQuery entityQuery, NativeArray<T> componentArray) where T : unmanaged, IComponentData
        {
            AddComponentData(entityQuery, componentArray);
        }
    }
}

namespace ZG
{
    public static partial class BurstUtility
    {
        [BurstCompile]
        private struct CalculateEntityCount : IJobChunk
        {
            public NativeArray<int> counter;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                counter[0] += useEnabledMask ? EnabledBitUtility.countbits(chunkEnabledMask) : chunk.Count;
            }
        }

        static BurstUtility()
        {
        }

        internal static void InitializeJob<T>() where T : struct, IJob
        {
        }

        internal static void InitializeJobParallelFor<T>() where T : struct, IJobParallelFor
        {
        }

        public static int CountBits(in this v128 u)
        {
            return EnabledBitUtility.countbits(u);
        }

        public static v128 Negate(in this v128 u)
        {
            if (X86.Sse2.IsSse2Supported)
                return X86.Sse2.xor_si128(u, new v128(0xFFFFFFFF));
            
            return new v128(~u.ULong0, ~u.ULong1);
        }

        public static JobHandle CalculateEntityCountAsync(this in EntityQuery group, NativeArray<int> counter, in JobHandle inputDeps)
        {
            CalculateEntityCount calculateEntityCount;
            calculateEntityCount.counter = counter;
            return calculateEntityCount.ScheduleByRef(group, inputDeps);
        }

        public static NativeArray<Entity> ToEntityArrayBurstCompatible(this in EntityQuery group, in EntityTypeHandle entityType, Allocator allocator)
        {
            return group.ToEntityArray(allocator);
        }

        public static NativeArray<T> ToComponentDataArrayBurstCompatible<T>(
            this in EntityQuery group,
            in DynamicComponentTypeHandle componentType,
            Allocator allocator) where T : unmanaged, IComponentData
        {
            return group.ToComponentDataArray<T>(allocator);
        }
    }
}