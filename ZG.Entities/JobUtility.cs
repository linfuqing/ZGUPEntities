using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

namespace ZG
{
    [Serializable]
    public struct EntityData<T> : IComparable<EntityData<T>> where T : struct
    {
        public Entity entity;
        public T value;

        public EntityData(in Entity entity, in T value)
        {
            this.entity = entity;
            this.value = value;
        }

        public int CompareTo(EntityData<T> other)
        {
            return entity.CompareTo(other.entity);
        }
    }

    [BurstCompile]
    public struct CopyNativeArrayToComponentDataSingle<T> : IJob where T : unmanaged, IComponentData
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<T> source;

        public ComponentLookup<T> destination;

        public void Execute()
        {
            int length = entityArray.Length;
            for (int i = 0; i < length; ++i)
                destination[entityArray[i]] = source[i];
        }
    }

    [BurstCompile]
    public struct CopyNativeArrayToComponentData<T> : IJobParallelFor where T : unmanaged, IComponentData
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<T> source;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<T> destination;

        public void Execute(int index)
        {
            destination[entityArray[index]] = source[index];
        }
    }

    [BurstCompile]
    public struct CopyNativeArrayToBuffer<T> : IJobParallelFor where T : unmanaged, IBufferElementData
    {
        [ReadOnly]
        public NativeArray<Entity> entityArray;

        [ReadOnly]
        public NativeArray<T> source;

        [NativeDisableParallelForRestriction]
        public BufferLookup<T> destination;

        public void Execute(int index)
        {
            destination[entityArray[index]].Add(source[index]);
        }
    }

    [BurstCompile]
    public struct CopyNativeHashMapToComponentData<T> : IJob where T : unmanaged, IComponentData
    {
        [ReadOnly]
        public NativeParallelHashMap<Entity, T> source;

        public ComponentLookup<T> destination;

        public void Execute()
        {
            using (var keyValueArrays = source.GetKeyValueArrays(Allocator.Temp))
            {
                int length = keyValueArrays.Keys.Length;
                for (int i = 0; i < length; ++i)
                    destination[keyValueArrays.Keys[i]] = keyValueArrays.Values[i];
            }
        }
    }

    [BurstCompile]
    public struct CopyNativeQueueToComponentData<T> : IJob where T : unmanaged, IComponentData
    {
        public NativeQueue<EntityData<T>> source;

        public ComponentLookup<T> destination;

        public void Execute()
        {
            while (source.TryDequeue(out var data))
                destination[data.entity] = data.value;
        }
    }

    [BurstCompile]
    public struct CopyNativeQueueToBuffer<T> : IJob where T : unmanaged, IBufferElementData
    {
        public NativeQueue<EntityData<T>> source;

        public BufferLookup<T> destination;

        public void Execute()
        {
            DynamicBuffer<T> buffer;
            while (source.TryDequeue(out var data))
            {
                buffer = destination[data.entity];
                buffer.Add(data.value);
            }
        }
    }

    [BurstCompile]
    public struct CopyNativeQueueToList<T> : IJob where T : unmanaged
    {
        public NativeQueue<T> source;

        public NativeList<T> destination;

        public void Execute()
        {
            T item;
            while (source.TryDequeue(out item))
                destination.Add(item);
        }
    }

    [BurstCompile]
    public struct ResizeList<T> : IJob
        where T : unmanaged
    {
        [ReadOnly]
        public NativeCounter counter;
        public NativeList<T> instance;

        public void Execute()
        {
            int capacity = instance.Length + counter.count;
            if (instance.Capacity < capacity)
                instance.Capacity = capacity;
        }
    }

    [BurstCompile]
    public struct ResizeHashMap<TKey, TValue> : IJob
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        public int count;
        public NativeParallelHashMap<TKey, TValue> instance;

        public void Execute()
        {
            int capacity = instance.Count() + count;
            if (instance.Capacity < capacity)
                instance.Capacity = capacity;
        }
    }

    [BurstCompile]
    public struct ClearHashMap<TKey, TValue> : IJob
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        public int capacity;
        public NativeParallelHashMap<TKey, TValue> instance;

        public void Execute()
        {
            if (instance.Capacity < capacity)
                instance.Capacity = capacity;

            instance.Clear();
        }
    }

    [BurstCompile]
    public struct ClearMultiHashMap<TKey, TValue> : IJob
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        public int capacity;
        public NativeParallelMultiHashMap<TKey, TValue> instance;

        public void Execute()
        {
            if (instance.Capacity < capacity)
                instance.Capacity = capacity;

            instance.Clear();
        }
    }

    [BurstCompile]
    public struct ClearQueue<T> : IJob
        where T : unmanaged
    {
        public NativeQueue<T> instance;

        public void Execute()
        {
            instance.Clear();
        }
    }

    [BurstCompile]
    public struct ClearList<T> : IJob
        where T : unmanaged
    {
        public NativeList<T> instance;

        public void Execute()
        {
            instance.Clear();
        }
    }

    public static class JobUtility
    {
        public static void MoveTo<T>(this in NativeArray<T> source, in NativeArray<Entity> entityArray, in ComponentLookup<T> destination) where T : unmanaged, IComponentData
        {
            CopyNativeArrayToComponentData<T> copyNativeArrayToComponentData;
            copyNativeArrayToComponentData.entityArray = entityArray;
            copyNativeArrayToComponentData.source = source;
            copyNativeArrayToComponentData.destination = destination;
            copyNativeArrayToComponentData.Run(entityArray.Length);
        }

        public static JobHandle MoveTo<T>(
            this in NativeArray<T> source,
            in NativeArray<Entity> entityArray,
            in ComponentLookup<T> destination,
            JobHandle jobHandle) where T : unmanaged, IComponentData
        {
            CopyNativeArrayToComponentDataSingle<T> copyNativeArrayToComponentData;
            copyNativeArrayToComponentData.entityArray = entityArray;
            copyNativeArrayToComponentData.source = source;
            copyNativeArrayToComponentData.destination = destination;
            return copyNativeArrayToComponentData.Schedule(jobHandle);
        }

        public static JobHandle MoveTo<T>(
            this in NativeArray<T> source, 
            in NativeArray<Entity> entityArray, 
            in ComponentLookup<T> destination,
            int innerloopBatchCount,
            JobHandle jobHandle) where T : unmanaged, IComponentData
        {
            CopyNativeArrayToComponentData<T> copyNativeArrayToComponentData;
            copyNativeArrayToComponentData.entityArray = entityArray;
            copyNativeArrayToComponentData.source = source;
            copyNativeArrayToComponentData.destination = destination;
            return copyNativeArrayToComponentData.Schedule(entityArray.Length, innerloopBatchCount, jobHandle);
        }

        public static JobHandle MoveTo<T>(
            this in NativeArray<T> source,
            in NativeArray<Entity> entityArray,
            in BufferLookup<T> destination,
            int innerloopBatchCount,
            JobHandle jobHandle) where T : unmanaged, IBufferElementData
        {
            CopyNativeArrayToBuffer<T> copyNativeArrayToBuffer;
            copyNativeArrayToBuffer.entityArray = entityArray;
            copyNativeArrayToBuffer.source = source;
            copyNativeArrayToBuffer.destination = destination;
            return copyNativeArrayToBuffer.Schedule(entityArray.Length, innerloopBatchCount, jobHandle);
        }

        public static JobHandle MoveTo<T>(this in NativeParallelHashMap<Entity, T> source, in ComponentLookup<T> destination, JobHandle jobHandle) where T : unmanaged, IComponentData
        {
            CopyNativeHashMapToComponentData<T> copyNativeHashMapToComponentData;
            copyNativeHashMapToComponentData.source = source;
            copyNativeHashMapToComponentData.destination = destination;
            return copyNativeHashMapToComponentData.Schedule(jobHandle);
        }

        public static JobHandle MoveTo<T>(this NativeQueue<EntityData<T>> source, ComponentLookup<T> destination, JobHandle jobHandle) where T : unmanaged, IComponentData
        {
            if (!source.IsCreated)
                return jobHandle;

            CopyNativeQueueToComponentData<T> copyNativeQueueToComponentData;
            copyNativeQueueToComponentData.source = source;
            copyNativeQueueToComponentData.destination = destination;

            return copyNativeQueueToComponentData.Schedule(jobHandle);
        }

        public static JobHandle MoveTo<T>(this NativeQueue<EntityData<T>> source, BufferLookup<T> destination, JobHandle jobHandle) where T : unmanaged, IBufferElementData
        {
            if (!source.IsCreated)
                return jobHandle;

            CopyNativeQueueToBuffer<T> copyNativeQueueToBuffer;
            copyNativeQueueToBuffer.source = source;
            copyNativeQueueToBuffer.destination = destination;

            return copyNativeQueueToBuffer.Schedule(jobHandle);
        }

        public static JobHandle MoveTo<T>(this NativeQueue<T> source, NativeList<T> destination, JobHandle jobHandle) where T : unmanaged
        {
            if (!source.IsCreated)
                return jobHandle;

            CopyNativeQueueToList<T> copyNativeQueueToList;
            copyNativeQueueToList.source = source;
            copyNativeQueueToList.destination = destination;

            return copyNativeQueueToList.Schedule(jobHandle);
        }

        public static JobHandle Resize<T>(this NativeList<T> instance, in NativeCounter counter, JobHandle jobHandle) where T : unmanaged
        {
            ResizeList<T> resizeList;
            resizeList.counter = counter;
            resizeList.instance = instance;
            return resizeList.Schedule(jobHandle);
        }

        public static JobHandle Resize<TKey, TValue>(this NativeParallelHashMap<TKey, TValue> instance, int count, JobHandle jobHandle)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            if (!instance.IsCreated)
                return jobHandle;

            ResizeHashMap<TKey, TValue> resizeHashMap;
            resizeHashMap.count = count;
            resizeHashMap.instance = instance;
            return resizeHashMap.Schedule(jobHandle);
        }

        public static JobHandle Clear<TKey, TValue>(this NativeParallelHashMap<TKey, TValue> instance, int capacity, JobHandle jobHandle) 
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            if (!instance.IsCreated)
                return jobHandle;

            ClearHashMap<TKey, TValue> clearHashMap;
            clearHashMap.capacity = capacity;
            clearHashMap.instance = instance;
            return clearHashMap.Schedule(jobHandle);
        }
        
        public static JobHandle Clear<TKey, TValue>(this NativeParallelMultiHashMap<TKey, TValue> instance, int capacity, JobHandle jobHandle)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            if (!instance.IsCreated)
                return jobHandle;

            ClearMultiHashMap<TKey, TValue> clearMultiHashMap;
            clearMultiHashMap.capacity = capacity;
            clearMultiHashMap.instance = instance;
            return clearMultiHashMap.Schedule(jobHandle);
        }

        public static JobHandle Clear<T>(this NativeList<T> instance, in JobHandle jobHandle) where T : unmanaged
        {
            if (!instance.IsCreated)
                return jobHandle;

            ClearList<T> clearList = new ClearList<T>();
            clearList.instance = instance;
            return clearList.Schedule(jobHandle);
        }
    }
}