using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using ZG.Mathematics;
using UnityEngine.UIElements;

namespace ZG
{
    [Serializable]
    public struct LandscapeSection
    {
        public int horizontal;
        public int top;
        public int bottom;

        public bool isVail => horizontal >= 0 && top >= 0 && bottom >= 0;
    }

    public struct LandscapeLayer
    {
        public int3 segments;
        public float3 size;

        public bool GetPosition(ref float3 position, out int3 result)
        {
            position = (position / size + math.float3(0.5f, 0.0f, 0.5f)) * segments;

            result = (int3)math.floor(position);

            return result.x >= 0 && result.x < segments.x && result.y >= 0 && result.y < segments.y && result.z >= 0 && result.z < segments.z;
        }

        public void FindNeighbor(
            in LandscapeSection section,
            in NativeArray<float3> positions,
            in UnsafeParallelHashSet<int3> origins,
            ref UnsafeParallelHashMap<int3, int> addList,
            ref UnsafeParallelHashMap<int3, int> removeList)
        {
            addList.Clear();

            int numPositions = positions.Length; 
            float3 position;
            int3 result;
            if (section.isVail)
            {
                int3 min = int3.zero, max = segments - 1;
                for (int i = 0; i < numPositions; ++i)
                {
                    position = positions[i];
                    if (!GetPosition(ref position, out result))
                        continue;

                    FindNeighbor(
                        section,
                        result,
                        segments,
                        ref addList);

                    position -= 0.5f;

                    FindNeighbor(
                        result,
                        math.clamp((int3)math.floor(position), min, max),
                        math.clamp((int3)math.ceil(position), min, max),
                        ref addList);
                }
            }

            removeList.Clear();

            if (origins.IsCreated)
            {
                int minLength, length;
                int3 key, distance;
                var originsEnumerator = origins.GetEnumerator();
                while(originsEnumerator.MoveNext())
                {
                    key = originsEnumerator.Current;

                    minLength = int.MaxValue;
                    for (int i = 0; i < numPositions; ++i)
                    {
                        position = positions[i];
                        if (!GetPosition(ref position, out result))
                            continue;

                        distance = math.abs(result - key);

                        length = distance.x * distance.x + distance.y * distance.y + distance.z * distance.z;

                        if (length < minLength)
                            minLength = length;
                    }

                    removeList[key] = minLength;
                }

                //removeList.UnionWith(origins);

                //removeList.ExceptWith(addList);

                var enumerator = addList.GetEnumerator();
                while (enumerator.MoveNext())
                    removeList.Remove(enumerator.Current.Key);

                originsEnumerator = origins.GetEnumerator();
                while (originsEnumerator.MoveNext())
                    addList.Remove(originsEnumerator.Current);

                //addList.ExceptWith(origins);
            }
        }

        public static void FindNeighbor(
            in LandscapeSection section,
            in int3 position,
            in int3 segments,
            ref UnsafeParallelHashMap<int3, int> positions)
        {
            FindNeighbor(
                position,
                math.max(position - math.int3(section.horizontal, section.bottom, section.horizontal), 0),
                math.min(position + math.int3(section.horizontal, section.top, section.horizontal), segments - 1),
                ref positions);
        }

        public static void FindNeighbor(
            in int3 position,
            in int3 start,
            in int3 end,
            ref UnsafeParallelHashMap<int3, int> positions)
        {
            int i, j, k, source, destination;
            int3 point, distance;
            for (i = start.x; i <= end.x; ++i)
            {
                for (j = start.y; j <= end.y; ++j)
                {
                    for (k = start.z; k <= end.z; ++k)
                    {
                        point = math.int3(i, j, k);
                        distance = math.abs(point - position);
                        destination = distance.x * distance.x + distance.y * distance.y + distance.z * distance.z;
                        if (!positions.TryGetValue(point, out source) || source > destination)
                            positions[point] = destination;
                    }
                }
            }
        }
    }

    public struct LandscapeDefinition
    {
        public struct Level
        {
            public BlobArray<LandscapeSection> sections;
        }

        public BlobArray<LandscapeLayer> layers;
        public BlobArray<Level> levels;
    }

    public struct LandscapeInput
    {
        public uint layerMask;
        public float3 position;
    }

    public struct LandscapeManager
    {
        public enum CompleteType
        {
            Error, 
            Done,
            Reverse
        }

        private struct Layer
        {
            [Flags]
            public enum Status
            {
                Loading = 0x00, 
                Unloading = 0x01,
                ReverseAfterDone = 0x02
            }

            private UnsafeParallelHashMap<int3, Status> __states;
            private UnsafeParallelHashMap<int3, int> __addList;
            private UnsafeParallelHashMap<int3, int> __removeList;
            private UnsafeParallelHashSet<int3> __origins;

