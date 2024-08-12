using System.Diagnostics;

public class RoomInfo
{
    public uint RoomId;
    
    /// <summary>
    /// 服务器当前帧
    /// </summary>
    public int AuthoritativeFrame;
    
    public List<uint> Readies;
    
    public List<uint> Gamers;
    
    /// <summary>
    /// 每一帧玩家是否操作了的标记数组
    /// </summary>
    public byte[] InputCounts;
    
    public Stopwatch BattleStopwatch;

    /// <summary>
    /// 某一帧玩家的MD5校验值数组
    /// </summary>
    public Dictionary<int, List<int>> BattleCheckMap;

    public RoomInfo()
    {
        AuthoritativeFrame = -1;
        Readies = new List<uint>();
        Gamers = new List<uint>();
        InputCounts = new byte[BattleSetting.MaxFrameCount];
        BattleCheckMap = new Dictionary<int, List<int>>();
        BattleStopwatch = new Stopwatch();
    }
}