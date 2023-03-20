using UnityEngine;

namespace ZG
{
    //[RequireComponent(typeof(Camera))]
    public class GrassObstacleManager : MonoBehaviour
    {
        [SerializeField]
        internal int _maxCount = 32;

        [SerializeField]
        internal string _worldName = string.Empty;

        private GrassObstacleSystem __obstacleSystem;
        private Vector4[] __parameters;

        protected void Awake()
        {
            __obstacleSystem = WorldUtility.GetOrCreateWorld(_worldName).GetExistingSystemManaged<GrassObstacleSystem>();
        }

        protected void Update()
        {
            if (__parameters == null)
                __parameters = new Vector4[_maxCount];
            
            int count = __obstacleSystem.GetParameters(transform.position, __parameters);

            if(count > 0)
                Shader.SetGlobalVectorArray("g_GrassObstacles", __parameters);

            Shader.SetGlobalInt("g_GrassObstacleCount", count);
        }
    }
}