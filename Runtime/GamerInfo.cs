using System.Net.Sockets;

public class LogicData
{
    public uint ID;
    
    public uint RoomId;

    /// <summary>
    /// 客户端TCP流
    /// </summary>
    public NetworkStream NetworkStream;
}

public class BattleData
{
    public int Pos;

    /// <summary>
    /// 客户端KCP连接ID
    /// </summary>
    public int ConnectionId;

    public byte[] Frames;
}

public class GamerInfo 
{
    public string Account;
    
    public string Password;
    
    public LogicData LogicData;
    
    public BattleData BattleData;

    public GamerInfo()
    {
        LogicData = new LogicData();
        BattleData = new BattleData() { Frames = new byte[BattleSetting.MaxFrameCount] };
    }
}