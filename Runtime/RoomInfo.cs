using System.Diagnostics;

public class RoomInfo
{
    public uint RoomId;
    
    public int AuthoritativeFrame;
    
    public List<uint> Readies;
    
    public List<uint> Gamers;
    
    public byte[] InputCounts;
    
    public Stopwatch BattleStopwatch;

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