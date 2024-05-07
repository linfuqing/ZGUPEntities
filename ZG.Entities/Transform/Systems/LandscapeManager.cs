using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

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
        T Current { get; }
        
        bool MoveNext();

        float DistanceTo(in T value);
    }

    public interface ILandscapeWorld<T> where T : unmanaged, IEquatable<T>
    {
        int countToLoad { get; }

        int countToUnload { get; }

        int GetCountToLoad(int layerIndex, float minDistance = float.MinValue);

        int GetCountToUnload(int layerIndex, float maxDistance = float.MaxValue);

        float GetMaxDistanceToUnload(out int layerIndex, out T position, float maxDistance = float.MinValue);

        float GetMinDistanceToLoad(out int layerIndex, out T position, float minDistance = int.MaxValue);

        bool Load(int layerIndex, out T position, float minDistance = float.MaxValue);

        bool Load(out int layerIndex, out T position, float minDistance = float.MaxValue);

        bool Unload(int layerIndex, out T position, float maxDistance = float.MinValue);

        bool Unload(out int layerIndex, out T position, float maxDistance = float.MinValue);

        LandscapeLoaderCompleteType Complete(bool isLoading, int layerIndex, in T position);
    }
    
    public interface ILandscapeManager<TKey, TValue>
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged, IEquatable<TValue>
    {
        int CountToLoad(in TKey key);

        int CountToUnload(in TKey key);

        int GetCountToLoad(in TKey key, int layerIndex,
            float minDistance = float.MinValue);

        int GetCountToUnload(in TKey key, int layerIndex,
            float maxDistance = float.MaxValue);

        float GetMaxDistanceToUnload(in TKey key, out int layerIndex,
            out TValue position, float maxDistance = float.MinValue);

        float GetMinDistanceToLoad(in TKey key, out int layerIndex,
            out TValue position, float minDistance = int.MaxValue);

        bool Load(in TKey key, int layerIndex, out TValue position,
            float minDistance = float.MaxValue);

        bool Load(in TKey key, out int layerIndex, out TValue position,
            float minDistance = float.MaxValue);

        bool Unload(in TKey key, int layerIndex, out TValue position,
            float maxDistance = float.MinValue);

        bool Unload(in TKey key, out int layerIndex, out TValue position,
            float maxDistance = float.MinValue);

        LandscapeLoaderCompleteType Complete(
            in TKey key,
            bool isLoading,
            int layerIndex,
            in TValue position);
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
        private UnsafeParallelHashMap<T, float> __addList;
        private UnsafeParallelHashMap<T, float> __removeList;
        private UnsafeParallelHashSet<T> __origins;

        public bool isCreated => __states.IsCreated;

        public bool isEmpty => __states.IsEmpty && __addList.IsEmpty && __removeList.IsEmpty && __origins.IsEmpty;

        public int countToLoad => __addList.Count();

        public int countToUnload => __removeList.Count();

        public int GetCountToLoad(float minDistance = int.MaxValue)
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

        public int GetCountToUnload(float maxDistance = int.MinValue)
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
            __addList = new UnsafeParallelHashMap<T, float>(1, allocator);
            __removeList = new UnsafeParallelHashMap<T, float>(1, allocator);
            __origins = new UnsafeParallelHashSet<T>(1, allocator);
        }

        public void Dispose()
        {
            __states.Dispose();
            __origins.Dispose();
            __addList.Dispose();
            __removeList.Dispose();
        }

        public float GetMinDistanceToLoad(out T position, float minDistance = float.MaxValue)
        {
            position = default;

            float distance;
            KeyValue<T, float> keyValue;
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

        public float GetMaxDistanceToUnload(out T position, float maxDistance = float.MinValue)
        {
            position = default;

            float distance;
            KeyValue<T, float> keyValue;
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

        public void Apply<TWrapper>(ref TWrapper wrapper) where TWrapper : ILandscapeWrapper<T>
        {
            __addList.Clear();

            T value;
            while (wrapper.MoveNext())
            {
                value = wrapper.Current;

                __addList[value] = wrapper.DistanceTo(value);
            }

            __removeList.Clear();

            if (__origins.IsCreated)
            {
                foreach (var source in __origins)
                {
                    if(__addList.Remove(source))
                        continue;
                    
                    __removeList[source] = wrapper.DistanceTo(source);
                }
            }
        }
    }

    public struct LandscapeWorld<T> where T : unmanaged, IEquatable<T>
    {
        private UnsafeList<LandscapeLayer<T>> __layers;

        public bool isCreated => __layers.IsCreated;

        public int layerCount => __layers.Length;

        public int countToLoad
        {
            get
            {
                LandscapeLayer<T> layer;
                int count = 0, numLayers = __layers.Length;
                for (int i = 0; i < numLayers; ++i)
                    count += __ReadOnly(i, out layer) ? layer.countToLoad : 0;

                return count;
            }
        }

        public int countToUnload
        {
            get
            {
                LandscapeLayer<T> layer;
                int count = 0, numLayers = __layers.Length;
                for (int i = 0; i < numLayers; ++i)
                    count += __ReadOnly(i, out layer) ? layer.countToUnload : 0;

                return count;
            }
        }

        public LandscapeWorld(in AllocatorManager.AllocatorHandle allocator, int layerCount = 0)
        {
            __layers = new UnsafeList<LandscapeLayer<T>>(layerCount, allocator);
            __layers.Resize(layerCount, NativeArrayOptions.ClearMemory);
        }

        public void Dispose()
        {
            int numLayers = __layers.Length;
            for (int i = 0; i < numLayers; ++i)
                __layers[i].Dispose();

            __layers.Dispose();
        }

        public void Reset(int layerCount)
        {
            Restore();

            int length = __layers.Length;
            if (length <= layerCount)
                __layers.Resize(layerCount, NativeArrayOptions.ClearMemory);
        }

        public int GetCountToLoad(int layerIndex, float minDistance = int.MaxValue)
        {
            return __ReadOnly(layerIndex, out var layer) ? layer.GetCountToLoad(minDistance) : 0;
        }

        public int GetCountToUnload(int layerIndex, float maxDistance = int.MinValue)
        {
            return __ReadOnly(layerIndex, out var layer) ? layer.GetCountToUnload(maxDistance) : 0;
        }

        public float GetMaxDistanceToUnload(out int layerIndex, out T layerPosition, float maxDistance = int.MinValue)
        {
            layerIndex = -1;
            layerPosition = default;

            int numLayers = __layers.Length, i;
            float distance;
            T position;

            for (i = 0; i < numLayers; ++i)
            {
                ref var layer = ref __layers.ElementAt(i);
                if(!layer.isCreated)
                    continue;

                if (i == 1)
                    i = 1;
                
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

        public float GetMinDistanceToLoad(out int layerIndex, out T layerPosition, float minDistance = int.MaxValue)
        {
            layerIndex = -1;
            layerPosition = default;

            int numLayers = __layers.Length, i;
            float distance;
            T position;

            for (i = 0; i < numLayers; ++i)
            {
                ref var layer = ref __layers.ElementAt(i);
                if(!layer.isCreated)
                    continue;

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

        public bool Load(int layerIndex, out T layerPosition, float minDistance = float.MaxValue)
        {
            ref var layer = ref __ReadWrite(layerIndex);

            return layer.GetMinDistanceToLoad(out layerPosition, minDistance) < minDistance &&
                   layer.Load(layerPosition);
        }

        public bool Load(out int layerIndex, out T layerPosition, float minDistance = float.MaxValue)
        {
            while (GetMinDistanceToLoad(out layerIndex, out layerPosition, minDistance) < minDistance)
            {
                if (__ReadWrite(layerIndex).Load(layerPosition))
                    return true;
            }

            return false;
        }

        public bool Unload(int layerIndex, out T layerPosition, float maxDistance = float.MinValue)
        {
            ref var layer = ref __ReadWrite(layerIndex);

            return layer.GetMaxDistanceToUnload(out layerPosition, maxDistance) > maxDistance &&
                   layer.Unload(layerPosition);
        }

        public bool Unload(out int layerIndex, out T layerPosition, float maxDistance = float.MinValue)
        {
            while (GetMaxDistanceToUnload(out layerIndex, out layerPosition, maxDistance) > maxDistance)
            {
                if (__ReadWrite(layerIndex).Unload(layerPosition))
                    return true;
            }

            return false;
        }

        public LandscapeLoaderCompleteType Complete(bool isLoading, int layerIndex, in T position)
        {
            return __ReadWrite(layerIndex).Complete(isLoading, position);
        }

        public void Restore()
        {
            int numLayers = __layers.Length;
            for (int i = 0; i < numLayers; ++i)
            {
                ref var layer = ref __layers.ElementAt(i);
                if(!layer.isCreated)
                    continue;
                
                __layers[i].Restore();
            }
        }
        
        public void Apply<TWrapper>(
            int layerIndex,
            ref TWrapper wrapper) where TWrapper : ILandscapeWrapper<T>
        {
            __ReadWrite(layerIndex).Apply(ref wrapper);
        }

        private bool __ReadOnly(int layerIndex, out LandscapeLayer<T> layer)
        {
            if (__layers.Length > layerIndex)
            {
                layer = __layers[layerIndex];

                return layer.isCreated;
            }

            layer = default;

            return false;
        }

        private ref LandscapeLayer<T> __ReadWrite(int layerIndex)
        {
            if (__layers.Length <= layerIndex)
            {
                int length = __layers.Length;
                __layers.Resize(layerIndex + 1);
                for (int i = length; i < layerIndex; ++i)
                    __layers[i] = default;
            }

            ref var layer = ref __layers.ElementAt(layerIndex);
            if (!layer.isCreated)
                layer = new LandscapeLayer<T>(__layers.Allocator);

            return ref layer;
        }
    }

    public struct LandscapeManager<TKey, TValue> : ILandscapeManager<TKey, TValue>
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
                foreach (var value in __value)
                    value.Value.Restore();
                
                /*if (!__value.isEmpty)
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
                }*/
            }

            public LandscapeWorld<TValue> GetOrCreate(in TKey key, int layers = 0)
            {
                if (!__value.TryGetValue(key, out var world))
                {
                    world = new LandscapeWorld<TValue>(Allocator, layers);

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
            float minDistance = float.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetCountToLoad(layerIndex, minDistance);

            return 0;
        }

        public int GetCountToUnload(in TKey key, int layerIndex,
            float maxDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetCountToUnload(layerIndex, maxDistance);

            return 0;
        }

        public float GetMaxDistanceToUnload(in TKey key, out int layerIndex,
            out TValue position, float maxDistance = float.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetMaxDistanceToUnload(out layerIndex, out position, maxDistance);

            layerIndex = -1;
            position = default;

            return maxDistance;
        }

        public float GetMinDistanceToLoad(in TKey key, out int layerIndex,
            out TValue position, float minDistance = float.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetMaxDistanceToUnload(out layerIndex, out position, minDistance);

            layerIndex = -1;
            position = default;

            return minDistance;
        }

        public bool Load(in TKey key, int layerIndex, out TValue position,
            float minDistance = float.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Load(layerIndex, out position, minDistance);

            position = default;

            return false;
        }

        public bool Load(in TKey key, out int layerIndex, out TValue position,
            float minDistance = float.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Load(out layerIndex, out position, minDistance);

            layerIndex = -1;
            position = default;

            return false;
        }

        public bool Unload(in TKey key, int layerIndex, out TValue position,
            float maxDistance = float.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Unload(layerIndex, out position, maxDistance);

            position = default;

            return false;
        }

        public bool Unload(in TKey key, out int layerIndex, out TValue position,
            float maxDistance = float.MinValue)
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