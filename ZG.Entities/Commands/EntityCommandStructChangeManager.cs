using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Profiling;

namespace ZG
{
    public struct EntityCommandStructChange : IComparable<EntityCommandStructChange>
    {
        public Entity entity;
        public ComponentType componentType;

        public int CompareTo(EntityCommandStructChange other)
        {
            return componentType.GetHashCode() - other.componentType.GetHashCode();
        }
    }

    public struct EntityCommandStructChangeContainer : IEntityCommandContainer, IDisposable
    {
        public interface IExecutor
        {
            void Execute(
                ref SystemState systemState,
                in NativeArray<Entity> entityArray,
                in ComponentType componentType);
        }

        public struct AddComponentCommand : IExecutor
        {
            public void Execute(
                ref SystemState systemState,
                in NativeArray<Entity> entityArray,
                in ComponentType componentType)
            {
                systemState.EntityManager.AddComponentBurstCompatible(entityArray, componentType);
            }
        }

        public struct RemoveComponentCommand : IExecutor
        {
            public void Execute(
                ref SystemState systemState,
                in NativeArray<Entity> entityArray,
                in ComponentType componentType)
            {
                systemState.EntityManager.RemoveComponent(entityArray, componentType);
            }
        }

        [BurstCompile]
        private struct Apply : IJobParallelFor
        {
            [ReadOnly]
            public EntityCommandContainerReadOnly container;
            public NativeList<EntityCommandStructChange>.ParallelWriter commands;

            public void Execute(int index)
            {
                var enumerator = container[index].GetEnumerator();
                while (enumerator.MoveNext())
                    commands.AddNoResizeEx(enumerator.As<EntityCommandStructChange>());
            }
        }

        [BurstCompile]
        private struct Sort : IJob
        {
            public NativeArray<EntityCommandStructChange> commands;

            public void Execute()
            {
                commands.Sort();
            }
        }

        private NativeList<EntityCommandStructChange> __commands;

        public EntityCommandStructChangeContainer(Allocator allocator)
        {
            __commands = new NativeList<EntityCommandStructChange>(allocator);
        }

        public void Dispose()
        {
            __commands.Dispose();
        }

        public JobHandle CopyFrom(in EntityCommandContainerReadOnly container, in JobHandle inputDeps)
        {
            var counter = new NativeCounter(Allocator.TempJob);

            var jobHandle = container.CountOf(counter, inputDeps);
            var commands = __commands.AsParallelWriter();

            jobHandle = __commands.Resize(counter, jobHandle);

            var disposeJobHandle = counter.Dispose(jobHandle);

            Apply apply;
            apply.container = container;
            apply.commands = commands;

            jobHandle = apply.Schedule(container.length, container.innerloopBatchCount, jobHandle);

            Sort sort;
            sort.commands = __commands.AsDeferredJobArrayEx();
            return JobHandle.CombineDependencies(sort.Schedule(jobHandle), disposeJobHandle);
        }

        public void CopyFrom(in EntityCommandContainerReadOnly container)
        {
            var enumerator = container.GetEnumerator();
            while (enumerator.MoveNext())
                __commands.Add(enumerator.As<EntityCommandStructChange>());

            __commands.Sort();
        }

        public void Execute<T>(ref SystemState systemState, ref T executor) where T : IExecutor
        {
            if (__commands.IsEmpty)
                return;

            //systemState.CompleteDependency();

            using (var entities = new NativeList<Entity>(Allocator.Temp))
            {
                EntityCommandStructChange oldCommand = __commands[0], command;
                int numCommands = __commands.Length;

                entities.Add(oldCommand.entity);
                for (int i = 1; i < numCommands; ++i)
                {
                    command = __commands[i];
                    if (command.CompareTo(oldCommand) != 0)
                    {
                        executor.Execute(ref systemState, entities.AsArray(), oldCommand.componentType);

                        oldCommand = command;

                        entities.Clear();
                    }

                    entities.Add(command.entity);
                }

                if (entities.Length > 0)
                    executor.Execute(ref systemState, entities.AsArray(), oldCommand.componentType);
            }
        }

        public void AddComponent(ref SystemState systemState)
        {
            AddComponentCommand command;
            Execute(ref systemState, ref command);
        }

