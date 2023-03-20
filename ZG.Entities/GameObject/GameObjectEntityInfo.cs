namespace ZG
{
    public class GameObjectEntityInfo : GameObjectEntityData.Info
    {
        public static GameObjectEntityInfo Create(int instanceID, int componentHash, string worldName) => Create<GameObjectEntityInfo>(instanceID, componentHash, worldName);
    }
}