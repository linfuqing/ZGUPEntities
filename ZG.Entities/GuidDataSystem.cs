using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using ZG.Unsafe;

namespace ZG
{
    public struct DataBuffer<TKey, TValue> 
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        private readonly TKey __key;
        private UnsafeList<TValue> __values;
        private NativeParallelHashMap<TKey, UnsafeList<TValue>> __map;

        internal DataBuffer(in TKey key, ref UnsafeList<TValue> values, ref NativeParallelHashMap<TKey, UnsafeList<TValue>> map)
        {
            __key = key;
            __values = values;
            __map = map;
        }

        public void Add(in TValue value)
        {
            UnityEngine.Assertions.Assert.IsTrue(__map.ContainsKey(__key));

            __values.Add(value);

            __map[__key] = __values;
        }

        public unsafe void AddRange(in TValue[] values)
        {
            UnityEngine.Assertions.Assert.IsTrue(__map.ContainsKey(__key));

            fixed(void* ptr = values)
                __values.AddRange(ptr, values.Length);

            __map[__key] = __values;
        }

        public NativeArray<TValue> AsArray()
        {
            return __values.ToNativeArray();
        }
    }

    [UpdateInGroup(typeof(EntityCommandSharedSystemGroup))]
    public partial class DataSystem<T> : SystemBase where T : unmanaged, IEquatable<T>
    {
        private interface ICommander : IDisposable
        {
            bool Contains(in T key);

            bool Remove(in T key);

            JobHandle Execute(DataSystem<T> system, in NativeArray<Item> items, in JobHandle jobHandle);
        }

        private struct Item
        {
            public T key;
            public Entity entity;
        }

        private struct Commander
        {
            public JobHandle jobHandle;
            public ICommander value;
        }

        private class CompoenntDataCommander<U> : ICommander where U : unmanaged, IComponentData
        {
            [BurstCompile]
            private struct Copy : IJobParallelFor
            {
                [ReadOnly]
                public NativeArray<Item> items;

                [ReadOnly]
                public NativeParallelHashMap<T, U> values;

                [NativeDisableParallelForRestriction]
                public ComponentLookup<U> outputs;

                public void Execute(int index)
                {
                    Item item = items[index];
                    if (!outputs.HasComponent(item.entity))
                        return;

                    if (!values.TryGetValue(item.key, out U value))
                        return;

                    outputs[item.entity] = value;
                }
            }

            private NativeParallelHashMap<T, U> __values;

            public U this[in T key]
            {
                get => __values[key];

                set => __values[key] = value;
            }

            public CompoenntDataCommander()
            {
                __values = new NativeParallelHashMap<T, U>(1, Allocator.Persistent);
            }

            public void Dispose()
            {
                __values.Dispose();
            }

            public bool Contains(in T key) => __values.ContainsKey(key);

            public bool TryGetValue(in T key, out U value) => __values.TryGetValue(key, out value);

            public bool Remove(in T key) => __values.Remove(key);

            public JobHandle Execute(DataSystem<T> system, in NativeArray<Item> items, in JobHandle jobHandle)
            {
                Copy copy;
                copy.items = items;
                copy.values = __values;
                copy.outputs = system.GetComponentLookup<U>();
                return copy.Schedule(items.Length, system.innerloopBatchCount, jobHandle);
            }
        }

        private class BufferCommander<U> : ICommander where U : unmanaged, IBufferElementData
        {
            [BurstCompile(CompileSynchronously = true)]
            private struct Copy : IJobParallelFor
            {
                [ReadOnly]
                public NativeArray<Item> items;

                [ReadOnly]
                public NativeParallelHashMap<T, UnsafeList<U>> values;

                [NativeDisableParallelForRestriction]
                public BufferLookup<U> outputs;

                public void Execute(int index)
                {
                    Item item = items[index];
                    if (!outputs.HasBuffer(item.entity))
                        return;

                    if (!this.values.TryGetValue(item.key, out var values))
                        return;

                    outputs[item.entity].CopyFrom(values.ToNativeArray<U>());
                }
            }

            private NativeParallelHashMap<T, UnsafeList<U>> __values;

            public DataBuffer<T, U> this[in T key]
            {
                get
                {
                    var values = __values[key];
                    return new DataBuffer<T, U>(key, ref values, ref __values);
                }
            }

            public BufferCommander()
            {
                __values = new NativeParallelHashMap<T, UnsafeList<U>>(1, Allocator.Persistent);
            }

            public void Dispose()
            {
                __values.Dispose();
            }

            public bool Contains(in T key) => __values.ContainsKey(key);

            public bool TryGetValue(in T key, out DataBuffer<T, U> buffer)
            {
                if (!__values.TryGetValue(key, out var values))
                {
                    buffer = default;

                    return false;
                }

                buffer = new DataBuffer<T, U>(key, ref values, ref __values);
                return true;
            }

            public DataBuffer<T, U> Set(in T key)
            {
                Remove(key);

                var values = new UnsafeList<U>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                __values[key] = values;

                return new DataBuffer<T, U>(key, ref values, ref __values);
            }

            public bool Remove(in T key)
            {
                if (!__values.TryGetValue(key, out var values))
                    return false;

                values.Dispose();

                return __values.Remove(key);
            }

            public JobHandle Execute(DataSystem<T> system, in NativeArray<Item> items, in JobHandle jobHandle)
            {
                Copy copy;
                copy.items = items;
                copy.values = __values;
                copy.outputs = system.GetBufferLookup<U>();
                return copy.Schedule(items.Length, system.innerloopBatchCount, jobHandle);
            }
        }

        public int innerloopBatchCount = 16;

        private Dictionary<Type, Commander> __commanders;

        private List<Type> __types;

        private NativeList<Item> __items;

        private NativeList<JobHandle> __jobHandles;

        public void CopyTo(in T key, in Entity entity)
        {
            Item item;
            item.key = key;
            item.entity = entity;
            __items.Add(item);
        }

        public bool HasComponent<U>(in T key)
        {
            if (__commanders != null && __commanders.TryGetValue(typeof(U), out var commander))
            {
                commander.jobHandle.Complete();
                commander.jobHandle = default;
                __commanders[typeof(U)] = commander;

                return commander.value.Contains(key);
            }

            return false;
        }

        public bool TryGetComponentData<U>(in T key, out U value) where U : unmanaged, IComponentData
        {
            if (__commanders != null && __commanders.TryGetValue(typeof(U), out var commander))
            {
                commander.jobHandle.Complete();
                commander.jobHandle = default;
                __commanders[typeof(U)] = commander;

                return ((CompoenntDataCommander<U>)commander.value).TryGetValue(key, out value);
            }

            value = default;

            return false;
        }

        public U PopComponentData<U>(in T key) where U : unmanaged, IComponentData
        {
            var commander = __commanders[typeof(U)];
            commander.jobHandle.Complete();
            commander.jobHandle = default;
            __commanders[typeof(U)] = commander;

            var compoenntDataCommander = (CompoenntDataCommander<U>)commander.value;
            var value = compoenntDataCommander[key];
            compoenntDataCommander.Remove(key);
            return value;
        }

        public U GetComponentData<U>(in T key) where U : unmanaged, IComponentData
        {
            var commander = __commanders[typeof(U)];
            commander.jobHandle.Complete();
            commander.jobHandle = default;
            __commanders[typeof(U)] = commander;

            return ((CompoenntDataCommander<U>)commander.value)[key];
        }

        public void SetComponentData<U>(in T key, U value) where U : unmanaged, IComponentData
        {
            if (__commanders == null)
                __commanders = new Dictionary<Type, Commander>();

            CompoenntDataCommander<U> compoenntDataCommander;
            if (__commanders.TryGetValue(typeof(U), out var commander))
            {
                commander.jobHandle.Complete();
                commander.jobHandle = default;

                compoenntDataCommander = (CompoenntDataCommander<U>)commander.value;
            }
            else
            {
                __types.Add(typeof(U));

                compoenntDataCommander = new CompoenntDataCommander<U>();
                commander.jobHandle = default;
                commander.value = compoenntDataCommander;
            }

            __commanders[typeof(U)] = commander;

            compoenntDataCommander[key] = value;
        }

        public virtual DataBuffer<T, U> SetBuffer<U>(in T key) where U : unmanaged, IBufferElementData
        {
            if (__commanders == null)
                __commanders = new Dictionary<Type, Commander>();

            BufferCommander<U> bufferCommander;
            if (__commanders.TryGetValue(typeof(U), out var commander))
            {
                commander.jobHandle.Complete();
                commander.jobHandle = default;

                bufferCommander = (BufferCommander<U>)commander.value;
            }
            else
            {
                __types.Add(typeof(U));

                bufferCommander = new BufferCommander<U>();
                commander.jobHandle = default;
                commander.value = bufferCommander;
            }

            __commanders[typeof(U)] = commander;

            return bufferCommander.Set(key);
        }

        public DataBuffer<T, U> GetBuffer<U>(in T key) where U : unmanaged, IBufferElementData
        {
            var commander = __commanders[typeof(U)];
            commander.jobHandle.Complete();
            commander.jobHandle = default;
            __commanders[typeof(U)] = commander;

            return ((BufferCommander<U>)commander.value)[key];
        }

        public bool RemoveComponent<U>(in T key) where U : struct
        {
            var commander = __commanders[typeof(U)];
            commander.jobHandle.Complete();
            commander.jobHandle = default;
            __commanders[typeof(U)] = commander;

            return commander.value.Remove(key);
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            __types = new List<Type>();

            __items = new NativeList<Item>(Allocator.Persistent);

            __jobHandles = new NativeList<JobHandle>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (__commanders != null)
            {
                foreach (var commander in __commanders.Values)
                    commander.value.Dispose();
            }

            __items.Dispose();

            __jobHandles.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (__items.Length > 0)
            {
                if (__commanders != null)
                {
                    using (var items = new NativeArray<Item>(__items.AsArray(), Allocator.TempJob))
                    {
                        JobHandle jobHandle = Dependency;
                        Commander commander;
                        foreach (var type in __types)
                        {
                            commander = __commanders[type];
                            commander.jobHandle = commander.value.Execute(this, items, jobHandle);

                            __jobHandles.Add(commander.jobHandle);

                            __commanders[type] = commander;
                        }

                        Dependency = JobHandle.CombineDependencies(__jobHandles.AsArray());

                        __jobHandles.Clear();
                    }
                }

                __items.Clear();
            }
        }
    }

    public partial class GuidDataSystem : DataSystem<Hash128>
    {
        private HashSet<Hash128> __guids;

        public Hash128 NewGuid()
        {
            if (__guids == null)
                __guids = new HashSet<Hash128>();

            Hash128 guid;
            do
            {
                guid = Guid.NewGuid().ToHash128();
            } while (!__guids.Add(guid));

            return guid;
        }

        public void Intern(in Hash128 guid)
        {
            if (__guids == null)
                __guids = new HashSet<Hash128>();

            __guids.Add(guid);
        }
    }

    public static class EntityGuidUtility
    {
        public static Hash128 ToHash128(this in Guid guid)
        {
            return new Hash128(guid.ToString("N"));
        }
    }
}