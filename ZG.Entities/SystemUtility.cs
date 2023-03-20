using System;
using System.Reflection;
using Unity.Jobs;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public static class SystemUtility
    {
        //private static readonly PropertyInfo AlwaysUpdateSystem = typeof(SystemState).GetProperty("AlwaysUpdateSystem", BindingFlags.NonPublic | BindingFlags.Instance);

        public unsafe static ref SystemState GetState(this ComponentSystemBase componentSystem)
        {
            return ref *componentSystem.CheckedState();
        }

        public unsafe static int GetSystemID(this ref SystemState systemState)
        {
            return systemState.m_SystemID;
        }

        public unsafe static void SetAlwaysUpdateSystem(this ref SystemState systemState, bool value)
        {
            /*object systemStateObject = systemState;
            AlwaysUpdateSystem.SetValue(systemStateObject, value);
            systemState = (SystemState)systemStateObject;*/
        }

    }
}