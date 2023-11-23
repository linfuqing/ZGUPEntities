using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using ZG;

namespace ZG
{
    public interface IEntityCommandContainer : IEntityCommandProducerJob
    {
        JobHandle CopyFrom(in EntityCommandContainerReadOnly values, in JobHandle jobHandle);


        void CopyFrom(in EntityCommandContainerReadOnly values);
    }

    public struct EntityCommandEntityContainer : IEntityCommandContainer, IDisposable
    {
        [BurstCompile]
        private struct Apply : IJobParallelFor
        {
            [ReadOnly]
            public EntityCommandContainerReadOnly container;
            public NativeList<Entity>.ParallelWriter entities;

            public void Execute(int index)
            {
                var enumerator = this.container[index].GetEnumerator();
                while (enumerator.MoveNext())
                {
                    entities.AddNoResize(enumerator.As<Entity>());
                }
            }
        }

        private NativeList<Entity> __entities;

        public EntityCommandEntityContainer(NativeList<Entity> entities)
        {
            __entities = entities;
        }

        public EntityCommandEntityContainer(Allocator allocator)
        {
            __entities = new NativeList<Entity>(allocator);
        }

        public void Dispose()
        {
            __entities.Dispose();
        }

        public void Clear()
        {
            __entities.Clear();
        }

        public JobHandle CopyFrom(in EntityCommandContainerReadOnly container, in JobHandle inputDeps)
        {
            var counter = new NativeCounter(Allocator.TempJob);

            var jobHandle = container.CountOf(counter, inputDeps);
            var entities = __entities.AsParallelWriter();
            jobHandle = __entities.Resize(counter, jobHandle);

            var disposeJobHandle = counter.Dispose(jobHandle);

            Apply apply;
            apply.container = container;
            apply.entities = entities;

            return JobHandle.CombineDependencies(apply.Schedule(container.length, container.innerloopBatchCount, jobHandle), disposeJobHandle);
        }

        public void CopyFrom(in EntityCommandContainerReadOnly container)
        {
            var enumerator = container.GetEnumerator();
            while (enumerator.MoveNext())
                __entities.Add(enumerator.As<Entity>());
        }

        public void AddComponent<T>(ref SystemState state) where T : struct
        {
            if (__entities.IsEmpty)
                return;

            //state.CompleteDependency();
            state.EntityManager.AddComponentBurstCompatible<T>(__entities.AsArray());
        }

        public void RemoveComponent<T>(ref SystemState state) where T : struct
        {
            if (__entities.IsEmpty)
                return;

            //state.CompleteDependency();
            state.EntityManager.RemoveComponent<T>(__entities.AsArray());
        }

        public void DestroyEntity(ref SystemState state)
        {
            if (__entities.IsEmpty)
                return;

            //state.CompleteDependency();
            state.EntityManager.DestroyEntity(__entities.AsArray());
        }
    }

    public struct EntityCommandComponentContainer<T> : IEntityCommandContainer, IDisposable where T : unmanaged, IComponentData
    {
        [BurstCompile]
        private struct SetCapacity : IJob
        {
            [DeallocateOnJobCompletion]
            public NativeCounter counter;
            public EntityComponentContainer<T> container;

            public void Execute()
            {
                container.SetCapacityIfNeed(container.length + counter.count);
            }
        }

        [BurstCompile]
        public struct Apply : IJobParallelFor
        {
            [ReadOnly]
            public EntityCommandContainerReadOnly input;
            public EntityComponentContainer<T>.ParallelWriter output;

            public void Execute(int index)
            {
                EntityData<T> value;
                var enumerator = input[index].GetEnumerator();
                while (enumerator.MoveNext())
                {
                    value = enumerator.As<EntityData<T>>();

                    output.AddNoResize(value.entity, value.value);
                }
            }
        }

        public EntityComponentContainer<T> __value;

        public EntityCommandComponentContainer(Allocator allocator)
        {
            __value = new EntityComponentContainer<T>(allocator);
        }

        public void Dispose()
        {
            __value.Dispose();
        }

