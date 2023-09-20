using System;
using System.Reflection;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using static Unity.Collections.AllocatorManager;

namespace ZG
{
    public interface IEntityDataStreamSerializer
    {
        void Serialize(ref NativeBuffer.Writer writer);
    }

    public interface IEntityComponentStreamSerializer<T> where T : struct, IComponentData
    {
        void Serialize(in T value, ref NativeBuffer.Writer writer);
    }

    public interface IEntityBufferStreamSerializer<T> where T : struct, IBufferElementData
    {
        void Serialize(in NativeArray<T> values, ref NativeBuffer.Writer writer);
    }

    public interface IEntityDataStreamDeserializer
    {
        ComponentTypeSet componentTypeSet { get; }

        void Deserialize(ref UnsafeBlock.Reader reader, ref EntityComponentAssigner assigner, in Entity entity);
    }

    public struct EntityComponentStreamSerializer<T> : IEntityComponentStreamSerializer<T> where T : unmanaged, IComponentData
    {
        public void Serialize(in T value, ref NativeBuffer.Writer writer)
        {
            writer.Write(value);
        }
    }

    public struct EntityBufferStreamSerializer<T> : IEntityBufferStreamSerializer<T> where T : unmanaged, IBufferElementData
    {
        public void Serialize(in NativeArray<T> values, ref NativeBuffer.Writer writer)
        {
            writer.Write(values.Length);
            writer.Write(values);
        }
    }

    public struct EntityComponentDeserializer<T> : IEntityDataStreamDeserializer where T : unmanaged, IComponentData
    {
        public ComponentTypeSet componentTypeSet => new ComponentTypeSet(ComponentType.ReadWrite<T>());

        public void Deserialize(ref UnsafeBlock.Reader reader, ref EntityComponentAssigner assigner, in Entity entity)
        {
            var value = reader.Read<T>();
            if (assigner.isCreated)
                assigner.SetComponentData(entity, value);
        }
    }

    public struct EntityBufferDeserializer<T> : IEntityDataStreamDeserializer where T : unmanaged, IBufferElementData
    {
        public ComponentTypeSet componentTypeSet => new ComponentTypeSet(ComponentType.ReadWrite<T>());

        public void Deserialize(ref UnsafeBlock.Reader reader, ref EntityComponentAssigner assigner, in Entity entity)
        {
            var array = reader.ReadArray<T>(reader.Read<int>());
            if (assigner.isCreated)
                assigner.SetBuffer(true, entity, array);
        }
    }

    public class EntityDataStreamAttribute : Attribute
    {
        public Type serializerType;
        public Type deserializerType;
    }

    public class EntityDataStreamSerializer : IEntityDataStreamSerializer
    {
        public List<IEntityDataStreamSerializer> children;

        public void Serialize(ref NativeBuffer.Writer writer)
        {
            if (children != null)
            {
                foreach (var child in children)
                    child.Serialize(ref writer);
            }
        }
    }

    [DisallowMultipleComponent]
    public class EntityComponentStream<T> : MonoBehaviour, IEntityDataStreamSerializer where T : unmanaged, IComponentData
    {
        public T value;

        public void Serialize(ref NativeBuffer.Writer writer) => writer.SerializeStream(value);
    }

    [DisallowMultipleComponent]
    public class EntityBufferStream<T> : MonoBehaviour, IEntityDataStreamSerializer where T : unmanaged, IBufferElementData
    {
        public T[] values;

        public unsafe void Serialize(ref NativeBuffer.Writer writer) => writer.SerializeStream(values);
    }

    public static class EntityDataStreamUtility
    {
        public static void SerializeStream<T>(this ref NativeBuffer.Writer writer, in T value) where T : unmanaged, IComponentData
        {
            writer.Write(typeof(T).AssemblyQualifiedName);

            var attributes = typeof(T).GetCustomAttributes<EntityDataStreamAttribute>();
            if (attributes != null)
            {
                foreach (var attribute in attributes)
                    ((IEntityComponentStreamSerializer<T>)Activator.CreateInstance(attribute.serializerType)).Serialize(value, ref writer);
            }
            //value.Serialize(ref writer);
        }

        public static void SerializeStream<T>(this ref NativeBuffer.Writer writer, in NativeArray<T> values) where T : unmanaged, IBufferElementData
        {
            writer.Write(typeof(T).AssemblyQualifiedName);

            var attributes = typeof(T).GetCustomAttributes<EntityDataStreamAttribute>();
            if (attributes != null)
            {
                foreach (var attribute in attributes)
                    ((EntityBufferStreamSerializer<T>)Activator.CreateInstance(attribute.serializerType)).Serialize(values, ref writer);
            }
        }

        public unsafe static void SerializeStream<T>(this ref NativeBuffer.Writer writer, T[] values) where T : unmanaged, IBufferElementData
        {
            int length = values == null ? 0 : values.Length;
            if (length > 0)
            {
                fixed (void* ptr = values)
                {
                    var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, values.Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
                    SerializeStream(ref writer, array);
                }
            }
        }

        public static void DeserializeStream(
            this ref UnsafeBlock.Reader reader, 
            ref NativeList<ComponentType> componentTypes, 
            ref EntityComponentAssigner assigner, 
            in Entity entity)
        {
            int i, numComponentTypes;
            ComponentTypeSet componentTypeSet;
            string typeName;
            Type type;
            IEntityDataStreamDeserializer deserializer;
            IEnumerable<EntityDataStreamAttribute> attributes;
            while (reader.isVail)
            {
                typeName = reader.ReadString();
                type = Type.GetType(typeName, true);
                attributes = type?.GetCustomAttributes<EntityDataStreamAttribute>();
                
                if(attributes != null)
                {
                    foreach (var attribute in attributes)
                    {
                        deserializer = (IEntityDataStreamDeserializer)Activator.CreateInstance(attribute.deserializerType);
                        deserializer.Deserialize(ref reader, ref assigner, entity);

                        if (componentTypes.IsCreated)
                        {
                            componentTypeSet = deserializer.componentTypeSet;
                            numComponentTypes = componentTypeSet.Length;
                            for(i = 0; i <numComponentTypes; ++i)
                                componentTypes.Add(componentTypeSet.GetComponentType(i));
                        }
                    }
                }
            }
        }

        public static void DeserializeStream(
            this ref UnsafeBlock.Reader reader, 
            ref EntityComponentAssigner assigner,
            in Entity entity)
        {
            NativeList<ComponentType> componentTypes = default;
            DeserializeStream(ref reader, ref componentTypes, ref assigner, Entity.Null);
        }

        public static void DeserializeStream(this ref UnsafeBlock.Reader reader, ref NativeList<ComponentType> componentTypes)
        {
            EntityComponentAssigner assigner = default;
            DeserializeStream(ref reader, ref componentTypes, ref assigner, Entity.Null);
        }
    }
}