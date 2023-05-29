using Unity.Entities;

namespace ZG
{
    public static class EntityArchetypeUtility
    {
        public unsafe static EntityArchetype GetInstantiateArchetype(in EntityArchetype entityArchetype)
        {
            EntityArchetype result;
            result.Archetype = entityArchetype.Archetype->InstantiateArchetype;
            return result;
        }
    }
}