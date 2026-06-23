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
    /// 服务器当前帧
    /// </summary>
    public int AuthoritativeFrame;
    /// <summary>
    /// 已准备的玩家ID集合
    /// </summary>
    public List<uint> Readies;
    /// <summary>
    /// 所有玩家ID集合
    /// </summary>
    public List<uint> Gamers;
    
    /// <summary>
    /// 每一帧玩家是否操作了的标记数组
    /// </summary>
    public byte[] InputCounts;
    /// <summary>
    /// 计时器
    /// </summary>
    public Stopwatch BattleStopwatch;
    /// <summary>
    /// 战斗是否正在运行
    /// </summary>
    public bool IsBattleRunning;
    /// <summary>
    /// 战斗是否正在退出流程中
    /// </summary>
    public bool IsBattleExiting;
    /// <summary>
    /// 战斗轮询取消源
    /// </summary>
    public CancellationTokenSource BattleCancellationTokenSource;
    /// <summary>
    /// 当前战斗轮询任务
    /// </summary>
    public Task? BattleTask;

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
        BattleCancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// 重置单局战斗状态，保留房间玩家列表用于后续重新准备
    /// </summary>
    public void ResetBattleState()
    {
        try
        {
            if (!BattleCancellationTokenSource.IsCancellationRequested)
                BattleCancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        BattleCancellationTokenSource.Dispose();

        AuthoritativeFrame = -1;
        Readies.Clear();
        Array.Clear(InputCounts, 0, InputCounts.Length);
        BattleCheckMap.Clear();
        BattleStopwatch.Reset();
        IsBattleRunning = false;
        IsBattleExiting = false;
        BattleCancellationTokenSource = new CancellationTokenSource();
        BattleTask = null;
    }
}
