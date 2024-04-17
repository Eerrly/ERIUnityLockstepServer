public class GameManager : AManager<GameManager>
{
    public List<GamerInfo> GamerInfos;
    public List<RoomInfo> RoomInfos;

    public override void Initialize()
    {
        GamerInfos = new List<GamerInfo>();
        RoomInfos = new List<RoomInfo>();
    }

    public override void OnRelease()
    {
        GamerInfos.Clear();
        RoomInfos.Clear();
    }

    public GamerInfo CreateGamer(string account, string password)
    {
        var gamer = new GamerInfo(){ Account = account, Password = password };
        gamer.LogicData.ID = GameSetting.DefaultPlayerIdBase + (uint)GamerInfos.Count + 1;
        GamerInfos.Add(gamer);
        return gamer;
    }

    public GamerInfo GetGamerById(uint playerId)
    {
        foreach(var g in GamerInfos){
            if(g.LogicData.ID == playerId) return g;
        }
        return null;
    }

    public GamerInfo GetGamerByPos(int pos)
    {
        foreach(var g in GamerInfos){
            if(g.BattleData.Pos == pos) return g;
        }
        return null;
    }

    public RoomInfo CreateRoom()
    {
        var room = new RoomInfo() { RoomId = GameSetting.DefaultRoomIdBase + (uint)RoomInfos.Count + 1 };
        RoomInfos.Add(room);
        return room;
    }

    public RoomInfo GetRoom(uint roomId)
    {
        foreach (var r in RoomInfos){
            if(r.RoomId == roomId) return r;
        }
        return null;
    }
    
}