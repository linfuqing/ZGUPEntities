using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
//using UnityEngine.SceneManagement;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public interface IGameObjectEntity
    {
        Entity entity { get; }

        World world { get; }
    }

    public struct GameObjectEntityWrapper : IGameObjectEntity
    {
        public Entity entity { get; }

        public World world { get; }

        public GameObjectEntityWrapper(Entity entity, World world)
        {
            this.entity = entity;
            this.world = world;
        }
    }

    public struct GameObjectEntityHandle : IBufferElementData
    {
        public GCHandle value; 
    }

    public class GameObjectEntity : GameObjectEntityDefinition, IGameObjectEntity, ISerializationCallbackReceiver
    {
        public enum Status
        {
            None,
            Deserializing,
            Creating,
            Created,
            Destroied,
            Invalid
        }

        public enum DeserializedType
        {
            Normal,
            IgnoreSceneLoading,
            InstanceOnly
        }

        internal struct DestroiedEntity
        {
            public int instanceID;
            public Status status;
            public Entity entity;
            public GameObjectEntityInfo info;

            public DestroiedEntity(GameObjectEntity instance)
            {
                instanceID = instance.__instanceID;
                status = instance.status;
                entity = instance.__entity;
                info = instance.__info;
            }
        }

        internal Action __onCreated = null;

        [SerializeField]
        internal string _worldName;

        [SerializeField, HideInInspector]
        private GameObjectEntityInfo __info;

        [SerializeField]
        internal GameObjectEntity _parent;

        private Entity __entity;

        private int __instanceID;

        private GameObjectEntity __next;
        private static volatile GameObjectEntity __deserializedEntities = null;
        //private static ConcurrentDictionary<int, GameObjectEntityInfo> __instancedEntities = new ConcurrentDictionary<int, GameObjectEntityInfo>();
        private static ConcurrentBag<DestroiedEntity> __destoriedEntities = new ConcurrentBag<DestroiedEntity>();

        private static List<ComponentType> __componentTypes;

        //private static ConcurrentDictionary<int, GameObjectEntityInfo> __infos = new ConcurrentDictionary<int, GameObjectEntityInfo>();
        //private static Dictionary<Scene, LinkedList<GameObjectEntity>> __sceneEntities = null;
        //private LinkedListNode<GameObjectEntity> __sceneLinkedListNode;

        /*[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void __Init()
        {
            SceneManager.sceneUnloaded += __DestroyEntities;
        }

        public static void __DestroyEntities(Scene scene)
        {
            if (__sceneEntities == null || !__sceneEntities.TryGetValue(scene, out var sceneEntities))
                return;

            LinkedListNode<GameObjectEntity> sceneLinkedListNode = sceneEntities == null ? null : sceneEntities.First;
            while(sceneLinkedListNode != null)
            {
                sceneLinkedListNode.Value.OnDestroy();

                sceneLinkedListNode = sceneEntities.First;
            }
        }*/

        private static ComponentTypeSet __CreateComponentTypes(List<ComponentType> componentTypes)
        {
            switch(componentTypes.Count)
            {
                case 0:
                    return default;
                case 1:
                    return new ComponentTypeSet(componentTypes[0]);
                case 2:
                    return new ComponentTypeSet(componentTypes[0], componentTypes[1]);
                case 3:
                    return new ComponentTypeSet(componentTypes[0], componentTypes[1], componentTypes[2]);
                case 4:
                    return new ComponentTypeSet(componentTypes[0], componentTypes[1], componentTypes[2], componentTypes[3]);
                case 5:
                    return new ComponentTypeSet(componentTypes[0], componentTypes[1], componentTypes[2], componentTypes[3], componentTypes[4]);
                default:
                    return new ComponentTypeSet(componentTypes.ToArray());
            }
        }

        private static bool __CreateDeserializedEntity(ref DeserializedType type)
        {
            GameObjectEntity deserializedEntity;
            do
            {
                deserializedEntity = __deserializedEntities;
            } while ((object)deserializedEntity != null && Interlocked.CompareExchange(ref __deserializedEntities, deserializedEntity.__next, deserializedEntity) != deserializedEntity);

            if (deserializedEntity == null)
                return (object)deserializedEntity == null ? false : __CreateDeserializedEntity(ref type);

            deserializedEntity.__next = null;

            if (deserializedEntity.status != Status.Deserializing)
                return __CreateDeserializedEntity(ref type);

            if (!deserializedEntity.isInstance)
            {
                if (type == DeserializedType.InstanceOnly)
                {
                    bool result = __CreateDeserializedEntity(ref type);

                    deserializedEntity.__Deserialize();

                    return result;
                }
                else
                {
                    if (type != DeserializedType.IgnoreSceneLoading)
                    {
                        bool result;
                        int sceneCount = SceneManager.sceneCount;
                        for (int i = 0; i < sceneCount; ++i)
                        {
                            if (!SceneManager.GetSceneAt(i).isLoaded)
                            {
                                type = DeserializedType.InstanceOnly;

                                result = __CreateDeserializedEntity(ref type);

                                deserializedEntity.__Deserialize();

                                return result;
                            }
                        }

                        type = DeserializedType.IgnoreSceneLoading;
                    }

                    var scene = deserializedEntity == null ? default : deserializedEntity.gameObject.scene;
                    if (scene.IsValid())
                    {
                        /*if (__sceneEntities == null)
                            __sceneEntities = new Dictionary<Scene, LinkedList<GameObjectEntity>>();

                        if(!__sceneEntities.TryGetValue(scene, out var sceneEntities))
                        {
                            sceneEntities = new LinkedList<GameObjectEntity>();

                            __sceneEntities[scene] = sceneEntities;
                        }

                        deserializedEntity.__sceneLinkedListNode = sceneEntities.AddLast(deserializedEntity);*/
                        deserializedEntity.__BuildArchetypeIfNeed(false);
                    }
                    else
                    {
                        bool result = __CreateDeserializedEntity(ref type);

                        deserializedEntity.status = Status.Invalid;

                        //Debug.LogError("Invalid GameObject Entity!", deserializedEntity);

                        return result;
                    }
                }
            }

            deserializedEntity.__Rebuild();

            return true;
        }

        public static void CreateAllDeserializedEntities()
        {
            var type = DeserializedType.Normal;
            while (__CreateDeserializedEntity(ref type)) ;
            /*bool isSceneLoading = true;
            int sceneCount, i;
            EntityArchetype archetype;
            lock (__deserializedEntities)
            {
                foreach(var deserializedEntity in __deserializedEntities)
                {
                    if (deserializedEntity == null || deserializedEntity.isAwake)
                        continue;

                    UnityEngine.Assertions.Assert.AreEqual(Status.None, deserializedEntity.status);

                    if (deserializedEntity.isInstance)
                        archetype = deserializedEntity.__info.entityArchetype;
                    else
                    {
                        if (isSceneLoading)
                        {
                            sceneCount = SceneManager.sceneCount;
                            for (i = 0; i < sceneCount; ++i)
                            {
                                if (!SceneManager.GetSceneAt(i).isLoaded)
                                    return;
                            }

                            isSceneLoading = false;
                        }

                        if (deserializedEntity != null && deserializedEntity.gameObject.scene.IsValid())
                            archetype = deserializedEntity.__GetOrBuildArchetype();
                        else
                            continue;
                    }

                    deserializedEntity.status = Status.Creating;

                    deserializedEntity.CreateEntity(archetype, deserializedEntity.__CreateAndInit);
                }

                __deserializedEntities.Clear();
            }*/
        }

        public static void DisposeAllDestoriedEntities()
        {
            while (__destoriedEntities.TryTake(out var destroiedEntity))
                destroiedEntity.Execute();
        }

        public event Action onCreated
        {
            add
            {
                if (isCreated)
                    value();

                __onCreated += value;
            }

            remove
            {
                __onCreated -= value;
            }
        }

        public bool isInstance { get; private set; }

        public bool isCreated
        {
            get
            {
                if (status == Status.Created)
                {
                    var world = this.world;
                    if (world != null)
                        return world.IsCreated;
                }

                return false;
            }
        }

        public bool isAssigned
        {
            get
            {
                if (status == Status.Creating || status == Status.Created)
                {
                    var world = this.world;
                    if (world != null)
                        return world.IsCreated;
                }

                return false;
            }
        }

        public Status status
        {
            get;

            private set;
        }

        public Entity entity
        {
            get
            {
                if (status != Status.Created)
                    this.ExecuteAllCommands();

                UnityEngine.Assertions.Assert.AreNotEqual(Entity.Null, __entity, name);

                return __entity;
            }
        }

        public string worldName
        {
            get
            {
                return _worldName;
            }

            set
            {
                if (isCreated)
                    throw new InvalidOperationException();

                _worldName = value;
            }

            /*set
            {
                if (_worldName == value)
                    return;

                UnityEngine.Assertions.Assert.AreNotEqual(Status.Creating, status);
#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPlaying)
#endif
                {
                    if (__entity != Entity.Null)
                    {
                        this.DestroyEntity(__entity);

                        __entity = Entity.Null;
                    }

                    __info = GameObjectEntityInfo.Create(value);
                    if (__data != null && __data.isBuild)
                        __info.Rebuild(__data);
                }

                _worldName = value;
                
                if (isCreated)
                    __Rebuild();
            }*/
        }

        public World world
        {
            get
            {
                if (__info == null || !__info.isValid)
                {
                    __instanceID = GetInstanceID();

                    if (__info != null && __info.instanceID == __instanceID)
                        Destroy(__info);

                    __info = GameObjectEntityInfo.Create(__instanceID, componentHash, _worldName);
                }

                return __info == null ? null : __info.world;
            }
        }

        public EntityManager entityManager
        {
            get
            {
                return world.EntityManager;
            }
        }

        public GameObjectEntity parent
        {
            get
            {
                return _parent;
            }
        }

        public GameObjectEntity root
        {
            get
            {
                if (_parent == null)
                    return this;

                return _parent.root;
            }
        }

        internal DestroiedEntity destroiedEntity
        {
            get
            {
                DestroiedEntity destroiedEntity;
                destroiedEntity.instanceID = __instanceID;
                destroiedEntity.status = status;
                destroiedEntity.entity = __entity;
                destroiedEntity.info = __info;

                return destroiedEntity;
            }
        }
        ~GameObjectEntity()
        {
            if (status != Status.Destroied && (object)__info != null)
                __destoriedEntities.Add(destroiedEntity);
        }

        public new bool Contains(Type type)
        {
            __BuildArchetypeIfNeed(false);

            return base.Contains(type);
        }

        public void RebuildArchetype()
        {
            UnityEngine.Assertions.Assert.AreNotEqual(Status.Creating, status);
            if (__entity != Entity.Null)
            {
                if (Status.Creating == status)
                    this.GetFactory().DestroyEntity(__entity);
                else
                    this.DestroyEntity(__entity);

                __entity = Entity.Null;
            }

            __RebuildArchetype(true);

            if (isCreated)
                __Rebuild();
        }

        protected void Awake()
        {
            __ForceBuildIfNeed();
        }

#if UNITY_EDITOR
        private bool __isNamed;
#endif

        protected void OnEnable()
        {
            if (isCreated)
            {
#if UNITY_EDITOR
                if (!__isNamed)
                {
                    __isNamed = true;

                    entityManager.SetName(entity, name);
                }
#endif
                var entityStatus = this.GetComponentData<EntityStatus>();
                ++entityStatus.activeCount;
                this.SetComponentData(entityStatus);
            }
        }

        protected void OnDisable()
        {
            if (isCreated)
            {
                var entityStatus = this.GetComponentData<EntityStatus>();
                --entityStatus.activeCount;
                this.SetComponentData(entityStatus);
            }

            /*if (__entity != Entity.Null)
            {
                if (onChanged != null)
                    onChanged.Invoke(Entity.Null);

                this.DestroyEntity(__entity);

                __entity = Entity.Null;
            }*/
        }

        protected void OnDestroy()
        {
            /*if (__sceneLinkedListNode != null)
            {
                __sceneLinkedListNode.List.Remove(__sceneLinkedListNode);

                __sceneLinkedListNode = null;
            }*/

            /*bool isInstance = this.isInstance;
            if (!isInstance)
                __infos.TryRemove(GetInstanceID(), out _);*/

            if ((object)__info != null)
            {
                destroiedEntity.Execute();

                __info = null;
            }

            __entity = Entity.Null;
            status = Status.Destroied;
        }

        internal void _Create(in Entity entity)
        {
            UnityEngine.Assertions.Assert.IsFalse(__entity.Index > 0);
            UnityEngine.Assertions.Assert.AreEqual(Status.Creating, status);
            //UnityEngine.Assertions.Assert.AreEqual(Entity.Null, __entity);

            //__info.SetComponents(entity, __data, __components);

            /*if (entity == new Entity() { Index = 26453, Version = 1 })
                Debug.LogError(name, this);*/

#if UNITY_EDITOR
            entityManager.SetName(entity, name);
#endif
            __entity = entity;

            status = Status.Created;

            if (__onCreated != null)
                __onCreated();
        }

        private bool __ForceBuildIfNeed()
        {
            if (__entity == Entity.Null)
            {
                if (status != Status.Creating)
                {
                    UnityEngine.Assertions.Assert.AreEqual(Status.Deserializing, status);

                    __BuildArchetypeIfNeed(false);

                    __Rebuild();
                }

                return true;
            }

            return false;
        }

        private void __Rebuild()
        {
            status = Status.Creating;

            if (__componentTypes == null)
                __componentTypes = new List<ComponentType>();
            else
                __componentTypes.Clear();

            GetRuntimeComponentTypes(__componentTypes);

            if (_parent == null)
            {
                var parent = transform.parent;
                if (parent != null)
                {
                    _parent = parent.GetComponentInParent<GameObjectEntity>(true);
                    if(_parent != null)
                        __componentTypes.Add(ComponentType.ReadOnly<EntityParent>());
                }
            }

            var componentTypes = __CreateComponentTypes(__componentTypes);

            __entity = CreateEntityDefinition(__info, out var assigner, componentTypes);

            GameObjectEntityHandle handle;
            handle.value = GCHandle.Alloc(this);
            assigner.SetBuffer(false, __entity, handle);

            EntityOrigin origin;
            origin.entity = __entity;
            assigner.SetComponentData(__entity, origin);

            if (_parent != null)
            {
                _parent.__ForceBuildIfNeed();

                EntityParent entityParent;
                entityParent.entity = _parent.__entity;
                assigner.SetComponentData(__entity, entityParent);
            }
        }

        private void __RebuildArchetype(bool isPrefab)
        {
            var data = Rebuild();

            if (__info == null || !__info.isValid || __info.componentHash != componentHash)
            {
                __instanceID = GetInstanceID();
                if (__info != null && __info.instanceID == __instanceID)
                    __info.Destroy();

                __info = GameObjectEntityInfo.Create(__instanceID, componentHash, _worldName);
                __info.name = name;
            }

            if (_parent == null)
            {
                var parent = transform.parent;
                if (parent != null)
                    _parent = parent.GetComponentInParent<GameObjectEntity>(true);
            }

            if(_parent == null)
                __info.Rebuild(
                    isPrefab, 
                    data, 
                    ComponentType.ReadOnly<GameObjectEntityHandle>(),
                    ComponentType.ReadOnly<EntityStatus>(), 
                    ComponentType.ReadOnly<EntityOrigin>());
            else
                __info.Rebuild(
                    isPrefab, 
                    data, 
                    ComponentType.ReadOnly<GameObjectEntityHandle>(),
                    ComponentType.ReadOnly<EntityStatus>(),
                    ComponentType.ReadOnly<EntityOrigin>(), 
                    ComponentType.ReadOnly<EntityParent>());

            /*if(isPrefab)
                __infos[GetInstanceID()] = __info;*/
        }

        private void __BuildArchetypeIfNeed(bool isPrefab)
        {
            if (__info == null || !__info.isValid)
            {
                /*if (isPrefab && __infos.TryGetValue(GetInstanceID(), out __info) && __info != null && __info.isValid)
                {
                    Rebuild();

                    if(componentHash == __info.componentHash)
                        return;
                }*/

                __RebuildArchetype(isPrefab);
            }
        }

        private void __Deserialize()
        {
            UnityEngine.Assertions.Assert.IsNull(__next);
            do
            {
                __next = __deserializedEntities;
            } while (Interlocked.CompareExchange(ref __deserializedEntities, this, __next) != __next);
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (status != Status.None)
                return;
            
            status = Status.Deserializing;

            isInstance = __info != null && __info.isValid;

            __Deserialize();

            /*EntityArchetype archetype = __info == null ? default : __info.entityArchetype;
            if (archetype.Valid)
            {
                status = Status.Creating;

                this.CreateEntity(archetype, __CreateAndInit);
            }*/
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlaying)
                return;
#endif

            //_parent = null;

            __BuildArchetypeIfNeed(!gameObject.scene.IsValid());
        }
    }

    public static partial class GameObjectEntityUtility
    {
        public static ref readonly Unity.Core.TimeData GetTimeData(this IGameObjectEntity gameObjectEntity)
        {
            return ref  __GetCommandSystem(gameObjectEntity).World.Time;
        }

        public static void ExecuteAllCommands(this IGameObjectEntity gameObjectEntity)
        {
#if UNITY_EDITOR
            Debug.LogWarning("Execute All Game Object Entity Commands");
#endif

            var world = gameObjectEntity.world;
            gameObjectEntity.world.GetExistingSystem<BeginFrameEntityCommandSystem>().Update(world.Unmanaged);
        }

        public static EntityCommandFactory GetFactory(this IGameObjectEntity gameObjectEntity)
        {
            return __GetCommandSystem(gameObjectEntity).factory;
        }

        public static void DestroyEntity(this IGameObjectEntity gameObjectEntity, Entity entity)
        {
            __GetCommandSystem(gameObjectEntity).DestroyEntity(entity);
        }

        public static void DestroyEntity(this IGameObjectEntity gameObjectEntity, NativeArray<Entity> entities)
        {
            __GetCommandSystem(gameObjectEntity).DestroyEntity(entities);
        }

        public static bool AddComponent<T>(this IGameObjectEntity gameObjectEntity)
        {
            return __GetCommandSystem(gameObjectEntity).AddComponent<T>(gameObjectEntity.entity);
        }

        public static void AddComponentData<T>(this IGameObjectEntity gameObjectEntity, T value) where T : struct, IComponentData
        {
            __GetCommandSystem(gameObjectEntity).AddComponentData(gameObjectEntity.entity, value);
        }

        public static void AddBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).AddBuffer(gameObjectEntity.entity, values);
        }
        
        public static void AppendBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).AppendBuffer<T, T[]>(gameObjectEntity.entity, values);
        }

        public static void AppendBuffer<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, params T[] values) where T : unmanaged, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).AppendBuffer<T, T[]>(entity, values);
        }

        public static void AppendBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, TCollection values) 
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            __GetCommandSystem(gameObjectEntity).AppendBuffer<TValue, TCollection>(gameObjectEntity.entity, values);
        }

        public static bool RemoveComponent<T>(this IGameObjectEntity gameObjectEntity)
        {
            return __GetCommandSystem(gameObjectEntity).RemoveComponent<T>(gameObjectEntity.entity);
        }
        
        public static void SetComponentData<T>(this IGameObjectEntity gameObjectEntity, T value) where T : struct, IComponentData
        {
            __GetCommandSystem(gameObjectEntity).SetComponentData(gameObjectEntity.entity, value);
        }

        public static void SetComponentData<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, T value) where T : struct, IComponentData
        {
            __GetCommandSystem(gameObjectEntity).SetComponentData(entity, value);
        }

        public static void SetBuffer<T>(this IGameObjectEntity gameObjectEntity, params T[] values) where T : unmanaged, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).SetBuffer(gameObjectEntity.entity, values);
        }

        public static void SetBuffer<T>(this IGameObjectEntity gameObjectEntity, in NativeArray<T> values) where T : struct, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).SetBuffer(gameObjectEntity.entity, values);
        }

        public static void SetBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, TCollection values) 
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            __GetCommandSystem(gameObjectEntity).SetBuffer<TValue, TCollection>(gameObjectEntity.entity, values);
        }

        public static void SetBuffer<TValue, TCollection>(this IGameObjectEntity gameObjectEntity, in Entity entity, TCollection values)
            where TValue : unmanaged, IBufferElementData
            where TCollection : IReadOnlyCollection<TValue>
        {
            __GetCommandSystem(gameObjectEntity).SetBuffer<TValue, TCollection>(entity, values);
        }

        public static void SetComponentEnabled<T>(this IGameObjectEntity gameObjectEntity, bool value)
            where T : unmanaged, IEnableableComponent
        {
            __GetCommandSystem(gameObjectEntity).SetComponentEnabled<T>(gameObjectEntity.entity, value);
        }

        public static void SetComponentEnabled<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, bool value)
            where T : unmanaged, IEnableableComponent
        {
            __GetCommandSystem(gameObjectEntity).SetComponentEnabled<T>(entity, value);
        }

        public static void SetSharedComponentData<T>(this IGameObjectEntity gameObjectEntity, T value) where T : struct, ISharedComponentData
        {
            __GetCommandSystem(gameObjectEntity).SetSharedComponentData(gameObjectEntity.entity, value);
        }
        
        public static void SetComponentObject<T>(this IGameObjectEntity gameObjectEntity, EntityObject<T> value)
        {
            __GetCommandSystem(gameObjectEntity).SetComponentObject(gameObjectEntity.entity, value);
        }

        public static bool TryGetComponentData<T>(this IGameObjectEntity gameObjectEntity, out T value) where T : unmanaged, IComponentData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentData(gameObjectEntity.entity, out value);
        }

        public static bool TryGetComponentData<T>(this IGameObjectEntity gameObjectEntity, Entity entity, out T value) where T : unmanaged, IComponentData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentData(entity, out value);
        }

        public static bool TryGetBuffer<T>(this IGameObjectEntity gameObjectEntity, int index, out T value) where T : unmanaged, IBufferElementData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetBuffer(gameObjectEntity.entity, index, out value);
        }

        public static bool TryGetBuffer<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, int index, out T value) where T : unmanaged, IBufferElementData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetBuffer(entity, index, out value);
        }

        public static bool TryGetBuffer<TValue, TList, TWrapper>(
            this IGameObjectEntity gameObjectEntity,
            in Entity entity, 
            ref TList list,
            ref TWrapper wrapper)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            return __GetCommandSystem(gameObjectEntity).TryGetBuffer<TValue, TList, TWrapper>(entity, ref list, ref wrapper);
        }

        public static bool TryGetBuffer<TValue, TList, TWrapper>(
            this IGameObjectEntity gameObjectEntity,
            ref TList list,
            ref TWrapper wrapper)
            where TValue : unmanaged, IBufferElementData
            where TWrapper : IWriteOnlyListWrapper<TValue, TList>
        {
            return __GetCommandSystem(gameObjectEntity).TryGetBuffer<TValue, TList, TWrapper>(gameObjectEntity.entity, ref list, ref wrapper);
        }

        public static bool TryGetComponentObject<T>(this IGameObjectEntity gameObjectEntity, out T value)
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentObject(gameObjectEntity.entity, out value);
        }

        public static bool TryGetComponentObject<T>(this IGameObjectEntity gameObjectEntity, Entity entity, out T value)
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentObject(entity, out value);
        }

        public static T GetComponentData<T>(this IGameObjectEntity gameObjectEntity) where T : unmanaged, IComponentData
        {
            __GetCommandSystem(gameObjectEntity).TryGetComponentData<T>(gameObjectEntity.entity, out var value);

            return value;
        }

        public static T GetComponentData<T>(this IGameObjectEntity gameObjectEntity, Entity entity) where T : unmanaged, IComponentData
        {
            __GetCommandSystem(gameObjectEntity).TryGetComponentData<T>(entity, out var value);

            return value;
        }

        public static T[] GetBuffer<T>(this IGameObjectEntity gameObjectEntity, Entity entity) where T : unmanaged, IBufferElementData
        {
            var list = new NativeList<T>(Allocator.Temp);
            NativeListWriteOnlyWrapper<T> wrapper;
            if (__GetCommandSystem(gameObjectEntity).TryGetBuffer<T, NativeList<T>, NativeListWriteOnlyWrapper<T>>(entity, ref list, ref wrapper))
            {
                int length = list.Length;
                if (length > 0)
                {
                    var result = new T[length];
                    for (int i = 0; i < length; ++i)
                        result[i] = list[i];

                    list.Dispose();

                    return result;
                }
            }
            list.Dispose();

            return null;
        }

        public static T[] GetBuffer<T>(this IGameObjectEntity gameObjectEntity) where T : unmanaged, IBufferElementData
        {
            return GetBuffer<T>(gameObjectEntity, gameObjectEntity.entity);
        }

        public static T GetBuffer<T>(this IGameObjectEntity gameObjectEntity, int index) where T : unmanaged, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).TryGetBuffer<T>(gameObjectEntity.entity, index, out var value);

            return value;
        }

        public static T GetBuffer<T>(this IGameObjectEntity gameObjectEntity, Entity entity, int index) where T : unmanaged, IBufferElementData
        {
            __GetCommandSystem(gameObjectEntity).TryGetBuffer<T>(entity, index, out var value);

            return value;
        }

        public static T GetComponentObject<T>(this IGameObjectEntity gameObjectEntity) where T : class
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentObject(gameObjectEntity.entity, out T value) ? value : null;
        }

        public static T GetComponentObject<T>(this IGameObjectEntity gameObjectEntity, Entity entity) where T : class
        {
            return __GetCommandSystem(gameObjectEntity).TryGetComponentObject(entity, out T value) ? value : null;
        }

        public static bool TryGetSharedComponentData<T>(this IGameObjectEntity gameObjectEntity, out T value) where T : struct, ISharedComponentData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetSharedComponentData(gameObjectEntity.entity, out value);
        }

        public static bool TryGetSharedComponentData<T>(this IGameObjectEntity gameObjectEntity, in Entity entity, out T value) where T : struct, ISharedComponentData
        {
            return __GetCommandSystem(gameObjectEntity).TryGetSharedComponentData(entity, out value);
        }

        public static bool HasComponent<T>(this IGameObjectEntity gameObjectEntity)
        {
            return __GetCommandSystem(gameObjectEntity).HasComponent<T>(gameObjectEntity.entity);
        }

        public static bool HasComponent<T>(this IGameObjectEntity gameObjectEntity, Entity entity)
        {
            return __GetCommandSystem(gameObjectEntity).HasComponent<T>(entity);
        }

        internal static void Execute(this in GameObjectEntity.DestroiedEntity destroiedEntity)
        {
            bool isPrefab = destroiedEntity.instanceID == destroiedEntity.info.instanceID;

            var world = destroiedEntity.info.world;
            if (world != null && world.IsCreated)
            {
                var commandSystem = __GetCommandSystem(world);

                if (isPrefab && destroiedEntity.info.isValidPrefab)
                {
                    commandSystem.factory.DestroyEntity(destroiedEntity.info.prefab);

                    //destroiedEntity.info.SetPrefab(Entity.Null);
                }

                if (destroiedEntity.status == GameObjectEntity.Status.Created)
                    commandSystem.DestroyEntity(destroiedEntity.entity);
            }

            if (isPrefab)
            {
#if UNITY_EDITOR
                if (Application.isPlaying)
#endif
                UnityEngine.Object.Destroy(destroiedEntity.info);
            }
        }

        private static EntityCommandSharedSystemGroup __GetCommandSystem(IGameObjectEntity gameObjectEntity) => __GetCommandSystem(gameObjectEntity.world);
    }
}