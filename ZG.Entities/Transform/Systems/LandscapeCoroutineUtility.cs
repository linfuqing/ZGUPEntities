using System;
using System.Collections;

namespace ZG
{
    public static class LandscapeCoroutineUtility
    {
        public enum Status
        {
            Done,
            NotImportant, 
            Important
        }

        public delegate bool Stop();

        public delegate IEnumerator Coroutine<T>(int layerIndex, T position);
        
        public delegate IEnumerator ChangeStatus(Status status);

        [Serializable]
        public struct ImportantLayerToLoad
        {
            public int index;
            public int minDistance;
        }
        
        private static IEnumerator __Complete<TKey, TValue>(
            bool isLoading, 
            int layerIndex,
            TKey key, 
            TValue value, 
            LandscapeManager<TKey, TValue> manager, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged, IEquatable<TValue>
        {
            //print($"Landscape Complete (isLoading : {isLoading}, layerIndex : {layerIndex}, position: {position})");

            LandscapeLoaderCompleteType completeType;
            do
            {
                isLoading = !isLoading;
                if (isLoading)
                {
                    if (!stop())
                        yield return load(layerIndex, value);
                }
                else
                    yield return unload(layerIndex, value);

                completeType = manager.Complete(key, isLoading, layerIndex, value);

                //print($"Landscape Complete Type {completeType} (isLoading : {isLoading}, layerIndex : {layerIndex}, position: {position})");

            } while (completeType == LandscapeLoaderCompleteType.Reverse);

            if (completeType == LandscapeLoaderCompleteType.Error)
                UnityEngine.Debug.LogError($"Landscape Complete Error(isLoading : {isLoading}, layerIndex : {layerIndex}, position: {value})");
        }

        public static IEnumerator Load<TKey, TValue>(
            LandscapeManager<TKey, TValue> manager,
            TKey key, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop, 
            ChangeStatus changeStatus, 
            params ImportantLayerToLoad[] importantLayersToLoad)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged, IEquatable<TValue>
        {
            int i, numImportantLayersToLoad, layerIndex;
            TValue position;
            ImportantLayerToLoad importantLayerToLoad;
            while (true)
            {
                do
                {
                    numImportantLayersToLoad = importantLayersToLoad == null ? 0 : importantLayersToLoad.Length;
                    for (i = 0; i < numImportantLayersToLoad; ++i)
                    {
                        importantLayerToLoad = importantLayersToLoad[i];
                        if (manager.Load(key, importantLayerToLoad.index, out position,
                                importantLayerToLoad.minDistance))
                        {
                            yield return changeStatus(Status.Important);

                            yield return __Complete(false, importantLayerToLoad.index, key, position, manager, load, unload, stop);

                            break;
                        }
                    }

                    /*player = GameClientPlayer.localPlayer;
                    if (player == null || !player.instance.instance.isInit)
                    {
                        yield return null;

                        continue;
                    }*/

                } while (i < numImportantLayersToLoad);

                if (manager.Load(key, out layerIndex, out position))
                {
                    yield return changeStatus(Status.NotImportant);

                    yield return __Complete(false, layerIndex, key, position, manager, load, unload, stop);
                }
                else
                {
                    yield return changeStatus(Status.Done);

                    if (stop())
                        break;
                    
                    yield return null;
                }
            }
        }
        
        public static IEnumerator Unload<TKey, TValue>(
            LandscapeManager<TKey, TValue> manager, 
            TKey key, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged, IEquatable<TValue>
        {
            while (true)
            {
                if (manager.Unload(key, out int layerIndex, out var position))
                    yield return __Complete(true, layerIndex, key, position, manager, load, unload, stop);
                else if (stop())
                    break;
                else
                    yield return null;
            }
        }
    }
}