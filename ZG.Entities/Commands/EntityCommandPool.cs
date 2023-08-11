using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ZG
{
    public interface IEntityCommandProducerJob
    {
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class RegisterEntityCommandProducerJobAttribute : Attribute
    {
        public Type type;

        public RegisterEntityCommandProducerJobAttribute(Type type)
        {
            this.type = type;
        }
    }

#if DEBUG
    public static partial class EntityCommandUtility
    {
        private class SharedProducerJobType
        {
            public static readonly SharedStatic<int> CurrentIndex = SharedStatic<int>.GetOrCreate<SharedProducerJobType>();

            public static ref int GetIndex(Type type)
            {
                return ref SharedStatic<int>.GetOrCreate(typeof(SharedProducerJobType), type).Data;
            }

            /*public static void Init(Type producerJobType, int index)
            {
                GetIndex(producerJobType) = index;

                SharedStatic<FixedString128>.GetOrCreate(typeof(SharedProducerJobType), producerJobType).Data = producerJobType.FullName;
            }*/
        }

        private class SharedProducerJobType<T>
        {
            public static readonly SharedStatic<int> Index = SharedStatic<int>.GetOrCreate<SharedProducerJobType, T>();

            //public static readonly SharedStatic<FixedString128> Name = SharedStatic<FixedString128>.GetOrCreate<SharedProducerJobType, T>();
        }

        private static int __previousProducerJobTypeIndex;
        private static Dictionary<World, SystemHandle> __systems;
        private static System.Threading.Timer __timer;
        private static List<Type> __types;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void __Init()
        {
            __types = new List<Type>();
            
            RegisterEntityCommandProducerJobAttribute attribute;
            object[] attributes;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                foreach (var type in assembly.GetTypes())
                    __RegisterProducerJobType(type);

                attributes = assembly.GetCustomAttributes(typeof(RegisterEntityCommandProducerJobAttribute), false);
                if (attributes != null)
                {
                    foreach (var temp in attributes)
                    {
                        attribute = temp as RegisterEntityCommandProducerJobAttribute;
                        if (attribute == null)
                            continue;

                        if (!__RegisterProducerJobType(attribute.type))
                            UnityEngine.Debug.LogError($"Fail to register EntityCommandProducerJob: {attribute.type}");
                    }
                }
            }

            __systems = new Dictionary<World, SystemHandle>();

            if (__timer != null)
                __timer.Dispose();

            __timer = new System.Threading.Timer(x =>
            {
                int producerJobTypeIndex = currentProducerJobTypeIndex;
                if (producerJobTypeIndex != 0)
                {
                    if(producerJobTypeIndex < 0 || producerJobTypeIndex > __types.Count)
                        UnityEngine.Debug.LogError($"WTF Of Job {producerJobTypeIndex}??");

                    if (__previousProducerJobTypeIndex != 0 && __previousProducerJobTypeIndex == producerJobTypeIndex)
                        UnityEngine.Debug.LogError($"Job {__types[producerJobTypeIndex - 1]} is timeout!");
                }

                __previousProducerJobTypeIndex = producerJobTypeIndex;

                var worlds = World.All;
                foreach(var world in worlds)
                {
                    if (__systems.TryGetValue(world, out var system))
                    {
                        var worldUnmanaged = world.Unmanaged;
                        if (worldUnmanaged.ExecutingSystem != default && worldUnmanaged.ExecutingSystem == system)
                            UnityEngine.Debug.LogError($"In World {world.Name} System {worldUnmanaged.GetTypeOfSystem(system)} is timeout!");
                    }

                    __systems[world] = world.Unmanaged.ExecutingSystem;
                }
            }, null, 0, 1000);
        }

        public static ref int currentProducerJobTypeIndex => ref SharedProducerJobType.CurrentIndex.Data;

        public static int GetProducerJobTypeIndex<T>() where T : IEntityCommandProducerJob => SharedProducerJobType<T>.Index.Data;

        public static void RegisterProducerJobType<T>() where T : IEntityCommandProducerJob
        {
            if (__types == null)
                __Init();

            __RegisterProducerJobType(typeof(T));
        }

        /*private static bool RegisterProducerJobType(Type type)
        {
            if (Array.IndexOf(type.GetInterfaces(), typeof(IEntityCommandProducerJob)) != -1)
            {
                __RegisterProducerJobType(type);

                return true;
            }

            return false;
        }*/

        private static bool __RegisterProducerJobType(Type type)
        {
            if (Array.IndexOf(type.GetInterfaces(), typeof(IEntityCommandProducerJob)) != -1)
            {
                __types.Add(type);

                SharedProducerJobType.GetIndex(type) = __types.Count;

                return true;
            }

            return false;
        }

        //public static FixedString128 GetProducerJobTypeName<T>() where T : IEntityCommandProducerJob => SharedProducerJobType<T>.Name.Data;
    }
#endif

    [NativeContainer]
    public struct EntityCommandContainerReadOnly : IEnumerable<NativeFactoryObject>
    {
        public struct Enumerator : IEnumerator<NativeFactoryObject>
        {
            internal NativeFactoryEnumerable.Enumerator _instance;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public NativeFactoryObject Current
            {

                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return _instance.Current;
                }
            }

            public T As<T>() where T : struct
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return _instance.As<T>();
            }

            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return _instance.MoveNext();
            }

            public void Reset()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                _instance.Reset();
            }

            void IDisposable.Dispose()
            {

            }

            object IEnumerator.Current => Current;
        }

        public struct ThreadEnumerator : IEnumerator<NativeFactoryObject>
        {
            internal UnsafeFactory.ThreadEnumerator __instance;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            public NativeFactoryObject Current
            {
                get
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                    return __instance.Current;
                }
            }

            public T As<T>() where T : struct
            {

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return __instance.As<T>();
            }

            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                return __instance.MoveNext();
            }

            public void Reset() => __instance.Reset();

            void IDisposable.Dispose()
            {

            }

            object IEnumerator.Current => Current;
        }

        public struct Enumerable : IEnumerable<NativeFactoryObject>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal UnsafeFactory.Thread __thread;

            public ThreadEnumerator GetEnumerator()
            {
                ThreadEnumerator enumerator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                enumerator.m_Safety = m_Safety;
#endif

                enumerator.__instance = __thread.GetEnumerator();

                return enumerator;
            }

            IEnumerator<NativeFactoryObject> IEnumerable<NativeFactoryObject>.GetEnumerator() => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        internal UnsafeFactory _commands;

        public readonly int length => _commands.length;

        public int innerloopBatchCount => _commands.innerloopBatchCount;

        public Enumerable this[int index]
        {
            get
            {
                Enumerable enumerable;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

                enumerable.m_Safety = m_Safety;
#endif
                enumerable.__thread = _commands[index];

                return enumerable;
            }
        }

        public int Count()
        {
            return _commands.Count();
        }

        public JobHandle CountOf(NativeCounter.Concurrent counter, in JobHandle jobHandle)
        {
            return _commands.CountOf(counter, jobHandle);
        }

        public Enumerator GetEnumerator()
        {
            Enumerator enumerator;
            enumerator._instance = _commands.enumerable.GetEnumerator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            enumerator.m_Safety = m_Safety;
#endif

            return enumerator;
        }

        IEnumerator<NativeFactoryObject> IEnumerable<NativeFactoryObject>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct EntityCommandQueue
    {
        internal UnsafeFactory _commands;
        private UnsafeListEx<JobHandle> __jobHandles;

#if DEBUG
        private UnsafeListEx<int> __jobTypeIndices;
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
#endif

        public bool isCreated => _commands.isCreated;

        internal UnsafeFactory commands
        {
            get
            {
                if (__jobHandles.length > 0)
                {
                    __CompleteAll();

                    __jobHandles.Clear();

#if DEBUG
                    __jobTypeIndices.Clear();
#endif
                }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                return _commands;
            }
        }

        public EntityCommandQueue(in AllocatorManager.AllocatorHandle allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif

#if DEBUG
            __jobTypeIndices = new UnsafeListEx<int>(allocator);
#endif

            __jobHandles = new UnsafeListEx<JobHandle>(allocator);
            _commands = new UnsafeFactory(allocator, true);
        }

        public JobHandle MoveTo<T>(T destination, in JobHandle inputDeps) where T : IEntityCommandContainer
        {
            AddJobHandleForProducer<T>(inputDeps);

            EntityCommandContainerReadOnly source;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            source.m_Safety = m_Safety;
#endif

            source._commands = _commands;

            var jobHandle = destination.CopyFrom(source, JobHandle.CombineDependencies(__jobHandles.AsArray()));
            jobHandle = _commands.Clear(jobHandle);

            __jobHandles.Clear();
            __jobHandles.Add(jobHandle);

            return jobHandle;
        }

        public void MoveTo<T>(T destination) where T : IEntityCommandContainer
        {
            EntityCommandContainerReadOnly source;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            source.m_Safety = m_Safety;
#endif

            source._commands = commands;

            destination.CopyFrom(source);
            _commands.Clear();
        }

        public void Clear()
        {
            commands.Clear();
        }

        public void AddJobHandleForProducer<T>(in JobHandle producerJob) where T : IEntityCommandProducerJob
        {
#if DEBUG
            int producerJobTypeIndex = EntityCommandUtility.GetProducerJobTypeIndex<T>();
            UnityEngine.Assertions.Assert.AreNotEqual(0, producerJobTypeIndex);
            __jobTypeIndices.Add(producerJobTypeIndex);
#endif

            __jobHandles.Add(producerJob);
        }

        internal void Dispose()
        {
            __CompleteAll();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
#endif

#if DEBUG
            __jobTypeIndices.Dispose();
#endif

            __jobHandles.Dispose();
            _commands.Dispose();
        }

        private void __CompleteAll()
        {
#if DEBUG
            int length = __jobHandles.length;
            for(int i = 0; i < length; ++i)
            {
                EntityCommandUtility.currentProducerJobTypeIndex = __jobTypeIndices[i];

                __jobHandles[i].Complete();
            }

            EntityCommandUtility.currentProducerJobTypeIndex = 0;
#else
            JobHandle.CompleteAll(__jobHandles.AsArray());
#endif
        }
    }

    public struct EntityCommandQueue<T> where T : struct
    {
        [NativeContainer]
        public struct Writer
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal UnsafeFactory _commands;

            public void Enqueue(T value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                _commands.Create<T>().As<T>() = value;
            }
        }

        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        public struct ParallelWriter
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
#endif

            internal UnsafeFactory.ParallelWriter _commands;

            public bool isCreated => _commands.isCreated;

            public void Enqueue(T value)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
                _commands.Create<T>().As<T>() = value;
            }
        }

        private EntityCommandQueue __instance;

        public bool isCreated => __instance.isCreated;

        public Writer writer
        {
            get
            {
                UnityEngine.Assertions.Assert.IsTrue(isCreated);

                Writer writer;

                writer._commands = __instance._commands;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                writer.m_Safety = __instance.m_Safety;
#endif
                return writer;
            }
        }

        public ParallelWriter parallelWriter
        {
            get
            {
                UnityEngine.Assertions.Assert.IsTrue(isCreated);

                ParallelWriter parallelWriter;

                parallelWriter._commands = __instance._commands.parallelWriter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                parallelWriter.m_Safety = __instance.m_Safety;
#endif
                return parallelWriter;
            }
        }

        internal EntityCommandQueue(in EntityCommandQueue instance)
        {
            __instance = instance;
        }

        public JobHandle MoveTo<U>(U destination, in JobHandle inputDeps) where U : IEntityCommandContainer => __instance.MoveTo(destination, inputDeps);

        public void MoveTo<U>(U destination) where U : IEntityCommandContainer => __instance.MoveTo(destination);

        public void Clear() => __instance.Clear();

        internal void Dispose() => __instance.Dispose();

        public void AddJobHandleForProducer<U>(JobHandle producerJob) where U : IEntityCommandProducerJob
        {
            __instance.AddJobHandleForProducer<U>(producerJob);
        }
    }

    public struct EntityCommandPool
    {
        public struct Context
        {
            public bool isCreated => this.pool.isCreated;

            public bool isVail => this.pool.__values.length > 0;

            public bool isEmpty => this.pool.__values.length <= 0;

            internal EntityCommandPool pool
            {
                get;

                private set;
            }

            public Context(in AllocatorManager.AllocatorHandle allocator)
            {
                this.pool = new EntityCommandPool(allocator);
            }

            public bool TryDequeue<T>(out T value) where T : struct
            {
                var pool = this.pool;

                EntityCommandQueue values;
                int length = pool.__values.length;
                while (--length >= 0)
                {
                    values = pool.__values[length];
                    if (values.commands.enumerable.TryPopUnsafe(out value))
                        return true;

                    pool.__values.RemoveAt(length);

                    pool.__caches.Add(values);
                }

                value = default;

                return false;
            }

            public JobHandle MoveTo<T>(T container, in JobHandle inputDeps) where T : IEntityCommandContainer
            {
                JobHandle jobHandle = inputDeps;
                var pool = this.pool;
                int length = pool.__values.length;
                for (int i = 0; i < length; ++i)
                    jobHandle = pool.__values[i].MoveTo(container, jobHandle);

                pool.Clear();

                return jobHandle;
            }

            public void MoveTo<T>(T container) where T : IEntityCommandContainer
            {
                var pool = this.pool;
                int length = pool.__values.length;
                for (int i = 0; i < length; ++i)
                    pool.__values[i].MoveTo(container);

                pool.Clear();
            }

            internal void Dispose()
            {
                pool.Dispose();
            }
        }

        private UnsafeListEx<EntityCommandQueue> __values;
        private UnsafeListEx<EntityCommandQueue> __caches;

        public bool isCreated => __values.isCreated;

        public EntityCommandPool(in AllocatorManager.AllocatorHandle allocator)
        {
            __values = new UnsafeListEx<EntityCommandQueue>(allocator);
            __caches = new UnsafeListEx<EntityCommandQueue>(allocator);
        }

        public EntityCommandQueue Create()
        {
            EntityCommandQueue value;
            int length = __caches.length;
            if (length > 0)
            {
                value = __caches[--length];

                __caches.RemoveAt(length);

                value._commands.Clear();
            }
            else
                value = new EntityCommandQueue(__values.allocator);

            __values.Add(value);

            return value;
        }

        public void Clear()
        {
            __caches.AddRange(__values);

            __values.Clear();
        }

        internal void Dispose()
        {
            int length = __values.length;
            for (int i = 0; i < length; ++i)
                __values[i].Dispose();

            __values.Dispose();

            length = __caches.length;
            for (int i = 0; i < length; ++i)
                __caches[i].Dispose();

            __caches.Dispose();
        }
    }

    public struct EntityCommandPool<T> where T : struct
    {
        public struct Context
        {
            private EntityCommandPool.Context __instance;

            public bool isCreated => __instance.isCreated;

            public bool isVail => __instance.isVail;

            public bool isEmpty => __instance.isEmpty;

            public EntityCommandPool<T> pool => new EntityCommandPool<T>(__instance.pool);

            public Context(in AllocatorManager.AllocatorHandle allocator)
            {
                __instance = new EntityCommandPool.Context(allocator);
            }

            public bool TryDequeue(out T value) => __instance.TryDequeue(out value);

            public JobHandle MoveTo<U>(U container, in JobHandle inputDeps) where U : IEntityCommandContainer => __instance.MoveTo(container, inputDeps);

            public void MoveTo<U>(U container) where U : IEntityCommandContainer => __instance.MoveTo(container);

            public void Dispose()
            {
                __instance.Dispose();
            }
        }

        private EntityCommandPool __instance;

        public bool isCreated => __instance.isCreated;

        internal EntityCommandPool(in EntityCommandPool instance)
        {
            __instance = instance;
        }

        public EntityCommandQueue<T> Create() => new EntityCommandQueue<T>(__instance.Create());

        public void Clear() => __instance.Clear();

        internal void Dispose() => __instance.Dispose();
    }
}