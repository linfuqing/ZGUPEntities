
namespace Unity.Entities
{
    public static class ArchetypeChunkArrayUtility
    {
        public static unsafe ref EntityTypeHandle UpdateAsRef(this ref EntityTypeHandle value, ref SystemState state)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            value.m_Safety = state.m_DependencyManager->Safety.GetSafetyHandleForEntityTypeHandle();
#endif
            return ref value;
        }

        /// <summary>
        /// When a ComponentTypeHandle is cached by a system across multiple system updates, calling this function
        /// inside the system's OnUpdate() method performs the minimal incremental updates necessary to make the
        /// type handle safe to use.
        /// </summary>
        /// <param name="state">The SystemState of the system on which this type handle is cached.</param>
        public static ref ComponentTypeHandle<T> UpdateAsRef<T>(this ref ComponentTypeHandle<T> value,  ref SystemState state) where T : unmanaged, IComponentData
        {
            value.Update(ref state);

            return ref value;
        }

        public static unsafe ref ComponentTypeHandle<T> UpdateAsRef<T>(this ref ComponentTypeHandle<T> value, EntityManager entityManager) where T : unmanaged, IComponentData
        {
            var entityDataAccess = entityManager.GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            value.m_Safety = entityDataAccess->DependencyManager->Safety.GetSafetyHandleForComponentTypeHandle(value.m_TypeIndex, value.m_IsReadOnly == 0 ? false : true);
#endif

            value.m_GlobalSystemVersion = entityDataAccess->EntityComponentStore->GlobalSystemVersion;

            return ref value;
        }

        public static unsafe ref SharedComponentTypeHandle<T> UpdateAsRef<T>(this ref SharedComponentTypeHandle<T> value, EntityManager entityManager) where T : unmanaged, ISharedComponentData
        {
            var entityDataAccess = entityManager.GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            value.m_Safety = entityDataAccess->DependencyManager->Safety.GetSafetyHandleForSharedComponentTypeHandle(value.m_TypeIndex);
#endif

            return ref value;
        }

        /// <summary>
        /// When a BufferTypeHandle is cached by a system across multiple system updates, calling this function
        /// inside the system's OnUpdate() method performs the minimal incremental updates necessary to make the
        /// type handle safe to use.
        /// </summary>
        /// <param name="state">The SystemState of the system on which this type handle is cached.</param>
        public static ref BufferTypeHandle<T> UpdateAsRef<T>(this ref BufferTypeHandle<T> value, ref SystemState state) where T : unmanaged, IBufferElementData
        {
            value.Update(ref state);

            return ref value;
        }

        public static ref ComponentLookup<T> UpdateAsRef<T>(this ref ComponentLookup<T> value, ref SystemState state) where T : unmanaged, IComponentData
        {
            value.Update(ref state);

            return ref value;
        }

        public static ref BufferLookup<T> UpdateAsRef<T>(this ref BufferLookup<T> value, ref SystemState state) where T : unmanaged, IBufferElementData
        {
            value.Update(ref state);

            return ref value;
        }

        public static ref EntityStorageInfoLookup UpdateAsRef(this ref EntityStorageInfoLookup value, ref SystemState state)
        {
            value.Update(ref state);

            return ref value;
        }

        public static ref DynamicComponentTypeHandle UpdateAsRef(this ref DynamicComponentTypeHandle value, ref SystemState state)
        {
            value.Update(ref state);

            return ref value;
        }
    }
}