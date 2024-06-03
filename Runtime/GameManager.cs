public class GameManager : AManager<GameManager>
{
    public Dictionary<uint, GamerInfo> GamerInfoDic => _gamerInfoDic;

    private Dictionary<uint, GamerInfo> _gamerInfoDic;
    private Dictionary<int, GamerInfo> _gamerInfoByPosDic;
    private Dictionary<string, GamerInfo> _gamerInfoByAccountPassword;
    private Dictionary<uint, RoomInfo> _roomInfoDic;

    public override void Initialize()
    {
        _gamerInfoDic = new Dictionary<uint, GamerInfo>();
        _gamerInfoByPosDic = new Dictionary<int, GamerInfo>();
        _gamerInfoByAccountPassword = new Dictionary<string, GamerInfo>();
        _roomInfoDic = new Dictionary<uint, RoomInfo>();
    }

    public override void OnRelease()
    {
        _gamerInfoDic.Clear();
        _gamerInfoByPosDic.Clear();
        _gamerInfoByAccountPassword.Clear();
        _roomInfoDic.Clear();
    }

    public GamerInfo GetOrCreateGamer(string account, string password)
    {
        if (_gamerInfoByAccountPassword.TryGetValue(account + password, out var gamer)) return gamer;
        gamer = new GamerInfo(){ Account = account, Password = password };
        gamer.LogicData.ID = GameSetting.DefaultPlayerIdBase + (uint)_gamerInfoDic.Count + 1;
        _gamerInfoDic[gamer.LogicData.ID] = gamer;
        _gamerInfoByAccountPassword[account + password] = gamer;
        return gamer;
    }

    public void UpdateGamerPos(uint playerId, int pos)
    {
        _gamerInfoDic[playerId].BattleData.Pos = pos;
        _gamerInfoByPosDic[pos] = _gamerInfoDic[playerId];
    }

    public GamerInfo GetGamerById(uint playerId)
    {
        return _gamerInfoDic[playerId];
    }

    public GamerInfo GetGamerByPos(int pos)
    {
        return _gamerInfoByPosDic[pos];
    }

    public RoomInfo CreateRoom()
    {
        var room = new RoomInfo() { RoomId = GameSetting.DefaultRoomIdBase + (uint)_roomInfoDic.Count + 1 };
        _roomInfoDic[room.RoomId] = room;
        return room;
    }

    public RoomInfo GetRoom(uint roomId)
    {
        return _roomInfoDic[roomId];
    }
    
}