        public JobHandle Dispose(in JobHandle inputDeps)
        {
            return __value.Dispose(inputDeps);
        }

        public JobHandle CopyFrom(in EntityCommandContainerReadOnly container, in JobHandle inputDeps)
        {
            var counter = new NativeCounter(Allocator.TempJob);

            var jobHandle = container.CountOf(counter, inputDeps);

            SetCapacity setCapacity;
            setCapacity.counter = counter;
            setCapacity.container = __value;
            jobHandle = setCapacity.Schedule(jobHandle);

            Apply apply;
            apply.input = container;
            apply.output = __value.AsParallelWriter();

            jobHandle = apply.Schedule(container.length, container.innerloopBatchCount, jobHandle);

            return counter.Dispose(jobHandle);
        }

        public void CopyFrom(in EntityCommandContainerReadOnly container)
        {
            EntityData<T> value;
            var enumerator = container.GetEnumerator();
            while (enumerator.MoveNext())
            {
                value = enumerator.As<EntityData<T>>();

                __value.Add(value.entity, value.value);
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

        public void AddComponentData(ref SystemState state, int innerloopBatchCount)
        {
            __value.AddComponentData(ref state, innerloopBatchCount);
        }
    }

    public struct EntityCommandBufferContainer<T> : IEntityCommandContainer, IDisposable where T : unmanaged, IBufferElementData
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

        private NativeList<Entity> __entities;
        private NativeList<T> __values;

        public EntityCommandBufferContainer(NativeList<Entity> entities, NativeList<T> values)
        {
            __entities = entities;
            __values = values;
        }

        public EntityCommandBufferContainer(Allocator allocator)
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
            int count = container.Count();

            __entities.Capacity = math.max(__entities.Capacity, count);
            __values.Capacity = math.max(__values.Capacity, count);

            Apply apply;
            apply.container = container;
            apply.entites = __entities.AsParallelWriter();
            apply.values = __values.AsParallelWriter();

            for (int i = 0; i < container.length; ++i)
                apply.Execute(i);
        }

        public void AddBufferDataAndDispose(ref SystemState state, int innerloopBatchCount)
        {
            JobHandle jobHandle;
            if (__entities.IsEmpty)
                jobHandle = state.Dependency;
            else
            {
                var entities = __entities.AsArray();
                using (var uniqueEntities = new NativeArray<Entity>(entities, Allocator.Temp))
                {
                    int count = uniqueEntities.ConvertToUniqueArray();

                    state.EntityManager.AddComponent<T>(uniqueEntities.GetSubArray(0, count));
                }

                jobHandle = __values.AsArray().MoveTo(entities, state.GetBufferLookup<T>(), innerloopBatchCount, state.Dependency);
            }

            state.Dependency = JobHandle.CombineDependencies(__entities.Dispose(jobHandle), __values.Dispose(jobHandle));
        }
    }

    public struct EntityAddComponentPool<T> where T : unmanaged, IComponentData
    {
        private EntityCommandPool<EntityData<T>>.Context __context;

        public EntityCommandPool<EntityData<T>> value => __context.pool;

        public bool isEmpty => __context.isEmpty;

        public EntityAddComponentPool(Allocator allocator)
        {
            __context = new EntityCommandPool<EntityData<T>>.Context(allocator);
        }

        public void Dispose()
        {
            __context.Dispose();
        }

        /// <summary>
        /// Need RegisterGenericJobType(typeof(CopyNativeArrayToComponentData<>)
        /// </summary>
        /// <param name="innerloopBatchCount"></param>
        /// <param name="systemState"></param>
        public void Playback(int innerloopBatchCount, ref SystemState systemState)
        {
            var entities = new NativeList<Entity>(Allocator.TempJob);
            var values = new NativeList<T>(Allocator.TempJob);

            while (__context.TryDequeue(out var value))
            {
                entities.Add(value.entity);
                values.Add(value.value);
            }

            var jobHandle = systemState.Dependency;
            if (!entities.IsEmpty)
            {
                var entityArray = entities.AsArray();

                systemState.EntityManager.AddComponentBurstCompatible<T>(entityArray);

                jobHandle = values.AsArray().MoveTo(entityArray, systemState.GetComponentLookup<T>(), innerloopBatchCount, jobHandle);
            }

            systemState.Dependency = JobHandle.CombineDependencies(entities.Dispose(jobHandle), values.Dispose(jobHandle));
        }
    }

