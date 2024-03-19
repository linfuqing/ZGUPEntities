using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace ZG
{
    public enum LandscapeLoaderCompleteType
    {
        Error,
        Done,
        Reverse
    }

    public interface ILandscapeWrapper<T> where T : unmanaged, IEquatable<T>
    {
        void FindNeighbor(
            in UnsafeParallelHashSet<T> origins,
            ref UnsafeParallelHashMap<T, int> addList,
            ref UnsafeParallelHashMap<T, int> removeList);
    }

    public struct LandscapeLayer<T> where T : unmanaged, IEquatable<T>
    {
        [Flags]
        public enum Status
        {
            Loading = 0x00,
            Unloading = 0x01,
            ReverseAfterDone = 0x02
        }

        private UnsafeParallelHashMap<T, Status> __states;
        private UnsafeParallelHashMap<T, int> __addList;
        private UnsafeParallelHashMap<T, int> __removeList;
        private UnsafeParallelHashSet<T> __origins;

        public bool isEmpty => __states.IsEmpty && __addList.IsEmpty && __removeList.IsEmpty && __origins.IsEmpty;

        public int countToLoad => __addList.Count();

        public int countToUnload => __removeList.Count();

        public int GetCountToLoad(int minDistance = int.MaxValue)
        {
            int count = 0;
            var enumerator = __addList.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Value < minDistance)
                    ++count;
            }

            return count;
        }

        public int GetCountToUnload(int maxDistance = int.MinValue)
        {
            int count = 0;
            var enumerator = __addList.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Value > maxDistance)
                    ++count;
            }

            return count;
        }

        public LandscapeLayer(in AllocatorManager.AllocatorHandle allocator)
        {
            __states = new UnsafeParallelHashMap<T, Status>(1, allocator);
            __addList = new UnsafeParallelHashMap<T, int>(1, allocator);
            __removeList = new UnsafeParallelHashMap<T, int>(1, allocator);
            __origins = new UnsafeParallelHashSet<T>(1, allocator);
        }

        public void Dispose()
        {
            __states.Dispose();
            __origins.Dispose();
            __addList.Dispose();
            __removeList.Dispose();
        }

        public int GetMinDistanceToLoad(out T position, int minDistance = int.MaxValue)
        {
            position = default;

            int distance;
            KeyValue<T, int> keyValue;
            var enumerator = __addList.GetEnumerator();
            while (enumerator.MoveNext())
            {
                keyValue = enumerator.Current;
                distance = keyValue.Value;
                if (distance < minDistance)
                {
                    minDistance = distance;

                    position = keyValue.Key;
                }
            }

            return minDistance;
        }

        public int GetMaxDistanceToUnload(out T position, int maxDistance = int.MinValue)
        {
            position = default;

            int distance;
            KeyValue<T, int> keyValue;
            var enumerator = __removeList.GetEnumerator();
            while (enumerator.MoveNext())
            {
                keyValue = enumerator.Current;
                distance = keyValue.Value;
                if (distance > maxDistance)
                {
                    maxDistance = distance;

                    position = keyValue.Key;
                }
            }

            return maxDistance;
        }

        public bool Load(in T position)
        {
            if (__addList.Remove(position) && __origins.Add(position))
            {
                if (__states.TryGetValue(position, out var status))
                {
                    if ((status & Status.ReverseAfterDone) == Status.ReverseAfterDone)
                    {
                        status &= ~Status.ReverseAfterDone;

                        if (status == Status.Loading)
                        {
                            __states[position] = status;

                            //return true;
                        }
                        else
                            UnityEngine.Debug.LogError($"Error Position {position} Status {status}");
                    }
                    else if (status == Status.Unloading)
                    {
                        status |= Status.ReverseAfterDone;

                        __states[position] = status;

                        //return true;
                    }
                    else
                        UnityEngine.Debug.LogError($"Error Position {position} Status {status}");
                }
                else
                {
                    __states[position] = Status.Loading;

                    return true;
                }
            }

            return false;
        }

        public bool Unload(in T position)
        {
            if (__removeList.Remove(position) && __origins.Remove(position))
            {
                if (__states.TryGetValue(position, out var status))
                {
                    if ((status & Status.ReverseAfterDone) == Status.ReverseAfterDone)
                    {
                        status &= ~Status.ReverseAfterDone;

                        if (status == Status.Unloading)
                        {
                            __states[position] = status;

                            //return true;
                        }
                        else
                            UnityEngine.Debug.LogError($"Error Position {position} Status {status}");
                    }
                    else if (status == Status.Loading)
                    {
                        status |= Status.ReverseAfterDone;

                        __states[position] = status;

                        //return true;
                    }
                    else
                        UnityEngine.Debug.LogError($"Error Position {position} Status {status}");
                }
                else
                {
                    __states[position] = Status.Unloading;

                    return true; // __origins.Remove(position);
                }
            }

            return false;
        }

        public LandscapeLoaderCompleteType Complete(bool isLoading, in T position)
        {
            if (__states.TryGetValue(position, out var status))
            {
                if ((status & Status.ReverseAfterDone) == Status.ReverseAfterDone)
                {
                    status &= ~Status.ReverseAfterDone;

                    if (status == Status.Loading)
                    {
                        if (!isLoading)
                            return LandscapeLoaderCompleteType.Error;

                        status = Status.Unloading;

                        __states[position] = status;
                    }
                    else if (status == Status.Unloading)
                    {
                        if (isLoading)
                            return LandscapeLoaderCompleteType.Error;

                        status = Status.Loading;

                        __states[position] = status;
                    }
                    else
                        return LandscapeLoaderCompleteType.Error;

                    return LandscapeLoaderCompleteType.Reverse;
                }
                else if (status == Status.Loading)
                {
                    if (!isLoading)
                        return LandscapeLoaderCompleteType.Error;

                    __states.Remove(position);

                    return LandscapeLoaderCompleteType.Done;
                }
                else if (status == Status.Unloading)
                {
                    if (isLoading)
                        return LandscapeLoaderCompleteType.Error;

                    __states.Remove(position);

                    return LandscapeLoaderCompleteType.Done;
                }

                return LandscapeLoaderCompleteType.Error;
            }

            return LandscapeLoaderCompleteType.Error;
        }

        public void Restore()
        {
            __addList.Clear();

            __removeList.Clear();

            using (var enumerator = __origins.GetEnumerator())
            {
                while (enumerator.MoveNext())
                    __removeList.Add(enumerator.Current, int.MaxValue);
            }
        }

        public void Apply<TWrapper>(ref TWrapper layer) where TWrapper : ILandscapeWrapper<T>
        {
            layer.FindNeighbor(__origins, ref __addList, ref __removeList);
        }
    }

    public struct LandscapeWorld<T> where T : unmanaged, IEquatable<T>
    {
        private UnsafeList<LandscapeLayer<T>> __layers;

        public int countToLoad
        {
            get
            {
                int count = 0, numLayers = __layers.Length;
                for (int i = 0; i < numLayers; ++i)
                    count += __layers[i].countToLoad;

                return count;
            }
        }

        public int countToUnload
        {
            get
            {
                int count = 0, numLayers = __layers.Length;
                for (int i = 0; i < numLayers; ++i)
                    count += __layers[i].countToUnload;

                return count;
            }
        }

        public LandscapeWorld(int layerCount, in AllocatorManager.AllocatorHandle allocator)
        {
            __layers = new UnsafeList<LandscapeLayer<T>>(layerCount, allocator);
            __layers.Resize(layerCount, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < layerCount; ++i)
                __layers[i] = new LandscapeLayer<T>(allocator);
        }

        public void Dispose()
        {
            int numLayers = __layers.Length;
            for (int i = 0; i < numLayers; ++i)
                __layers[i].Dispose();

            __layers.Dispose();
        }

        public int GetCountToLoad(int layerIndex, int minDistance = int.MaxValue)
        {
            return __layers[layerIndex].GetCountToLoad(minDistance);
        }

        public int GetCountToUnload(int layerIndex, int maxDistance = int.MinValue)
        {
            return __layers[layerIndex].GetCountToUnload(maxDistance);
        }

        public int GetMaxDistanceToUnload(out int layerIndex, out T layerPosition, int maxDistance = int.MinValue)
        {
            layerIndex = -1;
            layerPosition = default;

            int numLayers = __layers.Length, distance, i;
            T position;

            for (i = 0; i < numLayers; ++i)
            {
                ref var layer = ref __layers.ElementAt(i);
                distance = layer.GetMaxDistanceToUnload(out position, maxDistance);
                if (distance > maxDistance)
                {
                    layerIndex = i;

                    maxDistance = distance;

                    layerPosition = position;
                }
            }

            return maxDistance;
        }

        public int GetMinDistanceToLoad(out int layerIndex, out T layerPosition, int minDistance = int.MaxValue)
        {
            layerIndex = -1;
            layerPosition = default;

            int numLayers = __layers.Length, distance, i;
            T position;

            for (i = 0; i < numLayers; ++i)
            {
                ref var layer = ref __layers.ElementAt(i);
                distance = layer.GetMinDistanceToLoad(out position, minDistance);
                if (distance < minDistance)
                {
                    layerIndex = i;

                    minDistance = distance;

                    layerPosition = position;
                }
            }

            return minDistance;
        }

        public bool Load(int layerIndex, out T layerPosition, int minDistance = int.MaxValue)
        {
            ref var layer = ref __layers.ElementAt(layerIndex);

            return layer.GetMinDistanceToLoad(out layerPosition, minDistance) < minDistance &&
                   layer.Load(layerPosition);
        }

        public bool Load(out int layerIndex, out T layerPosition, int minDistance = int.MaxValue)
        {
            while (GetMinDistanceToLoad(out layerIndex, out layerPosition, minDistance) < minDistance)
            {
                if (__layers[layerIndex].Load(layerPosition))
                    return true;
            }

            return false;
        }

        public bool Unload(int layerIndex, out T layerPosition, int maxDistance = int.MinValue)
        {
            ref var layer = ref __layers.ElementAt(layerIndex);

            return layer.GetMaxDistanceToUnload(out layerPosition, maxDistance) > maxDistance &&
                   layer.Unload(layerPosition);
        }

        public bool Unload(out int layerIndex, out T layerPosition, int maxDistance = int.MinValue)
        {
            while (GetMaxDistanceToUnload(out layerIndex, out layerPosition, maxDistance) > maxDistance)
            {
                if (__layers[layerIndex].Unload(layerPosition))
                    return true;
            }

            return false;
        }

        public LandscapeLoaderCompleteType Complete(bool isLoading, int layerIndex, in T position)
        {
            return __layers[layerIndex].Complete(isLoading, position);
        }

        public void Restore()
        {
            int numLayers = __layers.Length;
            for (int i = 0; i < numLayers; ++i)
                __layers[i].Restore();
        }
        
        public void Apply<TWrapper>(
            int layerIndex,
            ref TWrapper wrapper) where TWrapper : ILandscapeWrapper<T>
        {
            __layers[layerIndex].Apply(ref wrapper);
        }
    }

    public struct LandscapeManager<TKey, TValue>
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged, IEquatable<TValue>
    {
        public struct Writer
        {
            public readonly AllocatorManager.AllocatorHandle Allocator;

            private SharedHashMap<TKey, LandscapeWorld<TValue>>.Writer __value;

            public Writer(ref LandscapeManager<TKey, TValue> manager)
            {
                Allocator = manager.__worlds.Allocator;

                __value = manager.__worlds.writer;
            }

            public void Restore()
            {
                if (!__value.isEmpty)
                {
                    using (var keys = __value.GetKeyArray(Unity.Collections.Allocator.Temp))
                    {
                        TKey key;
                        int length = keys.Length;
                        for (int i = 0; i < length; ++i)
                        {
                            key = keys[i];
                            if (!__value.ContainsKey(key))
                            {
                                __value[key].Restore();

                                //__value.Remove(key);
                            }
                        }
                    }
                }
            }

            public LandscapeWorld<TValue> GetOrCreate(in TKey key, int layers)
            {
                if (!__value.TryGetValue(key, out var world))
                {
                    world = new LandscapeWorld<TValue>(layers, Allocator);

                    __value[key] = world;
                }

                return world;
            }
        }
        
        private SharedHashMap<TKey, LandscapeWorld<TValue>> __worlds;

        public bool isCreated => __worlds.isCreated;

        public ref LookupJobManager lookupJobManager => ref __worlds.lookupJobManager;

        public Writer writer => new Writer(ref this);

        public LandscapeManager(Allocator allocator)
        {
            __worlds = new SharedHashMap<TKey, LandscapeWorld<TValue>>(allocator);
        }

        public void Dispose()
        {
            using (var worlds = __worlds.writer.GetValueArray(Allocator.Temp))
            {
                int numWorlds = worlds.Length;
                for (int i = 0; i < numWorlds; ++i)
                    worlds[i].Dispose();
            }

            __worlds.Dispose();
        }

        public int CountToLoad(in TKey key)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.countToLoad;

            return 0;
        }

        public int CountToUnload(in TKey key)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.countToUnload;

            return 0;
        }

        public int GetCountToLoad(in TKey key, int layerIndex,
            int minDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetCountToLoad(layerIndex, minDistance);

            return 0;
        }

        public int GetCountToUnload(in TKey key, int layerIndex,
            int maxDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetCountToUnload(layerIndex, maxDistance);

            return 0;
        }

        public int GetMaxDistanceToUnload(in TKey key, out int layerIndex,
            out TValue position, int maxDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetMaxDistanceToUnload(out layerIndex, out position, maxDistance);

            layerIndex = -1;
            position = default;

            return maxDistance;
        }

        public int GetMinDistanceToLoad(in TKey key, out int layerIndex,
            out TValue position, int minDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetMaxDistanceToUnload(out layerIndex, out position, minDistance);

            layerIndex = -1;
            position = default;

            return minDistance;
        }

        public bool Load(in TKey key, int layerIndex, out TValue position,
            int minDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Load(layerIndex, out position, minDistance);

            position = default;

            return false;
        }

        public bool Load(in TKey key, out int layerIndex, out TValue position,
            int minDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Load(out layerIndex, out position, minDistance);

            layerIndex = -1;
            position = default;

            return false;
        }

        public bool Unload(in TKey key, int layerIndex, out TValue position,
            int maxDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Unload(layerIndex, out position, maxDistance);

            position = default;

            return false;
        }

        public bool Unload(in TKey key, out int layerIndex, out TValue position,
            int maxDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Unload(out layerIndex, out position, maxDistance);

            layerIndex = -1;
            position = default;

            return false;
        }

        public LandscapeLoaderCompleteType Complete(
            in TKey key,
            bool isLoading,
            int layerIndex,
            in TValue position)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            var writer = __worlds.writer;
            if (writer.TryGetValue(key, out var world))
                return world.Complete(isLoading, layerIndex, position);

            return LandscapeLoaderCompleteType.Error;
        }
    }
}