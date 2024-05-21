
namespace ZG
{
    public static class SystemGroupUtilityEx
    {
        [RuntimeDispose, UnityEngine.Scripting.Preserve]
        public static void RuntimeDispose()
        {
            SystemGroupUtility.Dispose();
        }
    }
}