    public struct EntityAddDataQueue
    {
        public struct Writer
        {
            private EntityCommandQueue<EntityCommandStructChange>.Writer __addComponentQueue;
            private EntityComponentAssigner.Writer __assigner;

            public Writer(
                EntityCommandQueue<EntityCommandStructChange>.Writer addComponentQueue,
                EntityComponentAssigner.Writer assigner)
            {
                __addComponentQueue = addComponentQueue;
                __assigner = assigner;
            }

            public void AddComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
            {
                EntityCommandStructChange command;
                command.entity = entity;
                command.componentType = ComponentType.ReadWrite<T>();
                __addComponentQueue.Enqueue(command);

                __assigner.SetComponentData(entity, value);
            }
        }

        public struct ParallelWriter
        {
            private EntityCommandQueue<EntityCommandStructChange>.ParallelWriter __addComponentQueue;
            private EntityComponentAssigner.ParallelWriter __assigner;

            public ParallelWriter(
                EntityCommandQueue<EntityCommandStructChange>.ParallelWriter addComponentQueue,
                EntityComponentAssigner.ParallelWriter assigner)
            {
                __addComponentQueue = addComponentQueue;
                __assigner = assigner;
            }

            public void AddComponentData<T>(in Entity entity, in T value) where T : struct, IComponentData
            {
                EntityCommandStructChange command;
                command.entity = entity;
                command.componentType = ComponentType.ReadWrite<T>();
                __addComponentQueue.Enqueue(command);

                __assigner.SetComponentData(entity, value);
            }
        }

        private EntityCommandQueue<EntityCommandStructChange> __addComponentQueue;
        private EntityComponentAssigner __assigner;

        public Writer writer => new Writer(__addComponentQueue.writer, __assigner.writer);

        public EntityAddDataQueue(
            EntityCommandQueue<EntityCommandStructChange> addComponentQueue,
            EntityComponentAssigner assigner)
        {
            __addComponentQueue = addComponentQueue;
            __assigner = assigner;
        }

        public ParallelWriter AsParallelWriter(int bufferSize, int typeCount)
        {
            return new ParallelWriter(
                __addComponentQueue.parallelWriter,
                __assigner.AsParallelWriter(bufferSize, typeCount));
        }

        public ParallelWriter AsComponentParallelWriter<T>(int entityCount) where T : struct, IComponentData
        {
            return new ParallelWriter(
                __addComponentQueue.parallelWriter,
                __assigner.AsComponentDataParallelWriter<T>(entityCount));
        }

        public ParallelWriter AsComponentParallelWriter<T>(
            in NativeArray<int> entityCount, ref JobHandle jobHandle) where T : struct, IComponentData
        {
            return new ParallelWriter(
                __addComponentQueue.parallelWriter,
                __assigner.AsComponentDataParallelWriter<T>(entityCount, ref jobHandle));
        }

        public void AddJobHandleForProducer<T>(in JobHandle jobHandle) where T : IEntityCommandProducerJob
        {
            __addComponentQueue.AddJobHandleForProducer<T>(jobHandle);

            __assigner.jobHandle = jobHandle;
        }
    }

    public struct EntityAddDataPool
    {
        private EntityCommandPool<EntityCommandStructChange> __addComponentPool;
        private EntityComponentAssigner __assigner;

        public EntityAddDataPool(
            EntityCommandPool<EntityCommandStructChange> addComponentPool,
            EntityComponentAssigner assigner)
        {
            __addComponentPool = addComponentPool;
            __assigner = assigner;
        }

        public EntityAddDataQueue Create()
        {
            return new EntityAddDataQueue(__addComponentPool.Create(), __assigner);
        }
    }
}