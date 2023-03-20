using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using System.Collections.Generic;
using System;

[DisableAutoCreation]
public partial class ExclusiveEntityTransactionSystem : SystemBase
{
    private struct Target
    {
        public EntityManager entityManager;
        public NativeList<Entity> entities;
        public Action<NativeArray<Entity>> callback;
    }

    private List<Target> __targets;
    private ExclusiveEntityTransaction? __exclusiveEntityTransaction;

    public ExclusiveEntityTransaction exclusiveEntityTransaction
    {
        get
        {
            if (__exclusiveEntityTransaction == null)
                __exclusiveEntityTransaction = EntityManager.BeginExclusiveEntityTransaction();

            return __exclusiveEntityTransaction.Value;
        }
    }

    public JobHandle jobHandle
    {
        get => EntityManager.ExclusiveEntityTransactionDependency;

        set
        {
            var entityManager = EntityManager;

            entityManager.ExclusiveEntityTransactionDependency = value;
        }
    }

    public void WaitFor(ref EntityManager entityManager, ref NativeList<Entity> entities, Action<NativeArray<Entity>> callback)
    {
        Target target;
        target.entityManager = entityManager;
        target.entities = entities;
        target.callback = callback;

        if (__targets == null)
            __targets = new List<Target>();

        __targets.Add(target);
    }

    public bool ExecuteAll(bool isForce)
    {
        if (__exclusiveEntityTransaction == null)
            return false;

        var entityManager = EntityManager;
        var dependency = entityManager.ExclusiveEntityTransactionDependency;
        if (!isForce && !dependency.IsCompleted)
            return false;

        dependency.Complete();

        entityManager.EndExclusiveEntityTransaction();

        if (__targets != null && __targets.Count > 0)
        {
            int index = 0, length;
            NativeArray<Entity> temp;
            NativeList<Entity> entityArray = new NativeList<Entity>(Allocator.TempJob);
            NativeList<Entity> entities = new NativeList<Entity>(Allocator.TempJob);
            foreach (var target in __targets)
            {
                entities.AddRange(target.entities.AsArray());

                length = target.entities.Length;

                entityArray.ResizeUninitialized(length);

                UnityEngine.Profiling.Profiler.BeginSample("CopyEntitiesFrom");

                temp = entityArray.AsArray();

                target.entityManager.CopyEntitiesFrom(entityManager, entities.AsArray().GetSubArray(index, length), temp);

                UnityEngine.Profiling.Profiler.EndSample();

                index += length;

                UnityEngine.Profiling.Profiler.BeginSample("Callback");

                target.callback(temp);

                UnityEngine.Profiling.Profiler.EndSample();
            }

            entityArray.Dispose();

            __targets.Clear();

            UnityEngine.Profiling.Profiler.BeginSample("DestroyEntities");

            var exclusiveEntityTransaction = entityManager.BeginExclusiveEntityTransaction();
            dependency = Job.WithCode(() =>
            {
                exclusiveEntityTransaction.DestroyEntity(entities.AsArray());

            }).WithName("DestroyEntities").Schedule(Dependency);

            dependency = entities.Dispose(dependency);

            UnityEngine.Profiling.Profiler.EndSample();

            Dependency = dependency;

            entityManager.ExclusiveEntityTransactionDependency = dependency;
            __exclusiveEntityTransaction = exclusiveEntityTransaction;
        }
        else
            __exclusiveEntityTransaction = null;

        return true;
    }

    protected override void OnUpdate()
    {
        ExecuteAll(false);
    }
}
