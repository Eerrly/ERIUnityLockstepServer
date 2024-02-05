public class GameManager
{
    private static GameManager _instance;
    public static GameManager Instance => _instance ?? (_instance = new GameManager());

    public const int RoomMaxPlayerCount = 2;
    public const uint DefaultPlayerIdBase = 10000;
    public const uint DefaultRoomIdBase = 20000;

    public List<uint> PlayerIdList;
    public List<uint> RoomIdList;
    public Dictionary<uint, List<uint>> RoomInfoDic;

    public List<int> ConnectionIds;
    public List<uint> Readys;

    public Dictionary<int, int[]> CacheFrames;
    
    public void Initialize()
    {
        PlayerIdList = new List<uint>(10);
        RoomIdList = new List<uint>(5);
        RoomInfoDic = new Dictionary<uint, List<uint>>();
        ConnectionIds = new List<int>(10);
        Readys = new List<uint>();

        CacheFrames = new Dictionary<int, int[]>();

        InitLogger();
    }
    
    void InitLogger()
    {
        Logger.Initialize(Path.Combine(Directory.GetCurrentDirectory(), "server.log"), new Logger());
        Logger.SetLoggerLevel((int)LogLevel.Info | (int)LogLevel.Warning | (int)LogLevel.Error | (int)LogLevel.Exception);
        Logger.log = Console.WriteLine;
        Logger.logError = Console.WriteLine;
    }

    public void JoinRoom(uint roomId, uint playerId)
    {
        RoomInfoDic[roomId].Add(playerId);
        
        if (RoomInfoDic[roomId].Count == RoomMaxPlayerCount) NetworkManager.Instance.StartKcpServer();
    }

}