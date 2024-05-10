using System.Diagnostics;

/// <summary>
/// 房间信息
/// </summary>
public class RoomInfo
{
    /// <summary>
    /// 房间ID
    /// </summary>
    public uint RoomId;
    /// <summary>
    /// 服务器权威帧
    /// </summary>
    public int AuthoritativeFrame;
    /// <summary>
    /// 战斗已准备列表
    /// </summary>
    public List<uint> Readies;
    /// <summary>
    /// 房间内的玩家列表
    /// </summary>
    public List<uint> Gamers;
    /// <summary>
    /// 每帧的操作人数
    /// </summary>
    public byte[] InputCounts;
    /// <summary>
    /// 战斗内客户端计时器
    /// </summary>
    public Stopwatch BattleStopwatch;

    public RoomInfo()
    {
        AuthoritativeFrame = -1;
        Readies = new List<uint>();
        Gamers = new List<uint>();
        InputCounts = new byte[BattleSetting.MaxFrameCount];
        BattleStopwatch = new Stopwatch();
    }
}