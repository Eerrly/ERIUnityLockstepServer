using System.Net.Sockets;

/// <summary>
/// 逻辑层数据
/// </summary>
public class LogicData
{
    /// <summary>
    /// 玩家ID
    /// </summary>
    public uint ID;
    /// <summary>
    /// 房间ID
    /// </summary>
    public uint RoomId;
    /// <summary>
    /// TCP通信流
    /// </summary>
    public NetworkStream NetworkStream;
}

/// <summary>
/// 战斗层数据
/// </summary>
public class BattleData
{
    /// <summary>
    /// 位置
    /// </summary>
    public int Pos;
    /// <summary>
    /// KCP通信ID
    /// </summary>
    public int ConnectionId;
    /// <summary>
    /// 帧数据
    /// </summary>
    public byte[] Frames;
}

/// <summary>
/// 玩家信息
/// </summary>
public class GamerInfo 
{
    /// <summary>
    /// 账号
    /// </summary>
    public string Account;
    
    /// <summary>
    /// 密码
    /// </summary>
    public string Password;
    
    /// <summary>
    /// 逻辑层数据
    /// </summary>
    public LogicData LogicData;
    
    /// <summary>
    /// 战斗层数据
    /// </summary>
    public BattleData BattleData;

    public GamerInfo()
    {
        LogicData = new LogicData();
        BattleData = new BattleData() { Frames = new byte[BattleSetting.MaxFrameCount] };
    }
}