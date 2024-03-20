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

        public delegate IEnumerator Coroutine<in T>(int layerIndex, T position);
        
        public delegate IEnumerator ChangeStatus(Status status);

        [Serializable]
        public struct ImportantLayerToLoad
        {
            public int index;
            public int minDistance;
        }
        
        private static IEnumerator __Complete<TValue, TWorld>(
            bool isLoading, 
            int layerIndex,
            TValue value, 
            TWorld world, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop)
            where TValue : unmanaged, IEquatable<TValue>
            where TWorld : ILandscapeWorld<TValue>
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

                completeType = world.Complete(isLoading, layerIndex, value);

                //print($"Landscape Complete Type {completeType} (isLoading : {isLoading}, layerIndex : {layerIndex}, position: {position})");

            } while (completeType == LandscapeLoaderCompleteType.Reverse);

            if (completeType == LandscapeLoaderCompleteType.Error)
                UnityEngine.Debug.LogError($"Landscape Complete Error(isLoading : {isLoading}, layerIndex : {layerIndex}, position: {value})");
        }

        private static IEnumerator __Complete<TKey, TValue, TManager>(
            bool isLoading, 
            int layerIndex,
            TKey key, 
            TValue value, 
            TManager manager, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged, IEquatable<TValue>
            where TManager : ILandscapeManager<TKey, TValue>
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

        public static IEnumerator Load<TKey, TValue, TManager>(
            TManager manager,
            TKey key, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop, 
            ChangeStatus changeStatus, 
            params ImportantLayerToLoad[] importantLayersToLoad)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged, IEquatable<TValue>
            where TManager : ILandscapeManager<TKey, TValue>
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
        
        public static IEnumerator Load<TValue, TWorld>(
            TWorld world,
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop, 
            ChangeStatus changeStatus, 
            params ImportantLayerToLoad[] importantLayersToLoad)
            where TValue : unmanaged, IEquatable<TValue>
            where TWorld : ILandscapeWorld<TValue>
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
                        if (world.Load(importantLayerToLoad.index, out position,
                                importantLayerToLoad.minDistance))
                        {
                            yield return changeStatus(Status.Important);

                            yield return __Complete(false, importantLayerToLoad.index, position, world, load, unload, stop);

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

                if (world.Load(out layerIndex, out position))
                {
                    yield return changeStatus(Status.NotImportant);

                    yield return __Complete(false, layerIndex, position, world, load, unload, stop);
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
        
        public static IEnumerator Unload<TKey, TValue, TManager>(
            TManager manager, 
            TKey key, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged, IEquatable<TValue>
            where TManager : ILandscapeManager<TKey, TValue>
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
        
        public static IEnumerator Unload<TValue, TWorld>(
            TWorld world, 
            Coroutine<TValue> load, 
            Coroutine<TValue> unload, 
            Stop stop)
            where TValue : unmanaged, IEquatable<TValue>
            where TWorld : ILandscapeWorld<TValue>
        {
            while (true)
            {
                if (world.Unload(out int layerIndex, out var position))
                    yield return __Complete(true, layerIndex, position, world, load, unload, stop);
                else if (stop())
                    break;
                else
                    yield return null;
            }
        }
    }
}