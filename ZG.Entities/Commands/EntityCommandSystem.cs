using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace ZG
{
    public interface IEntityCommandScheduler
    {
        EntityCommander commander { get; }

        EntitySharedComponentCommander sharedComponentCommander { get; }

        EntityManager entityManager { get; }
    }

    public interface IEntityCommander<T> : IDisposable where T : struct
    {
        void Execute(
            EntityCommandPool<T>.Context context, 
            EntityCommandSystem system, 
            ref NativeParallelHashMap<ComponentType, JobHandle> dependency, 
            in JobHandle inputDeps);
    }

    public class AddComponentDataCommander<T> : IEntityCommander<EntityData<T>> where T : unmanaged, IComponentData
    {
        public struct EntityCommandComponentContainer : IEntityCommandContainer, IDisposable
        {
            [BurstCompile]
            private struct Apply : IJobParallelFor
            {
                [ReadOnly]
                public EntityCommandContainerReadOnly container;
                public NativeList<Entity>.ParallelWriter entites;
                public NativeList<T>.ParallelWriter values;

                public void Execute(int index)
                {
                    EntityData<T> value;
                    var enumerator = container[index].GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        value = enumerator.As<EntityData<T>>();

                        entites.AddNoResize(value.entity);
                        values.AddNoResize(value.value);
                    }
                }
            }

            public NativeList<Entity> __entities;
            public NativeList<T> __values;

            public EntityCommandComponentContainer(NativeList<Entity> entities, NativeList<T> values)
            {
                __entities = entities;
                __values = values;
            }

            public EntityCommandComponentContainer(Allocator allocator)
            {
                __entities = new NativeList<Entity>(allocator);
                __values = new NativeList<T>(allocator);
            }

            public void Dispose()
            {
                __entities.Dispose();
                __values.Dispose();
            }

            public JobHandle CopyFrom(in EntityCommandContainerReadOnly container, in JobHandle inputDeps)
            {
                var counter = new NativeCounter(Allocator.TempJob);

                var jobHandle = container.CountOf(counter, inputDeps);

                var entities = __entities.AsParallelWriter();
                var entityJobHandle = __entities.Resize(counter, jobHandle);

                var values = __values.AsParallelWriter();
                var valueJobHandle = __values.Resize(counter, jobHandle);

                Apply apply;
                apply.container = container;
                apply.entites = entities;
                apply.values = values;

                jobHandle = apply.Schedule(container.length, container.innerloopBatchCount, JobHandle.CombineDependencies(entityJobHandle, valueJobHandle));

                return counter.Dispose(jobHandle);
            }

            public void CopyFrom(in EntityCommandContainerReadOnly container)
            {
                EntityData<T> value;
                var enumerator = container.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    value = enumerator.As<EntityData<T>>();

                    __entities.Add(value.entity);
                    __values.Add(value.value);
                }

                /*int count = container.Count();
                if (count < 0)
                    return;

                __entities.Capacity = math.max(__entities.Capacity, __entities.Length + count);
                __values.Capacity = math.max(__values.Capacity, __values.Length + count);

                Apply apply;
                apply.container = container;
                apply.entites = new UnsafeListEx<Entity>.ParallelWriterSafe(__entities);
                apply.values = new UnsafeListEx<T>.ParallelWriterSafe(__values);

                for (int i = 0; i < container.length; ++i)
                    apply.Execute(i);*/
            }

            /*public void AddComponentDataAndDispose(ref SystemState state, int innerloopBatchCount)
            {
                UnityEngine.Assertions.Assert.AreEqual(__values.Length, __entities.Length);

                JobHandle jobHandle;
                if (__entities.IsEmpty)
                    jobHandle = state.Dependency;
                else
                {
                    //state.CompleteDependency();
                    state.EntityManager.AddComponentBurstCompatible<T>(__entities.AsArray());

                    jobHandle = __values.AsArray().MoveTo(__entities.AsArray(), state.GetComponentLookup<T>(), innerloopBatchCount, state.Dependency);
                }

                state.Dependency = JobHandle.CombineDependencies(__entities.Dispose(jobHandle), __values.Dispose(jobHandle));
            }*/
        }

        public int innerloopBatchCount = 32;

        private NativeList<Entity> __entities;
        private NativeList<T> __values;

        public void Execute(
            EntityCommandPool<EntityData<T>>.Context context, 
            EntityCommandSystem system,
            ref NativeParallelHashMap<ComponentType, JobHandle> dependency, 
            in JobHandle inputDeps)
        {
            if (__entities.IsCreated)
                __entities.Clear();
            else
                __entities = new NativeList<Entity>(Allocator.Persistent);

            if (__values.IsCreated)
                __values.Clear();
            else
                __values = new NativeList<T>(Allocator.Persistent);

            {
                var entityMananger = system.EntityManager;

/*#if DEBUG
                while (context.TryDequeue(out var command))
                {
                    if (!entityMananger.Exists(command.entity))
                    {
                        UnityEngine.Debug.LogError(command.entity.ToString() + ":" + typeof(T));

                        continue;
                    }

                    if (entityMananger.HasComponent<T>(command.entity))
                    {
                        UnityEngine.Debug.LogError(command.entity.ToString() + "-" + typeof(T));

                        continue;
                    }

                    __entities.Add(command.entity);
                    __values.Add(command.value);
                }
#else*/
                context.MoveTo(new EntityCommandComponentContainer(__entities, __values));
//#endif

                UnityEngine.Assertions.Assert.AreEqual(__values.Length, __entities.Length);

                if (__entities.Length > 0)
                {
                    dependency.CompleteAll(inputDeps);

                    entityMananger.AddComponent<T>(__entities.AsArray());

                    /*SetValues setValues;
                    setValues.entityArray = entityArray;
                    setValues.inputs = values;
                    setValues.outputs = system.GetComponentLookup<T>();*/

                    var jobHandle = __values.AsArray().MoveTo(__entities.AsArray(), system.GetComponentLookup<T>(), innerloopBatchCount, inputDeps);

                    //jobHandle = JobHandle.CombineDependencies(entityArray.Dispose(jobHandle), values.Dispose(jobHandle));
                    dependency[typeof(T)] = jobHandle;

                    return;
                }
            }
            //entityArray.Dispose();
            //values.Dispose();
        }

        public void Dispose()
        {
            if (__entities.IsCreated)
                __entities.Dispose();

            if(__values.IsCreated)
                __values.Dispose();
        }
    }

    public class AddBufferCommander<T> : IEntityCommander<EntityData<T>> where T : unmanaged, IBufferElementData
    {
        /*[BurstCompile]
        private struct SetValues : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<T> inputs;

            [WriteOnly, NativeDisableParallelForRestriction]
            public BufferLookup<T> outputs;

            public void Execute(int index)
            {
                outputs[entityArray[index]].Add(inputs[index]);
            }
        }*/

        public const int innerloopBatchCount = 1;

        private NativeList<Entity> __entities;
        private NativeList<T> __values;

        public void Execute(
            EntityCommandPool<EntityData<T>>.Context context, 
            EntityCommandSystem system, 
            ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
            in JobHandle inputDeps)
        {
            if (__entities.IsCreated)
                __entities.Clear();
            else
                __entities = new NativeList<Entity>(Allocator.Persistent);

            if (__values.IsCreated)
                __values.Clear();
            else
                __values = new NativeList<T>(Allocator.Persistent);

            {
                context.MoveTo(new EntityCommandBufferContainer<T>( __entities, __values));

                if (__entities.Length > 0)
                {
                    using (var uniqueEntities = new NativeArray<Entity>(__entities.AsArray(), Allocator.TempJob))
                    {
                        int count = uniqueEntities.ConvertToUniqueArray();

                        dependency.CompleteAll(inputDeps);

                        system.EntityManager.AddComponent<T>(uniqueEntities.GetSubArray(0, count));
                    }

                    var jobHandle = __values.AsArray().MoveTo(__entities.AsArray(), system.GetBufferLookup<T>(), innerloopBatchCount, inputDeps);
                    dependency[typeof(T)] = jobHandle;

                    return;

                    /*SetValues setValues;
                    setValues.entityArray = entities;
                    setValues.inputs = values;
                    setValues.outputs = system.GetBufferLookup<T>();
                    dependency[typeof(T)] = setValues.Schedule(entities.Length, innerloopBatchCount, inputDeps);*/
                }
            }
            //entityArray.Dispose();
            //values.Dispose();
        }

        public void Dispose()
        {
            if (__entities.IsCreated)
                __entities.Dispose();

            if (__values.IsCreated)
                __values.Dispose();
        }
    }

    public class AddComponentCommander<T> : IEntityCommander<Entity>
    {
        private NativeList<Entity> __entities;

        public void Execute(
            EntityCommandPool<Entity>.Context context, 
            EntityCommandSystem system,
            ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
            in JobHandle inputDeps)
        {
            if (__entities.IsCreated)
                __entities.Clear();
            else
                __entities = new NativeList<Entity>(Allocator.Persistent);

            /*if (system.World.Name.Contains("Server") && typeof(T).Name.Contains("EntityDataSerializable"))
                __counter.count = 0;*/

            {
                context.MoveTo(new EntityCommandEntityContainer(__entities));

                /*while (context.TryDequeue(out Entity entity))
                    __entities.Add(entity);*/

                if (__entities.Length > 0)
                {
                    dependency.CompleteAll(inputDeps);

                    system.EntityManager.AddComponent<T>(__entities.AsArray());
                }
            }
        }

        public void Dispose()
        {
            if (__entities.IsCreated)
                __entities.Dispose();
        }
    }

    public class RemoveComponentCommander<T> : IEntityCommander<Entity>
    {
        private NativeList<Entity> __entities;

        public void Execute(
            EntityCommandPool<Entity>.Context context, 
            EntityCommandSystem system, 
            ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
            in JobHandle inputDeps)
        {
            if (__entities.IsCreated)
                __entities.Clear();
            else
                __entities = new NativeList<Entity>(Allocator.Persistent);

            {
                context.MoveTo(new EntityCommandEntityContainer(__entities));

                if (__entities.Length > 0)
                {
                    dependency.CompleteAll(inputDeps);

                    system.EntityManager.RemoveComponent<T>(__entities.AsArray());
                }
            }
        }

        public void Dispose()
        {
            if (__entities.IsCreated)
                __entities.Dispose();
        }
    }

    public class DestroyEntityCommander : IEntityCommander<Entity>
    {
        private NativeList<Entity> __entities;

        public void Execute(
            EntityCommandPool<Entity>.Context context, 
            EntityCommandSystem system,
            ref NativeParallelHashMap<ComponentType, JobHandle> dependency,
            in JobHandle inputDeps)
        {
            if (__entities.IsCreated)
                __entities.Clear();
            else
                __entities = new NativeList<Entity>(Allocator.Persistent);

            {
                context.MoveTo(new EntityCommandEntityContainer(__entities));

                if (__entities.Length > 0)
                {
                    dependency.CompleteAll(inputDeps);

                    system.EntityManager.DestroyEntity(__entities.AsArray());
                }
            }
        }

        public void Dispose()
        {
            if (__entities.IsCreated)
                __entities.Dispose();
        }
    }

    public class EntityCommandManager : IDisposable
    {
        public const float QUEUE_INIT = -3f;
        public const float QUEUE_CREATE = -2f;
        public const float QUEUE_ADD = -1f;
        public const float QUEUE_SET = 0f;
        public const float QUEUE_REMOVE = 1f;
        public const float QUEUE_DESTROY = 2f;
        public const float QUEUE_PRESENT = 3f;

        public interface ICommander : IDisposable
        {
            void Execute(ComponentSystemBase system, ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps);
        }

        private struct CommanderKey
        {
            public struct Comparer : IComparer<CommanderKey>
            {
                public int Compare(CommanderKey x, CommanderKey y)
                {
                    int result = x.queue.CompareTo(y.queue);
                    if (result == 0)
                        return x.value.GetHashCode().CompareTo(y.value.GetHashCode());

                    return result;
                }
            }

            public float queue;
            public Type value;

            public override string ToString()
            {
                return $"Commander Type({value} : {queue})";
            }
        }

        private struct CommanderValue
        {

#if ENABLE_PROFILER
            public ProfilerMarker profilerMarker;
#endif
            public ICommander instance;
        }

        private bool __isExecuting;
        private SortedList<CommanderKey, CommanderValue> __commanders;
        private CommanderValue[] __values;

#if ENABLE_PROFILER
        private ProfilerMarker __profilerMarker;

        public EntityCommandManager()
        {
            __profilerMarker = new ProfilerMarker("Execute EntityCommandManager");
        }
#endif

        public bool Get<T>(float queue, out T commander) where T : ICommander
        {
            if (__commanders != null)
            {
                CommanderKey commanderKey;
                commanderKey.queue = queue;
                commanderKey.value = typeof(T);

                if (__commanders.TryGetValue(commanderKey, out var commanderValue))
                {
                    commander = (T)commanderValue.instance;

                    return true;
                }
            }

            commander = default;

            return false;
        }

        public void Create<T>(float queue, in T commander) where T : ICommander
        {
            UnityEngine.Assertions.Assert.IsFalse(__isExecuting);

            CommanderKey commanderKey;
            commanderKey.queue = queue;
            commanderKey.value = typeof(T);

            CommanderValue commanderValue;
            commanderValue.instance = commander;

#if ENABLE_PROFILER
            commanderValue.profilerMarker = new ProfilerMarker(typeof(T).FullName);
#endif

            if (__commanders == null)
                __commanders = new SortedList<CommanderKey, CommanderValue>(new CommanderKey.Comparer());

            __commanders.Add(commanderKey, commanderValue);

            __values = null;
        }

        public T GetOrCreate<T>(float queue) where T : ICommander, new()
        {
            if (Get(queue, out T value))
                return value;

            value = new T();
            Create(queue, value);

            return value;
        }

        public bool Delete<T>(float queue)
        {
            if (__commanders == null)
                return false;

            CommanderKey commanderKey;
            commanderKey.queue = queue;
            commanderKey.value = typeof(T);

            if(__commanders.Remove(commanderKey))
            {
                __values = null;

                return true;
            }

            return false;
        }

        public bool Delete<T>(float queue, out T commander)
        {
            if (__commanders != null)
            {
                CommanderKey commanderKey;
                commanderKey.queue = queue;
                commanderKey.value = typeof(T);

                if (__commanders.TryGetValue(commanderKey, out var commanderValue) && __commanders.Remove(commanderKey))
                {
                    __values = null;

                    commander = (T)commanderValue.instance;

                    return true;
                }
            }

            commander = default;

            return false;
        }

        public void Execute(ComponentSystemBase system, ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps)
        {
            int count = __commanders == null ? 0 : __commanders.Count;
            if (count < 1)
                return;

            if (__values == null)
            {
                __values = new CommanderValue[count];
                __commanders.Values.CopyTo(__values, 0);
            }

            UnityEngine.Assertions.Assert.IsFalse(__isExecuting);

            __isExecuting = true;

#if ENABLE_PROFILER
            using (__profilerMarker.Auto())
#endif
            {
                foreach(var value in __values)
                {
                    try
                    {
#if ENABLE_PROFILER
                        using (value.profilerMarker.Auto())
#endif
                        value.instance.Execute(system, ref dependency, inputDeps);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogException(e.InnerException ?? e);
                    }
                }
            }

            __isExecuting = false;
        }

        public void Dispose()
        {
            if (__commanders != null)
            {
                foreach (var commanderValue in __commanders.Values)
                    commanderValue.instance.Dispose();

                __commanders = null;
            }
        }
    }

    public abstract partial class EntityCommandSystemBase : SystemBase
    {
#if ENABLE_PROFILER
        private ProfilerMarker __profilerMarker;
#endif

        private NativeParallelHashMap<ComponentType, JobHandle> __dependency;

        public EntityCommandManager _manager
        {
            get;
        }

        public EntityCommandSystemBase()
        {
            _manager = new EntityCommandManager();
        }

        protected override void OnCreate()
        {
            base.OnCreate();

#if ENABLE_PROFILER
            __profilerMarker = new ProfilerMarker("EntityCommandManager");
#endif

            __dependency = new NativeParallelHashMap<ComponentType, JobHandle>(1, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            _manager.Dispose();
            __dependency.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
#if ENABLE_PROFILER
            using (__profilerMarker.Auto())
#endif
                _manager.Execute(this, ref __dependency, Dependency);

            JobHandle jobHandle;
            using (var jobHandles = __dependency.GetValueArray(Allocator.Temp))
                jobHandle = jobHandles.Length > 0 ? JobHandle.CombineDependencies(jobHandles) : Dependency;

            Dependency = __dependency.Clear(0, jobHandle);
        }
    }

    public abstract class EntityCommandSystem : EntityCommandSystemBase
    {
        private struct Group
        {
            public JobHandle jobHandle;
            public NativeList<Entity> entities;
        }

        private class InternalAddComponentCommander<T> : EntityCommandManager.ICommander
        {
            public List<Group> groups;

            public void Execute(ComponentSystemBase system, ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps)
            {
                int count = groups == null ? 0 : groups.Count;
                switch (count)
                {
                    case 0:
                        break;
                    case 1:
                        dependency.CompleteAll(inputDeps);

                        Group group = groups[0];

                        group.jobHandle.Complete();

                        system.EntityManager.AddComponent<T>(group.entities.AsArray());

                        group.entities.Dispose();

                        groups.Clear();
                        break;
                    default:
                        dependency.CompleteAll(inputDeps);

                        using (var entities = new NativeList<Entity>(Allocator.TempJob))
                        {
                            foreach (var temp in groups)
                            {
                                temp.jobHandle.Complete();
                                entities.AddRange(temp.entities.AsArray());
                                temp.entities.Dispose();
                            }

                            groups.Clear();

                            system.EntityManager.AddComponent<T>(entities.AsArray());
                        }
                        break;
                }
            }

            void IDisposable.Dispose()
            {
                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        group.jobHandle.Complete();
                        group.entities.Dispose();
                    }

                    groups = null;
                }

            }
        }

        private class InternalRemoveComponentCommander<T> : EntityCommandManager.ICommander
        {
            public List<Group> groups;

            public void Execute(ComponentSystemBase system, ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps)
            {
                int count = groups == null ? 0 : groups.Count;
                switch (count)
                {
                    case 0:
                        break;
                    case 1:
                        Group group = groups[0];

                        group.jobHandle.Complete();

                        dependency.CompleteAll(inputDeps);

                        system.EntityManager.RemoveComponent<T>(group.entities.AsArray());

                        group.entities.Dispose();

                        groups.Clear();
                        break;
                    default:

                        using (var entities = new NativeList<Entity>(Allocator.TempJob))
                        {
                            foreach (var temp in groups)
                            {
                                temp.jobHandle.Complete();
                                entities.AddRange(temp.entities.AsArray());
                                temp.entities.Dispose();
                            }

                            groups.Clear();

                            dependency.CompleteAll(inputDeps);

                            system.EntityManager.RemoveComponent<T>(entities.AsArray());
                        }
                        break;
                }
            }

            void IDisposable.Dispose()
            {
                if (groups != null)
                {
                    foreach (var group in groups)
                    {
                        group.jobHandle.Complete();
                        group.entities.Dispose();
                    }

                    groups = null;
                }
            }
        }

        private class InternalDestroyEntityCommander : EntityCommandManager.ICommander
        {
            public List<Group> groups;

            public void Execute(ComponentSystemBase system, ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps)
            {
                if (groups.Count < 1)
                    return;

                dependency.CompleteAll(inputDeps);

                var entityManager = system.EntityManager;
                foreach (var group in groups)
                {
                    group.jobHandle.Complete();

                    entityManager.DestroyEntity(group.entities.AsArray());

                    group.entities.Dispose();
                }

                groups.Clear();
            }

            void IDisposable.Dispose()
            {

            }
        }

        private class Commander<TCommand, TEntityCommander> : EntityCommandManager.ICommander
            where TCommand : struct
            where TEntityCommander : IEntityCommander<TCommand>
        {
            private TEntityCommander __instance;
            private EntityCommandPool<TCommand>.Context __context;

            public EntityCommandPool<TCommand> pool => __context.pool;

            public Commander(TEntityCommander instance)
            {
                __instance = instance;

                __context = new EntityCommandPool<TCommand>.Context(Allocator.Persistent);
            }

            public void Execute(ComponentSystemBase system, ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps)
            {
                if(__context.isVail)
                    __instance.Execute(__context, (EntityCommandSystem)system, ref dependency, inputDeps);
            }

            public void Dispose()
            {
                __instance.Dispose();

                __context.pool.Dispose();
            }
        }

        /*public static bool IsSystemStateComponent<T>()
        {
            Type[] types = typeof(T).GetInterfaces();
            if (types != null)
            {
                foreach (Type type in types)
                {
                    if (type == typeof(ISystemStateComponentData) ||
                        type == typeof(ISystemStateBufferElementData) ||
                        type == typeof(ISystemStateSharedComponentData))
                        return true;
                }
            }

            return false;
        }*/

        public bool Destroy<TCommand, TEntityCommander>(float queue)
            where TCommand : struct
            where TEntityCommander : IEntityCommander<TCommand>
        {
            if (_manager.Delete<Commander<TCommand, TEntityCommander>>(queue, out var commander))
            {
                commander.Dispose();

                return true;
            }

            return false;
        }

        public void Create<T>(float queue, in T commander) where T : EntityCommandManager.ICommander
        {
            _manager.Create(queue, commander);
        }

        public T GetOrCreate<T>(float queue) where T : EntityCommandManager.ICommander, new()
        {
            return _manager.GetOrCreate<T>(queue);
        }

        public EntityCommandPool<TCommand> Get<TCommand, TEntityCommander>(float queue)
            where TCommand : struct
            where TEntityCommander : IEntityCommander<TCommand>
        {
            if (_manager.Get<Commander<TCommand, TEntityCommander>>(queue, out var commander))
                return commander.pool;

            return default;
        }

        public EntityCommandPool<TCommand> Create<TCommand, TEntityCommander>(float queue, in TEntityCommander instance)
            where TCommand : struct
            where TEntityCommander : IEntityCommander<TCommand>
        {
            var commander = new Commander<TCommand, TEntityCommander>(instance);

            Create(queue, commander);

            return commander.pool;
        }

        public EntityCommandPool<TCommand> GetOrCreate<TCommand, TEntityCommander>(float queue)
            where TCommand : struct
            where TEntityCommander : IEntityCommander<TCommand>, new()
        {
            var result = Get<TCommand, TEntityCommander>(queue);
            if (!result.isCreated)
            {
                var aotFixer = new TEntityCommander();

                result = Create<TCommand, TEntityCommander>(queue, aotFixer);
            }

            return result;
        }

        public EntityCommandPool<EntityData<T>> CreateAddComponentDataCommander<T>() where T : unmanaged, IComponentData
        {
            var result = Get<EntityData<T>, AddComponentDataCommander<T>>(EntityCommandManager.QUEUE_ADD);
            if (!result.isCreated)
            {
                var aotFixer = new AddComponentDataCommander<T>();

                result = Create<EntityData<T>, AddComponentDataCommander<T>>(EntityCommandManager.QUEUE_ADD, aotFixer);
            }

            return result;
        }

        public EntityCommandPool<EntityData<T>> CreateAddBufferCommander<T>() where T : unmanaged, IBufferElementData
        {
            var result = Get<EntityData<T>, AddBufferCommander<T>>(EntityCommandManager.QUEUE_ADD);
            if (!result.isCreated)
            {
                var aotFixer = new AddBufferCommander<T>();

                result = Create<EntityData<T>, AddBufferCommander<T>>(EntityCommandManager.QUEUE_ADD, aotFixer);
            }

            return result;
        }

        public EntityCommandPool<Entity> CreateAddComponentCommander<T>()
        {
            var result = Get<Entity, AddComponentCommander<T>>(EntityCommandManager.QUEUE_ADD);
            if (!result.isCreated)
            {
                var aotFixer = new AddComponentCommander<T>();

                result = Create<Entity, AddComponentCommander<T>>(EntityCommandManager.QUEUE_ADD, aotFixer);
            }

            return result;
        }

        public EntityCommandPool<Entity> CreateRemoveComponentCommander<T>()
        {
            var result = Get<Entity, RemoveComponentCommander<T>>(EntityCommandManager.QUEUE_REMOVE);
            if (!result.isCreated)
            {
                var aotFixer = new RemoveComponentCommander<T>();

                result = Create<Entity, RemoveComponentCommander<T>>(EntityCommandManager.QUEUE_REMOVE, aotFixer);
            }

            return result;
        }

        public EntityCommandPool<Entity> CreateDestroyEntityCommander()
        {
            return GetOrCreate<Entity, DestroyEntityCommander>(EntityCommandManager.QUEUE_DESTROY);
        }

        public JobHandle AddComponent<T>(in EntityQuery value, in JobHandle inputDeps)
        {
            var addComponentCommander = GetOrCreate<InternalAddComponentCommander<T>>(EntityCommandManager.QUEUE_ADD);

            Group group;
            group.entities = value.ToEntityListAsync(Allocator.TempJob, out JobHandle jobHandle);
            group.jobHandle = JobHandle.CombineDependencies(jobHandle, inputDeps);

            if (addComponentCommander.groups == null)
                addComponentCommander.groups = new List<Group>();

            addComponentCommander.groups.Add(group);

            return group.jobHandle;
        }

        public JobHandle RemoveComponent<T>(in EntityQuery value, in JobHandle inputDeps)
        {
            var removeComponentCommander = GetOrCreate<InternalRemoveComponentCommander<T>>(EntityCommandManager.QUEUE_REMOVE);

            Group group;
            group.entities = value.ToEntityListAsync(Allocator.TempJob, out JobHandle jobHandle);
            group.jobHandle = JobHandle.CombineDependencies(jobHandle, inputDeps);

            if (removeComponentCommander.groups == null)
                removeComponentCommander.groups = new List<Group>();

            removeComponentCommander.groups.Add(group);

            return group.jobHandle;
        }

        public void DestroyEntity(in EntityQuery value, in JobHandle inputDeps)
        {
            var destroyEntityCommander = GetOrCreate<InternalDestroyEntityCommander>(EntityCommandManager.QUEUE_DESTROY);

            Group group;
            group.entities = value.ToEntityListAsync(Allocator.TempJob, out JobHandle jobHandle);
            group.jobHandle = JobHandle.CombineDependencies(jobHandle, inputDeps);

            if (destroyEntityCommander.groups == null)
                destroyEntityCommander.groups = new List<Group>();

            destroyEntityCommander.groups.Add(group);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderFirst = true)]
    public partial struct EntityCommandFactorySystem : ISystem
    {

        public EntityCommandFactory factory
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            /*__prefabGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<EntityObjects>(), 
                    //ComponentType.ReadOnly<Prefab>()
                },
                Options = EntityQueryOptions.IncludePrefab// | EntityQueryOptions.IncludeDisabled
            });

            __instanceGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<EntityObjects>(),
                },
                //Options = EntityQueryOptions.IncludeDisabled
            });*/

            factory = new EntityCommandFactory(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            factory.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //factory.Playback(ref this.GetState(), __prefabGroup, __instanceGroup);

            this.factory.Playback(ref state);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(EntityCommandSharedSystemGroup), OrderLast = true)]
    public partial struct EntityCommanderSystem : ISystem
    {
        public EntityCommander value
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            /*__group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<EntityObjects>()
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
                });*/

            value = new EntityCommander(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            value.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            value.Playback(ref state);
        }
    }

    [UpdateInGroup(typeof(EntityCommandSharedSystemGroup))]
    public partial struct EntitySharedComponentCommanderSystem : ISystem
    {
        public EntitySharedComponentCommander value
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            value = new EntitySharedComponentCommander(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            value.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            value.Playback(ref state);
        }
    }

    [UpdateInGroup(typeof(ManagedSystemGroup))]
    public partial class EntityCommandSharedSystemGroup : ComponentSystemGroup, IEntityCommandScheduler
    {
        public EntityCommandFactory factory
        {
            get;

            private set;
        }

        public EntityCommander commander
        {
            get;

            private set;
        }

        public EntitySharedComponentCommander sharedComponentCommander
        {
            get;

            private set;
        }

        public EntityCommandSharedSystemGroup()
        {
            //UseLegacySortOrder = false;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            World world = World;
            factory = world.GetOrCreateSystemUnmanaged<EntityCommandFactorySystem>().factory;
            commander = world.GetOrCreateSystemUnmanaged<EntityCommanderSystem>().value;
            sharedComponentCommander = world.GetOrCreateSystemUnmanaged<EntitySharedComponentCommanderSystem>().value;
        }

        EntityManager IEntityCommandScheduler.entityManager => EntityManager;
    }

    public abstract partial class EntityCommandSystemHybrid : SystemBase, IEntityCommandScheduler
    {
        private EntityCommandSharedSystemGroup __sharedSystemGroup;

        public EntityCommander commander => __sharedSystemGroup.commander;

        public EntitySharedComponentCommander sharedComponentCommander => __sharedSystemGroup.sharedComponentCommander;

        protected override void OnCreate()
        {
            base.OnCreate();

            __sharedSystemGroup = World.GetOrCreateSystemManaged<EntityCommandSharedSystemGroup>();
        }

        protected override sealed void OnUpdate()
        {
            __sharedSystemGroup.Update();
        }

        EntityManager IEntityCommandScheduler.entityManager => EntityManager;
    }

    [UpdateInGroup(typeof(EndFrameEntityCommandSystemGroup))]
    public sealed class EndFrameEntityCommandSystem : EntityCommandSystem
    {

    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public sealed class BeginFrameEntityCommandSystem : EntityCommandSystemHybrid
    {
    }

    public static partial class EntityCommandUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(this IEntityCommandScheduler scheduler, Entity entity) => scheduler.commander.DestroyEntity(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(this IEntityCommandScheduler scheduler, NativeArray<Entity> entities) => scheduler.commander.DestroyEntity(entities);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AddComponent<T>(this IEntityCommandScheduler scheduler, in Entity entity)
        {
            if (TypeManager.IsSharedComponentType(TypeManager.GetTypeIndex<T>()))
                return scheduler.sharedComponentCommander.AddComponent<T>(entity);

            return scheduler.commander.AddComponent<T>(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool RemoveComponent<T>(this IEntityCommandScheduler scheduler, in Entity entity)
        {
            if (TypeManager.IsSharedComponentType(TypeManager.GetTypeIndex<T>()))
                return scheduler.sharedComponentCommander.RemoveComponent<T>(entity);

            return scheduler.commander.RemoveComponent<T>(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, IComponentData => scheduler.commander.SetComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBuffer<T>(this IEntityCommandScheduler scheduler, in Entity entity, in NativeArray<T> values) where T : struct, IBufferElementData => scheduler.commander.SetBuffer(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBuffer<T>(this IEntityCommandScheduler scheduler, in Entity entity, params T[] values) where T : unmanaged, IBufferElementData => scheduler.commander.SetBuffer(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetBuffer<TValue, TCollection>(this IEntityCommandScheduler scheduler, in Entity entity, in TCollection values) 
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
            => scheduler.commander.SetBuffer<TValue, TCollection>(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetComponentEnabled<T>(this IEntityCommandScheduler scheduler, in Entity entity, bool value)
            where T : unmanaged, IEnableableComponent
            => scheduler.commander.SetComponentEnabled<T>(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetSharedComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, ISharedComponentData => scheduler.sharedComponentCommander.SetSharedComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetComponentObject<T>(this IEntityCommandScheduler scheduler, in Entity entity, in EntityObject<T> value) => scheduler.commander.SetComponentObject(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, IComponentData => scheduler.commander.AddComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddBuffer<T>(this IEntityCommandScheduler scheduler, in Entity entity, params T[] values) where T : unmanaged, IBufferElementData => scheduler.commander.AddBuffer(entity, values);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddSharedComponentData<T>(this IEntityCommandScheduler scheduler, in Entity entity, in T value) where T : struct, ISharedComponentData => scheduler.sharedComponentCommander.AddSharedComponentData(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddComponentObject<T>(this IEntityCommandScheduler scheduler, in Entity entity, in EntityObject<T> value) => scheduler.commander.AddComponentObject(entity, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AppendBuffer<TValue, TCollection>(this IEntityCommandScheduler scheduler, in Entity entity, TCollection values) 
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue> => scheduler.commander.AppendBuffer<TValue, TCollection>(entity, values);

        public static TValue GetComponentData<TValue>(this IEntityCommandScheduler scheduler, in Entity entity)
            where TValue : unmanaged, IComponentData
        {
            TryGetComponentData<TValue>(scheduler, entity, out var value);

            return value;
        }

        public static bool TryGetComponentData<TValue>(this IEntityCommandScheduler scheduler, in Entity entity, out TValue value) 
            where TValue : unmanaged, IComponentData
        {
            var entityManager = scheduler.entityManager;
            if (entityManager.HasComponent<TValue>(entity))
            {
                value = entityManager.GetComponentData<TValue>(entity);

                scheduler.commander.TryGetComponentData(entity, ref value);

                return true;
            }
            else
            {
                value = default;

                if (scheduler.commander.TryGetComponentData(entity, ref value))
                    return true;
            }

            return false;
        }

        public static bool TryGetSharedComponentData<TValue>(this IEntityCommandScheduler scheduler, in Entity entity, out TValue value)
            where TValue : struct, ISharedComponentData
        {
            var entityManager = scheduler.entityManager;
            if (entityManager.HasComponent<TValue>(entity))
            {
                value = entityManager.GetSharedComponentManaged<TValue>(entity);

                scheduler.sharedComponentCommander.TryGetSharedComponentData(entity, ref value);

                return true;
            }
            else
            {
                value = default;

                if (scheduler.sharedComponentCommander.TryGetSharedComponentData(entity, ref value))
                    return true;
            }

            return false;
        }

        public static bool TryGetComponentObject<TValue>(this IEntityCommandScheduler scheduler, in Entity entity, out TValue value)
        {
            if(TryGetComponentData<EntityObject<TValue>>(scheduler, entity, out var result))
            {
                value = result.value;

                return true;
            }

            value = default;

            return false;
        }

        public static bool TryGetBuffer<TValue, TList, TWrapper>(
            this IEntityCommandScheduler scheduler,
            in Entity entity,
            ref TList list,
            ref TWrapper wrapper)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            var entityManager = scheduler.entityManager;
            if (entityManager.HasComponent<TValue>(entity))
            {
                var buffer = entityManager.GetBuffer<TValue>(entity);
                int length = buffer.Length;
                wrapper.SetCount(ref list, length);
                for (int i = 0; i < length; ++i)
                    wrapper.Set(ref list, buffer[i], i);

                scheduler.commander.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);

                return true;
            }

            return scheduler.commander.TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);
        }

        public static bool TryGetBuffer<T>(this IEntityCommandScheduler scheduler, in Entity entity, int index, out T value)
            where T : unmanaged, IBufferElementData
        {
            var list = new NativeList<T>(Allocator.Temp);
            NativeListWriteOnlyWrapper<T> wrapper;
            bool result = TryGetBuffer<T, NativeList<T>, NativeListWriteOnlyWrapper<T>>(scheduler, entity, ref list, ref wrapper) &&
                list.Length > index;

            value = result ? list[index] : default;

            list.Dispose();

            return result;
        }

        public static bool HasComponent(this IEntityCommandScheduler scheduler, in Entity entity, in ComponentType componentType)
        {
            var commander = scheduler.commander;
            if (!commander.IsExists(entity))
                return true;

            bool status;
            if (TypeManager.IsSharedComponentType(componentType.TypeIndex))
            {
                var sharedComponentCommander = scheduler.sharedComponentCommander;
                if (sharedComponentCommander.IsAddOrRemoveComponent(entity, componentType.TypeIndex, out status))
                    return status;
            }
            else if (commander.IsAddOrRemoveComponent(entity, componentType.TypeIndex, out status))
                return status;

            return scheduler.entityManager.HasComponent(entity, componentType);
        }

        public static bool HasComponent<TValue>(this IEntityCommandScheduler scheduler, in Entity entity) => HasComponent(scheduler, entity, typeof(TValue));

        public static void CompleteAll(this ref NativeParallelHashMap<ComponentType, JobHandle> dependency, in JobHandle inputDeps)
        {
            inputDeps.Complete();

            if (dependency.IsEmpty)
                return;

            using (var jobHandles = dependency.GetValueArray(Allocator.Temp))
                JobHandle.CompleteAll(jobHandles);

            dependency.Clear();
        }
    }
}