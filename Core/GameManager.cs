using System.Net.Sockets;

public class GamerInfo
{
    public int ConnectionId;
    public int PlayerId;
    public int RoomId;
    public int Pos;
    public byte[] CacheFrames;
    public bool Ready;
    public NetworkStream NetworkStream;

    public GamerInfo()
    {
        ConnectionId = -1;
        Pos = -1;
        RoomId = -1;
        PlayerId = -1;
        CacheFrames = new byte[10000];
        NetworkStream = null;
        Ready = false;
    }
}

public class RoomInfo
{
    public int RoomId;
    public int AuthoritativeFrame;
    public List<GamerInfo> Gamers;

    public RoomInfo()
    {
        RoomId = -1;
        AuthoritativeFrame = 0;
        Gamers = new List<GamerInfo>();
    }
}

/// <summary>
/// 游戏管理器
/// </summary>
public class GameManager : AManager<GameManager>
{
    /// <summary>
    /// 房间最大人数
    /// </summary>
    public const int RoomMaxPlayerCount = 2;
    /// <summary>
    /// 玩家ID初始化基底
    /// </summary>
    public const uint DefaultPlayerIdBase = 10000;
    /// <summary>
    /// 房间ID初始化基底
    /// </summary>
    public const uint DefaultRoomIdBase = 20000;

    public List<GamerInfo> Gamers;

    public List<RoomInfo> Rooms;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        Gamers = new List<GamerInfo>();
        Rooms = new List<RoomInfo>();

        InitLogger();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        Gamers.Clear();
        Rooms.Clear();
    }
    
    /// <summary>
    /// 初始化Logger
    /// </summary>
    void InitLogger()
    {
        // Log Path -> bin\Debug\netX.X\server.log
        Logger.Initialize(Path.Combine(Directory.GetCurrentDirectory(), "server.log"), new Logger());
        Logger.SetLoggerLevel((int)LogLevel.Info | (int)LogLevel.Warning | (int)LogLevel.Error | (int)LogLevel.Exception);
        Logger.log = Console.WriteLine;
        Logger.logWarning = Console.WriteLine;
        Logger.logError = Console.WriteLine;
    }

    /// <summary>
    /// 开启服务器
    /// </summary>
    public void StartServer()
    {
        NetworkManager.Instance.StartTcpServer();
    }

    /// <summary>
    /// 加入房间
    /// </summary>
    /// <param name="roomId"></param>
    /// <param name="playerId"></param>
    public RoomInfo? JoinRoom(uint roomId, uint playerId)
    {
        var gamer = GetGamerByPlayerId((int)playerId);
        var room = GetRoomByRoomId((int)roomId);
        if (gamer == null || room == null) 
            return null;
        
        gamer.Pos = room.Gamers.Count;
        gamer.RoomId = room.RoomId;
        room.Gamers.Add(gamer);

        if (room.Gamers.Count == RoomMaxPlayerCount) NetworkManager.Instance.StartKcpServer();
        return room;
    }

    public GamerInfo? GetGamerByPlayerId(int playerId)
    {
        foreach (var g in Gamers.Where(g => g.PlayerId == playerId))
            return g;
        return null;
    }
    
    public GamerInfo? GetGamerByConnectionId(int connectionId)
    {
        foreach (var g in Gamers.Where(g => g.ConnectionId == connectionId))
            return g;
        return null;
    }

    public GamerInfo? GetGamerByPos(int pos)
    {
        foreach (var g in Gamers.Where(g => g.Pos == pos))
            return g;
        return null;
    }

    public RoomInfo? GetRoomByRoomId(int roomId)
    {
        foreach (var r in Rooms.Where(r => r.RoomId == roomId))
            return r;
        return null;
    }

}