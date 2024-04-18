using System.Diagnostics;

public class RoomInfo
{
    public uint RoomId;
    public int AuthoritativeFrame;
    public List<uint> Readies;
    public List<uint> Gamers;
    public Stopwatch BattleStopwatch;

    public RoomInfo()
    {
        AuthoritativeFrame = -1;
        Readies = new List<uint>();
        Gamers = new List<uint>();
        BattleStopwatch = new Stopwatch();
    }
}