            public bool isEmtpy => __states.IsEmpty && __addList.IsEmpty && __removeList.IsEmpty && __origins.IsEmpty;

            public int countToLoad => __addList.Count();

            public int countToUnload => __removeList.Count();

            public int GetCountToLoad(int minDistance = int.MaxValue)
            {
                int count = 0;
                var enumerator = __addList.GetEnumerator();
                while(enumerator.MoveNext())
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

            public Layer(Allocator allocator)
            {
                __states = new UnsafeParallelHashMap<int3, Status>(1, allocator);
                __addList = new UnsafeParallelHashMap<int3, int>(1, allocator);
                __removeList = new UnsafeParallelHashMap<int3, int>(1, allocator);
                __origins = new UnsafeParallelHashSet<int3>(1, allocator);
            }

            public void Dispose()
            {
                __states.Dispose();
                __origins.Dispose();
                __addList.Dispose();
                __removeList.Dispose();
            }

            public int GetMinDistanceToLoad(out int3 position, int minDistance = int.MaxValue)
            {
                position = int3.zero;

                int distance;
                KeyValue<int3, int> keyValue;
                var enumerator = __addList.GetEnumerator();
                while(enumerator.MoveNext())
                {
                    keyValue = enumerator.Current;
                    distance = keyValue.Value;
                    if(distance < minDistance)
                    {
                        minDistance = distance;

                        position = keyValue.Key;
                    }
                }

                return minDistance;
            }

            public int GetMaxDistanceToUnload(out int3 position, int maxDistance = int.MinValue)
            {
                position = int3.zero;

                int distance;
                KeyValue<int3, int> keyValue;
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

            public bool Load(in int3 position)
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

            public bool Unload(in int3 position)
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

                        return true;// __origins.Remove(position);
                    }
                }

                return false;
            }

            public CompleteType Complete(bool isLoading, in int3 position)
            {
                if (__states.TryGetValue(position, out var status))
                {
                    if ((status & Status.ReverseAfterDone) == Status.ReverseAfterDone)
                    {
                        status &= ~Status.ReverseAfterDone;

                        if (status == Status.Loading)
                        {
                            if (!isLoading)
                                return CompleteType.Error;

                            status = Status.Unloading;

                            __states[position] = status;
                        }
                        else if (status == Status.Unloading)
                        {
                            if (isLoading)
                                return CompleteType.Error;

                            status = Status.Loading;

                            __states[position] = status;
                        }
                        else
                            return CompleteType.Error;

                        return CompleteType.Reverse;
                    }
                    else if(status == Status.Loading)
                    {
                        if (!isLoading)
                            return CompleteType.Error;

                        __states.Remove(position);

                        return CompleteType.Done;
                    }
                    else if (status == Status.Unloading)
                    {
                        if (isLoading)
                            return CompleteType.Error;

                        __states.Remove(position);

                        return CompleteType.Done;
                    }

                    return CompleteType.Error;
                }

                return CompleteType.Error;
            }

            public void Apply(
                in LandscapeLayer layer, 
                in LandscapeSection section,
                in NativeArray<float3> positions)
            {
                layer.FindNeighbor(section, positions, __origins, ref __addList, ref __removeList);
            }

            public void Restore()
            {
                __addList.Clear();

                __removeList.Clear();

                var enumerator = __origins.GetEnumerator();
                while (enumerator.MoveNext())
                    __removeList.Add(enumerator.Current, int.MaxValue);
            }
        }

        public struct World
        {
            private UnsafeList<Layer> __layers;

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

