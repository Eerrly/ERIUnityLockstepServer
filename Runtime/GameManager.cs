/// <summary>
/// 游戏业务管理器
/// </summary>
public class GameManager : AManager<GameManager>
{
    /// <summary>
    /// 玩家信息列表
    /// </summary>
    public List<GamerInfo> GamerInfos;
    /// <summary>
    /// 房间信息列表
    /// </summary>
    public List<RoomInfo> RoomInfos;

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        GamerInfos = new List<GamerInfo>();
        RoomInfos = new List<RoomInfo>();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        GamerInfos.Clear();
        RoomInfos.Clear();
    }

    /// <summary>
    /// 创建一个玩家对象
    /// </summary>
    /// <param name="account">账号</param>
    /// <param name="password">密码</param>
    /// <returns></returns>
    public GamerInfo CreateGamer(string account, string password)
    {
        var gamer = new GamerInfo(){ Account = account, Password = password };
        gamer.LogicData.ID = GameSetting.DefaultPlayerIdBase + (uint)GamerInfos.Count + 1;
        GamerInfos.Add(gamer);
        return gamer;
    }

    /// <summary>
    /// 通过玩家ID来获取对应的玩家对象
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <returns>玩家对象</returns>
    public GamerInfo GetGamerById(uint playerId)
    {
        foreach(var g in GamerInfos){
            if(g.LogicData.ID == playerId) return g;
        }
        return null;
    }

    /// <summary>
    /// 通过战斗内的位置来获取对应的玩家对象
    /// </summary>
    /// <param name="pos">战斗内的位置</param>
    /// <returns>玩家对象</returns>
    public GamerInfo GetGamerByPos(int pos)
    {
        foreach(var g in GamerInfos){
            if(g.BattleData.Pos == pos) return g;
        }
        return null;
    }

    /// <summary>
    /// 创建房间对象
    /// </summary>
    /// <returns></returns>
    public RoomInfo CreateRoom()
    {
        var room = new RoomInfo() { RoomId = GameSetting.DefaultRoomIdBase + (uint)RoomInfos.Count + 1 };
        RoomInfos.Add(room);
        return room;
    }

    /// <summary>
    /// 通过房间ID来获取房间对象
    /// </summary>
    /// <param name="roomId">房间ID</param>
    /// <returns>房间对象</returns>
    public RoomInfo GetRoom(uint roomId)
    {
        foreach (var r in RoomInfos){
            if(r.RoomId == roomId) return r;
        }
        return null;
    }
    
}