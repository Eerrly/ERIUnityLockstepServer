/// <summary>
/// 游戏管理器
/// </summary>
public class GameManager : AManager<GameManager>
{
    /// <summary>
    /// 所有玩家信息的字典 key:玩家ID
    /// </summary>
    public Dictionary<uint, GamerInfo> GamerInfoDic => _gamerInfoDic;

    private Dictionary<uint, GamerInfo> _gamerInfoDic;
    /// <summary>
    /// 所有玩家信息的字典 key:玩家POS
    /// </summary>
    private Dictionary<int, GamerInfo> _gamerInfoByPosDic;
    /// <summary>
    /// 所有玩家信息的字典 key:玩家账号密码
    /// </summary>
    private Dictionary<string, GamerInfo> _gamerInfoByAccountPassword;
    /// <summary>
    /// 所有玩家信息的字典 key:玩家KCP连接ID
    /// </summary>
    private Dictionary<int, GamerInfo> _gamerInfoByConnectionId;
    /// <summary>
    /// 所有房间信息的字典 key:房间ID
    /// </summary>
    private Dictionary<uint, RoomInfo> _roomInfoDic;

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        _gamerInfoDic = new Dictionary<uint, GamerInfo>();
        _gamerInfoByPosDic = new Dictionary<int, GamerInfo>();
        _gamerInfoByAccountPassword = new Dictionary<string, GamerInfo>();
        _gamerInfoByConnectionId = new Dictionary<int, GamerInfo>();
        _roomInfoDic = new Dictionary<uint, RoomInfo>();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        _gamerInfoDic.Clear();
        _gamerInfoByPosDic.Clear();
        _gamerInfoByAccountPassword.Clear();
        _gamerInfoByConnectionId.Clear();
        _roomInfoDic.Clear();
    }

    /// <summary>
    /// 获取或创建玩家对象
    /// </summary>
    /// <param name="account">账号</param>
    /// <param name="password">密码</param>
    /// <returns>玩家对象</returns>
    public GamerInfo GetOrCreateGamer(string account, string password)
    {
        if (_gamerInfoByAccountPassword.TryGetValue(account + password, out var gamer)) return gamer;
        gamer = new GamerInfo(){ Account = account, Password = password };
        gamer.LogicData.ID = GameSetting.DefaultPlayerIdBase + (uint)_gamerInfoDic.Count + 1;
        _gamerInfoDic[gamer.LogicData.ID] = gamer;
        _gamerInfoByAccountPassword[account + password] = gamer;
        return gamer;
    }

    /// <summary>
    /// 更新玩家战斗POS到对象信息中
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="pos">战斗POS</param>
    public void UpdateGamerPos(uint playerId, int pos)
    {
        _gamerInfoDic[playerId].BattleData.Pos = pos;
        _gamerInfoByPosDic[pos] = _gamerInfoDic[playerId];
    }

    /// <summary>
    /// 更新玩家KCP连接ID到对象信息中
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="connectionId">KCP连接ID</param>
    public void UpdateGamerConnectionId(uint playerId, int connectionId)
    {
        _gamerInfoDic[playerId].BattleData.ConnectionId = connectionId;
        _gamerInfoByConnectionId[connectionId] = _gamerInfoDic[playerId];
    }

    /// <summary>
    /// 通过玩家ID获取玩家对象
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <returns>玩家对象</returns>
    public GamerInfo GetGamerById(uint playerId)
    {
        return _gamerInfoDic[playerId];
    }

    /// <summary>
    /// 通过战斗POS来获取玩家对象
    /// </summary>
    /// <param name="pos">战斗POS</param>
    /// <returns>玩家对象</returns>
    public GamerInfo GetGamerByPos(int pos)
    {
        return _gamerInfoByPosDic[pos];
    }

    /// <summary>
    /// 通过KCP连接ID来获取玩家对象
    /// </summary>
    /// <param name="connectionId">KCP连接ID</param>
    /// <returns>玩家对象</returns>
    public GamerInfo GetGamerByConnectionId(int connectionId)
    {
        return _gamerInfoByConnectionId[connectionId];
    }

    /// <summary>
    /// 创建房间对象
    /// </summary>
    /// <returns>房间对象</returns>
    public RoomInfo CreateRoom()
    {
        var room = new RoomInfo() { RoomId = GameSetting.DefaultRoomIdBase + (uint)_roomInfoDic.Count + 1 };
        _roomInfoDic[room.RoomId] = room;
        return room;
    }

    /// <summary>
    /// 获取房间对象
    /// </summary>
    /// <param name="roomId">房间ID</param>
    /// <returns>房间对象</returns>
    public RoomInfo GetRoom(uint roomId)
    {
        return _roomInfoDic[roomId];
    }
    
}