            public World(int layerCount, Allocator allocator)
            {
                __layers = new UnsafeList<Layer>(layerCount, allocator);
                __layers.Resize(layerCount, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < layerCount; ++i)
                    __layers[i] = new Layer(allocator);
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

            public int GetMaxDistanceToUnload(out int layerIndex, out int3 layerPosition, int maxDistance = int.MinValue)
            {
                layerIndex = -1;
                layerPosition = int3.zero;

                int numLayers = __layers.Length, distance, i;
                int3 position;

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

            public int GetMinDistanceToLoad(out int layerIndex, out int3 layerPosition, int minDistance = int.MaxValue)
            {
                layerIndex = -1;
                layerPosition = int3.zero;

                int numLayers = __layers.Length, distance, i;
                int3 position;

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

            public bool Load(int layerIndex, out int3 layerPosition, int minDistance = int.MaxValue)
            {
                ref var layer = ref __layers.ElementAt(layerIndex);

                return layer.GetMinDistanceToLoad(out layerPosition, minDistance) < minDistance && layer.Load(layerPosition);
            }

            public bool Load(out int layerIndex, out int3 layerPosition, int minDistance = int.MaxValue)
            {
                while (GetMinDistanceToLoad(out layerIndex, out layerPosition, minDistance) < minDistance)
                {
                    if (__layers[layerIndex].Load(layerPosition))
                        return true;
                }

                return false;
            }

            public bool Unload(int layerIndex, out int3 layerPosition, int maxDistance = int.MinValue)
            {
                ref var layer = ref __layers.ElementAt(layerIndex);

                return layer.GetMaxDistanceToUnload(out layerPosition, maxDistance) > maxDistance && layer.Unload(layerPosition);
            }

            public bool Unload(out int layerIndex, out int3 layerPosition, int maxDistance = int.MinValue)
            {
                while (GetMaxDistanceToUnload(out layerIndex, out layerPosition, maxDistance) > maxDistance)
                {
                    if (__layers[layerIndex].Unload(layerPosition))
                        return true;
                }

                return false;
            }

            public CompleteType Complete(bool isLoading, int layerIndex, in int3 position)
            {
                return __layers[layerIndex].Complete(isLoading, position);
            }

            public void Apply(
                int layerIndex,
                in LandscapeLayer layer,
                in LandscapeSection section,
                in NativeArray<float3> positions)
            {
                __layers[layerIndex].Apply(layer, section, positions);
            }

            public void Restore()
            {
                int numLayers = __layers.Length;
                for (int i = 0; i < numLayers; ++i)
                    __layers[i].Restore();
            }
        }

        public struct Writer
        {
            public readonly Allocator Allocator;

            private SharedHashMap<BlobAssetReference<LandscapeDefinition>, World>.Writer __value;

            public Writer(ref LandscapeManager manager)
            {
                Allocator = manager.__worlds.Allocator;

                __value = manager.__worlds.writer;
            }

            public void Apply(int levelIndex, in NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput> values)
            {
                BlobAssetReference<LandscapeDefinition> key;
                if (!__value.isEmpty)
                {
                    using (var keys = __value.GetKeyArray(Allocator.Temp))
                    {
                        int length = keys.Length;
                        for (int i = 0; i < length; ++i)
                        {
                            key = keys[i];
                            if (!values.ContainsKey(key))
                            {
                                __value[key].Restore();

                                //__value.Remove(key);
                            }
                        }
                    }
                }

                if (!values.IsEmpty)
                {
                    using (var keys = values.GetKeyArray(Allocator.Temp))
                    {
                        var positionList = new NativeList<float3>(Allocator.Temp);

                        NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput>.Enumerator enumerator;
                        LandscapeInput value;
                        World world;
                        int i, j, numLayers, count = keys.ConvertToUniqueArray();
                        for (i = 0; i < count; ++i)
                        {
                            key = keys[i];

                            ref var definition = ref key.Value;
                            numLayers = definition.layers.Length;
                            if (!__value.TryGetValue(key, out world))
                            {
                                world = new World(numLayers, Allocator);

                                __value[key] = world;
                            }

                            ref var level = ref definition.levels[math.clamp(levelIndex, 0, definition.levels.Length - 1)];
                            for (j = 0; j < numLayers; ++j)
                            {
                                positionList.Clear();

                                enumerator = values.GetValuesForKey(key);
                                while (enumerator.MoveNext())
                                {
                                    value = enumerator.Current;
                                    if((value.layerMask & (1 << j)) != 0)
                                        positionList.Add(value.position);
                                }

                                world.Apply(j, definition.layers[j], level.sections[j], positionList.AsArray());
                            }
                        }

                        positionList.Dispose();
                    }
                }
            }
        }

        private SharedHashMap<BlobAssetReference<LandscapeDefinition>, World> __worlds;

        public bool isCreated => __worlds.isCreated;

        public ref LookupJobManager lookupJobManager => ref __worlds.lookupJobManager;

        public Writer writer => new Writer(ref this);

        public LandscapeManager(Allocator allocator)
        {
            __worlds = new SharedHashMap<BlobAssetReference<LandscapeDefinition>, World>(allocator);
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

        public int CountToLoad(in BlobAssetReference<LandscapeDefinition> key)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.countToLoad;

            return 0;
        }

        public int CountToUnload(in BlobAssetReference<LandscapeDefinition> key)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.countToUnload;

            return 0;
        }

        public int GetCountToLoad(in BlobAssetReference<LandscapeDefinition> key, int layerIndex, int minDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetCountToLoad(layerIndex, minDistance);

            return 0;
        }

        public int GetCountToUnload(in BlobAssetReference<LandscapeDefinition> key, int layerIndex, int maxDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetCountToUnload(layerIndex, maxDistance);

            return 0;
        }

        public int GetMaxDistanceToUnload(in BlobAssetReference<LandscapeDefinition> key, out int layerIndex, out int3 position, int maxDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetMaxDistanceToUnload(out layerIndex, out position, maxDistance);

            layerIndex = -1;
            position = int3.zero;

            return maxDistance;
        }

        public int GetMinDistanceToLoad(in BlobAssetReference<LandscapeDefinition> key, out int layerIndex, out int3 position, int minDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.GetMaxDistanceToUnload(out layerIndex, out position, minDistance);

            layerIndex = -1;
            position = int3.zero;

            return minDistance;
        }

        public bool Load(in BlobAssetReference<LandscapeDefinition> key, int layerIndex, out int3 position, int minDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if(__worlds.writer.TryGetValue(key, out var world))
                return world.Load(layerIndex, out position, minDistance);

            position = int3.zero;

            return false;
        }

        public bool Load(in BlobAssetReference<LandscapeDefinition> key, out int layerIndex, out int3 position, int minDistance = int.MaxValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Load(out layerIndex, out position, minDistance);

            layerIndex = -1;
            position = int3.zero;

            return false;
        }

        public bool Unload(in BlobAssetReference<LandscapeDefinition> key, int layerIndex, out int3 position, int maxDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Unload(layerIndex, out position, maxDistance);

            position = int3.zero;

            return false;
        }

        public bool Unload(in BlobAssetReference<LandscapeDefinition> key, out int layerIndex, out int3 position, int maxDistance = int.MinValue)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            if (__worlds.writer.TryGetValue(key, out var world))
                return world.Unload(out layerIndex, out position, maxDistance);

            layerIndex = -1;
            position = int3.zero;

            return false;
        }

