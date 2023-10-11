using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    public class WorldManager : MonoBehaviour
    {
        [Serializable]
        public struct WorldConfig
        {
            public string worldName;
            public WorldFlags worldFlags;
            public WorldSystemFilterFlags systemFilterFlags;

            //[Type(typeof(ISystem))]
            //public string[] maskSystemTypes;

            public string[] names;

            public World Create()
            {
                /*int numMaskSystemTypes = maskSystemTypes == null ? 0 : maskSystemTypes.Length;
                List<Type> types;
                if (numMaskSystemTypes > 0)
                {
                    types = new List<Type>(numMaskSystemTypes);
                    Type type;
                    for (int i = 0; i < numMaskSystemTypes; ++i)
                    {
                        type = Type.GetType(maskSystemTypes[i]);
                        if (type == null)
                            continue;

                        types.Add(type);
                    }
                }
                else
                    types = null;*/

                return WorldUtility.Create(worldName, worldFlags, systemFilterFlags, names/*types == null ? null : types.ToArray()*/);
            }
        }

        public WorldConfig[] worldConfigs;

        protected void Awake()
        {
            if(worldConfigs != null)
            {
                foreach(var worldConfig in worldConfigs)
                    worldConfig.Create();
            }
        }
    }
}