using Unity.Collections;
using Unity.Entities;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    public unsafe partial struct EntityManager
    {
        [StructuralChangeMethod]
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(BurstCompatibleSharedComponentData) })]
        public unsafe void SetSharedComponent(in NativeArray<Entity> entities, void* componentDataAddr, TypeIndex typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            Assert.IsFalse(TypeManager.IsManagedSharedComponent(typeIndex));
            access->SetSharedComponentData_Unmanaged(entities, typeIndex, componentDataAddr, null);
            access->EndStructuralChanges(ref changes);
        }
    }
}