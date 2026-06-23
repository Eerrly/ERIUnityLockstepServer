using System.Net.Sockets;

public class LogicData
{
    public uint ID;

    public uint RoomId;

    /// <summary>
    /// 客户端 TCP 流
    /// </summary>
    public NetworkStream NetworkStream;
}

public enum BattleConnectionState
{
    None = 0,
    Online = 1,
    Disconnected = 2,
    Reconnecting = 3,
}

public class BattleData
{
    public int Pos;

    /// <summary>
    /// 客户端 KCP 连接ID
    /// </summary>
    public int ConnectionId;

    /// <summary>
    /// 战斗连接状态
    /// </summary>
    public BattleConnectionState ConnectionState;

    /// <summary>
    /// 客户端确认收到的最后一帧
    /// </summary>
    public uint LastReceivedFrame;

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
        BattleData = new BattleData()
        {
            Pos = -1,
            ConnectionId = -1,
            ConnectionState = BattleConnectionState.None,
            LastReceivedFrame = 0,
            Frames = new byte[BattleSetting.MaxFrameCount],
        };
    }
}
