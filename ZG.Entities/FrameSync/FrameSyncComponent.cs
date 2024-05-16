using System;
using Unity.Entities;

namespace ZG
{
    public struct SyncFrameCallbackData
    {
        public uint frameIndex;
        public EntityCommander commander;
    }

    public struct SyncFrameCallback : IBufferElementData, IEnableableComponent
    {
        public int type;
        public uint frameIndex;
        public CallbackHandle<SyncFrameCallbackData> handle;
        //public CallbackHandle finalHandle;
    }

    public struct SyncFrameEvent : IBufferElementData, IEnableableComponent, IComparable<SyncFrameEvent>
    {
        public int type;
        public uint callbackFrameIndex;
        public uint sourceFrameIndex;
        public uint destinationFrameIndex;

        public CallbackHandle<SyncFrameCallbackData> handle;

        //public CallbackHandle finalHandle;

        public int CompareTo(SyncFrameEvent obj)
        {
            return type.CompareTo(obj.type);
        }
    }

    public struct SyncFrame : IBufferElementData
    {
        public uint sourceIndex;
        public uint destinationIndex;
    }

    [Serializable]
    public struct SyncFrameArray : IBufferElementData
    {
        public int type;
        public int startIndex;
        public int count;

        public uint oldSourceIndex;
        public uint oldDestinationIndex;
    }

    [EntityComponent(typeof(SyncFrameCallback))]
    [EntityComponent(typeof(SyncFrameEvent))]
    [EntityComponent(typeof(SyncFrame))]
    [EntityComponent(typeof(SyncFrameArray))]
    public class FrameSyncComponent : EntityProxyComponent
    {
        public void Invoke(int type, uint frameIndex, Action<SyncFrameCallbackData> callback)
        {
            SyncFrameCallback frameCallback;
            frameCallback.type = type;
            frameCallback.frameIndex = frameIndex;
            frameCallback.handle = callback.Register();
            this.AppendBuffer(frameCallback);

            this.SetComponentEnabled<SyncFrameCallback>(true);
            
            //UnityEngine.Debug.LogError($"Delegate {frameCallback.handle}");
        }
    }
}