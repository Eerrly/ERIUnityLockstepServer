/// <summary>
/// 游戏管理器
/// </summary>
public class GameManager : AManager<GameManager>
{
    /// <summary>
    /// 所有玩家信息，key: 玩家ID
    /// </summary>
    public Dictionary<uint, GamerInfo> GamerInfoDic => _gamerInfoDic;
    private Dictionary<uint, GamerInfo> _gamerInfoDic;

    /// <summary>
    /// 所有玩家信息，key: 战斗 Pos
    /// </summary>
    private Dictionary<int, GamerInfo> _gamerInfoByPosDic;

    /// <summary>
    /// 所有玩家信息，key: 账号+密码
    /// </summary>
    private Dictionary<string, GamerInfo> _gamerInfoByAccountPassword;

    /// <summary>
    /// 所有玩家信息，key: KCP 连接ID
    /// </summary>
    private Dictionary<int, GamerInfo> _gamerInfoByConnectionId;

    /// <summary>
    /// 所有房间信息，key: 房间ID
    /// </summary>
    private Dictionary<uint, RoomInfo> _roomInfoDic;

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize(params object[] objs)
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
    public GamerInfo GetOrCreateGamer(string account, string password)
    {
        if (_gamerInfoByAccountPassword.TryGetValue(account + password, out var gamer))
            return gamer;

        gamer = new GamerInfo() { Account = account, Password = password };
        gamer.LogicData.ID = GameSetting.DefaultPlayerIdBase + (uint)_gamerInfoDic.Count + 1;
        _gamerInfoDic[gamer.LogicData.ID] = gamer;
        _gamerInfoByAccountPassword[account + password] = gamer;
        return gamer;
    }

    /// <summary>
    /// 更新玩家战斗 Pos
    /// </summary>
    public void UpdateGamerPos(uint playerId, int pos)
    {
        var gamer = _gamerInfoDic[playerId];
        gamer.BattleData.Pos = pos;
        _gamerInfoByPosDic[pos] = gamer;
    }

    /// <summary>
    /// 更新玩家 KCP 连接ID，并替换旧映射
    /// </summary>
    public void UpdateGamerConnectionId(uint playerId, int connectionId)
    {
        var gamer = _gamerInfoDic[playerId];
        if (gamer.BattleData.ConnectionId >= 0)
            _gamerInfoByConnectionId.Remove(gamer.BattleData.ConnectionId);

        gamer.BattleData.ConnectionId = connectionId;
        gamer.BattleData.ConnectionState = BattleConnectionState.Online;
        _gamerInfoByConnectionId[connectionId] = gamer;
    }

    /// <summary>
    /// 通过玩家ID获取玩家对象
    /// </summary>
    public GamerInfo GetGamerById(uint playerId)
    {
        return _gamerInfoDic[playerId];
    }

    /// <summary>
    /// 尝试通过玩家ID获取玩家对象
    /// </summary>
    public bool TryGetGamerById(uint playerId, out GamerInfo? gamer)
    {
        return _gamerInfoDic.TryGetValue(playerId, out gamer);
    }

    /// <summary>
    /// 通过战斗 Pos 获取玩家对象
    /// </summary>
    public GamerInfo GetGamerByPos(int pos)
    {
        return _gamerInfoByPosDic[pos];
    }

    /// <summary>
    /// 通过 KCP 连接ID 获取玩家对象
    /// </summary>
    public GamerInfo GetGamerByConnectionId(int connectionId)
    {
        return _gamerInfoByConnectionId[connectionId];
    }

    /// <summary>
    /// 尝试通过 KCP 连接ID 获取玩家对象
    /// </summary>
    public bool TryGetGamerByConnectionId(int connectionId, out GamerInfo? gamer)
    {
        return _gamerInfoByConnectionId.TryGetValue(connectionId, out gamer);
    }

    /// <summary>
    /// 移除玩家 KCP 连接映射
    /// </summary>
    public void RemoveGamerConnectionId(uint playerId)
    {
        if (!_gamerInfoDic.TryGetValue(playerId, out var gamer))
            return;

        if (gamer.BattleData.ConnectionId >= 0)
            _gamerInfoByConnectionId.Remove(gamer.BattleData.ConnectionId);

        gamer.BattleData.ConnectionId = -1;
        if (gamer.BattleData.ConnectionState == BattleConnectionState.Online)
            gamer.BattleData.ConnectionState = BattleConnectionState.None;
    }

    /// <summary>
    /// 标记玩家为断线
    /// </summary>
    public void MarkGamerDisconnected(uint playerId)
    {
        if (!_gamerInfoDic.TryGetValue(playerId, out var gamer))
            return;

        if (gamer.BattleData.ConnectionId >= 0)
            _gamerInfoByConnectionId.Remove(gamer.BattleData.ConnectionId);

        gamer.BattleData.ConnectionId = -1;
        gamer.BattleData.ConnectionState = BattleConnectionState.Disconnected;
    }

    /// <summary>
    /// 尝试获取玩家可重连的房间
    /// </summary>
    public bool TryGetReconnectRoom(uint playerId, out RoomInfo? room)
    {
        room = null;
        if (!_gamerInfoDic.TryGetValue(playerId, out var gamer))
            return false;

        if (gamer.BattleData.ConnectionState != BattleConnectionState.Disconnected)
            return false;

        if (gamer.BattleData.ConnectionId >= 0)
            return false;

        if (gamer.LogicData.RoomId == 0)
            return false;

        if (!_roomInfoDic.TryGetValue(gamer.LogicData.RoomId, out room) || room == null)
            return false;

        if (!room.Gamers.Contains(playerId))
            return false;

        if (!room.IsBattleRunning || room.IsBattleExiting)
            return false;

        if (gamer.BattleData.Pos < 0)
            return false;

        return true;
    }

    /// <summary>
    /// 创建房间对象
    /// </summary>
    public RoomInfo CreateRoom()
    {
        var room = new RoomInfo() { RoomId = GameSetting.DefaultRoomIdBase + (uint)_roomInfoDic.Count + 1 };
        _roomInfoDic[room.RoomId] = room;
        return room;
    }

    /// <summary>
    /// 获取房间对象
    /// </summary>
    public RoomInfo GetRoom(uint roomId)
    {
        return _roomInfoDic[roomId];
    }

    /// <summary>
    /// 尝试获取房间对象
    /// </summary>
    public bool TryGetRoom(uint roomId, out RoomInfo? room)
    {
        return _roomInfoDic.TryGetValue(roomId, out room);
    }

    /// <summary>
    /// 重置房间战斗状态
    /// </summary>
    public bool ResetRoomBattleState(uint roomId)
    {
        if (!_roomInfoDic.TryGetValue(roomId, out var room))
            return false;

        room.ResetBattleState();
        return true;
    }
}