        public CompleteType Complete(
            in BlobAssetReference<LandscapeDefinition> key, 
            bool isLoading, 
            int layerIndex, 
            in int3 position)
        {
            __worlds.lookupJobManager.CompleteReadWriteDependency();

            var writer = __worlds.writer;
            if (writer.TryGetValue(key, out var world))
                return world.Complete(isLoading, layerIndex, position);

            return CompleteType.Error;
        }
    }

    public struct LandscapeData : IComponentData//, ISharedComponentData
    {
        public uint layerMask;
        public BlobAssetReference<LandscapeDefinition> definition;
    }

    public struct LandscapeLevel : IComponentData
    {
        public int index;
    }

    [BurstCompile]
    public partial struct LandscapeSystem : ISystem
    {
        private struct Collect
        {
            [ReadOnly]
            public NativeArray<LandscapeData> instances;
            [ReadOnly]
            public NativeArray<Translation> translations;
            public NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput>.ParallelWriter values;

            public void Execute(int index)
            {
                var instance = instances[index];
                if (!instance.definition.IsCreated)
                    return;

                LandscapeInput value;
                value.layerMask = instance.layerMask;
                value.position = translations[index].Value;

                values.Add(instance.definition, value);
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<LandscapeData> instanceType;
            [ReadOnly]
            public ComponentTypeHandle<Translation> translationType;
            public NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput>.ParallelWriter values;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.translations = chunk.GetNativeArray(ref translationType);
                collect.values = values;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);
            }
        }

        [BurstCompile]
        private struct Apply : IJob
        {
            public int levelIndex;

            //[ReadOnly]
            public NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput> positions;

            public LandscapeManager.Writer manager;

            public void Execute()
            {
                manager.Apply(levelIndex, positions);

                positions.Clear();
            }
        }

        public LandscapeManager manager
        {
            get;

            private set;
        }

        private EntityQuery __levelGroup;
        private EntityQuery __group;
        private NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput> __values;

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJob<Apply>();

            state.SetAlwaysUpdateSystem(true);

            __levelGroup = state.GetEntityQuery(ComponentType.ReadOnly<LandscapeLevel>());

            __group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All =
                        new ComponentType[]
                        {
                            ComponentType.ReadOnly<LandscapeData>(),
                            ComponentType.ReadOnly<Translation>()
                        },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __values = new NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput>(1, Allocator.Persistent);

            manager = new LandscapeManager(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            __values.Dispose();

            manager.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            /*if (__group.IsEmptyIgnoreFilter)
                return;*/

            NativeParallelMultiHashMap<BlobAssetReference<LandscapeDefinition>, LandscapeInput> values = __values;
            values.Capacity = math.max(values.Capacity, __group.CalculateEntityCount());

            CollectEx collect;
            collect.instanceType = state.GetComponentTypeHandle<LandscapeData>(true);
            collect.translationType = state.GetComponentTypeHandle<Translation>(true);
            collect.values = values.AsParallelWriter();
            var jobHandle = collect.ScheduleParallel(__group, state.Dependency);

            ref var lookupJobManager = ref manager.lookupJobManager;
            lookupJobManager.CompleteReadWriteDependency();

            Apply apply;
            apply.levelIndex = __levelGroup.IsEmptyIgnoreFilter ? 0 : __levelGroup.GetSingleton<LandscapeLevel>().index;
            apply.positions = values;
            apply.manager = manager.writer;

            jobHandle = apply.Schedule(jobHandle);

            lookupJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }
}