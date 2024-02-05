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

    /// <summary>
    /// 所有的玩家ID集合
    /// </summary>
    public List<uint> PlayerIdList;
    /// <summary>
    /// 所有的房间ID集合
    /// </summary>
    public List<uint> RoomIdList;
    /// <summary>
    /// 所有的房间信息集合
    /// </summary>
    public Dictionary<uint, List<uint>> RoomInfoDic;
    /// <summary>
    /// 所有的KCP链接Id集合
    /// </summary>
    public List<int> KcpConnectionIds;
    /// <summary>
    /// 玩家准备集合
    /// </summary>
    public List<uint> Readys;
    /// <summary>
    /// 服务器缓存的帧数据字典
    /// </summary>
    public Dictionary<int, int[]> CacheFrames;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        PlayerIdList = new List<uint>(10);
        RoomIdList = new List<uint>(5);
        RoomInfoDic = new Dictionary<uint, List<uint>>();
        KcpConnectionIds = new List<int>(10);
        Readys = new List<uint>();

        CacheFrames = new Dictionary<int, int[]>();

        InitLogger();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        PlayerIdList.Clear();
        RoomIdList.Clear();
        RoomInfoDic.Clear();
        KcpConnectionIds.Clear();
        Readys.Clear();
        CacheFrames.Clear();
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
    public void JoinRoom(uint roomId, uint playerId)
    {
        RoomInfoDic[roomId].Add(playerId);
        if (RoomInfoDic[roomId].Count == RoomMaxPlayerCount) NetworkManager.Instance.StartKcpServer();
    }

}