        public void RemoveComponent(ref SystemState systemState)
        {
            RemoveComponentCommand command;
            Execute(ref systemState, ref command);
        }
    }

    public struct EntityCommandStructChangeManager
    {
        public interface IAssigner
        {
            void Playback(ref SystemState systemState);
        }

        public struct Assigner : IAssigner
        {
            public void Playback(ref SystemState systemState)
            {

            }
        }

        public struct ComponentAssigner : IAssigner
        {
            public EntityComponentAssigner instance;

            public void Playback(ref SystemState systemState)
            {
                instance.Playback(ref systemState);
            }
        }

#if ENABLE_PROFILER
        private ProfilerMarker __addComponentProfilerMarker;
        private ProfilerMarker __removeComponentProfilerMarker;
        private ProfilerMarker __destoyEntityProfilerMarker;
        private ProfilerMarker __assignProfilerMarker;
#endif

        private EntityCommandPool<EntityCommandStructChange>.Context __addComponentCommander;
        private EntityCommandPool<EntityCommandStructChange>.Context __removeComponentCommander;
        private EntityCommandPool<Entity>.Context __destoyEntityCommander;

        public EntityCommandPool<EntityCommandStructChange> addComponentPool => __addComponentCommander.pool;

        public EntityCommandPool<EntityCommandStructChange> removeComponentPool => __removeComponentCommander.pool;

        public EntityCommandPool<Entity> destoyEntityPool => __destoyEntityCommander.pool;

        public EntityCommandStructChangeManager(Allocator allocator)
        {
#if ENABLE_PROFILER
            __addComponentProfilerMarker = new ProfilerMarker("AddComponents");
            __removeComponentProfilerMarker = new ProfilerMarker("RemoveComponents");
            __destoyEntityProfilerMarker = new ProfilerMarker("DestroyEntities");
            __assignProfilerMarker = new ProfilerMarker("Assign");
#endif

            __addComponentCommander = new EntityCommandPool<EntityCommandStructChange>.Context(allocator);
            __removeComponentCommander = new EntityCommandPool<EntityCommandStructChange>.Context(allocator);
            __destoyEntityCommander = new EntityCommandPool<Entity>.Context(allocator);
        }

        public void Dispose()
        {
            __addComponentCommander.Dispose();
            __removeComponentCommander.Dispose();
            __destoyEntityCommander.Dispose();
        }

        public void Playback<T>(ref SystemState systemState, ref T assigner) where T : IAssigner
        {
            if (!__addComponentCommander.isEmpty)
            {
#if ENABLE_PROFILER
                using (__addComponentProfilerMarker.Auto())
#endif
                {
                    using (var container = new EntityCommandStructChangeContainer(Allocator.Temp))
                    {
                        __addComponentCommander.MoveTo(container);
                        container.AddComponent(ref systemState);
                    }
                }
            }

#if ENABLE_PROFILER
            using (__assignProfilerMarker.Auto())
#endif
                assigner.Playback(ref systemState);

            if (!__removeComponentCommander.isEmpty)
            {
#if ENABLE_PROFILER
                using (__removeComponentProfilerMarker.Auto())
#endif
                {
                    using (var container = new EntityCommandStructChangeContainer(Allocator.Temp))
                    {
                        __removeComponentCommander.MoveTo(container);
                        container.RemoveComponent(ref systemState);
                    }
                }
            }

            if (!__destoyEntityCommander.isEmpty)
            {
#if ENABLE_PROFILER
                using (__destoyEntityProfilerMarker.Auto())
#endif
                {
                    using (var container = new EntityCommandEntityContainer(Allocator.Temp))
                    {
                        __destoyEntityCommander.MoveTo(container);
                        container.DestroyEntity(ref systemState);
                    }
                }
            }
        }

        public void Playback(ref SystemState systemState)
        {
            Assigner assigner;
            Playback(ref systemState, ref assigner);
        }

        public void Playback(ref SystemState systemState, ref EntityComponentAssigner assigner)
        {
            ComponentAssigner componentAssigner;
            componentAssigner.instance = assigner;
            Playback(ref systemState, ref componentAssigner);
        }
    }
}