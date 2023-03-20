using Unity.Jobs;
using Unity.Entities;

namespace ZG
{
    public abstract partial class ReadOnlyLookupSystem : SystemBase, IReadOnlyLookupJobManager
    {
        protected LookupJobManager _lookupJobManager;

        public JobHandle readOnlyJobHandle
        {
            get => _lookupJobManager.readOnlyJobHandle;
        }

        public void CompleteReadOnlyDependency() => _lookupJobManager.CompleteReadOnlyDependency();

        public void AddReadOnlyDependency(in JobHandle inputDeps) => _lookupJobManager.AddReadOnlyDependency(inputDeps);

        protected override void OnUpdate()
        {
            _lookupJobManager.CompleteReadWriteDependency();

            _Update();

            _lookupJobManager.readWriteJobHandle = Dependency;
        }

        protected abstract void _Update();
    }

    public abstract class LookupSystem : ReadOnlyLookupSystem, ILookupJobManager
    {
        public JobHandle readWriteJobHandle
        {
            get => _lookupJobManager.readWriteJobHandle;

            set => _lookupJobManager.readWriteJobHandle = value;
        }

        public void CompleteReadWriteDependency() => _lookupJobManager.CompleteReadWriteDependency();
    }
}