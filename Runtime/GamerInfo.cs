using System.Net.Sockets;

public class LogicData
{
    public uint ID;
    public uint RoomId;
    public NetworkStream NetworkStream;
}

public class BattleData
{
    public int Pos;
    public int ConnectionId;
    public byte[] Frames;
    public uint LastSentFrame;
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
        BattleData = new BattleData() { Frames = new byte[10000] };
    }
}