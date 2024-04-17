using System.Diagnostics;
using System.Net.Sockets;
using Google.Protobuf;

public class NetworkManager : AManager<NetworkManager>
{
    private KcpServerTransport kcpServerTransport;
    private TcpServerTransport tcpServerTransport;
    private MemoryStream memoryStream;

    public bool KcpActive => kcpServerTransport.Active();
    public bool TcpActive => tcpServerTransport.Active();

    public Uri KcpUri => kcpServerTransport.Uri();
    public Uri TcpUri => tcpServerTransport.Uri();

    List<uint> readies = new List<uint>();

    public override void Initialize()
    {
        memoryStream = new MemoryStream();
        kcpServerTransport = new KcpServerTransport(KcpUtil.defaultConfig, NetSetting.KcpPort);
        kcpServerTransport.onConnected = OnKcpConnected;
        kcpServerTransport.onDataReceived = OnKcpDataReceived;
        kcpServerTransport.onDisconnected = OnkcpDisconnected;
        kcpServerTransport.onError = OnKcpError;
        tcpServerTransport = new TcpServerTransport(NetSetting.NetAddress, NetSetting.TcpPort);
        tcpServerTransport.onDataReceived = OnTcpDataReceived;
    }

    private void OnKcpConnected(int connectionId)
    {
        System.Console.WriteLine($"onConnected connectionId: {connectionId}");
    }

    private void OnKcpDataReceived(int connectionId, ArraySegment<byte> data, kcp2k.KcpChannel channel)
    {
        System.Console.WriteLine($"onDataReceived connectionId: {connectionId} data.len: {data.Count} channel: {channel}");
        if (data.Array == null) {
            System.Console.WriteLine($"kcpServerTransport.onDataReceived data.Array == null");
            return;
        }
        kcpServerTransport.OnMessageProcess(data.ToArray(), memoryStream, cmd => {
            System.Console.WriteLine($"[KCP] OnMessageProcess -> Cmd:{cmd} Length:{data.Count} Channel:{Enum.GetName(typeof(kcp2k.KcpChannel), channel)}");
            switch (cmd) 
            {
                case (byte)pb.BattleMsgID.BattleMsgConnect:
                {
                    var c2SMessage = pb.C2S_ConnectMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[KCP] BattleMsgConnect -> playerId:{c2SMessage.PlayerId} seasonId:{c2SMessage.SeasonId}");

                    foreach (var g in GameManager.Instance.GamerInfos) if(g.LogicData.ID == c2SMessage.PlayerId) g.BattleData.ConnectionId = connectionId;
                    SendBattleConnectMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgReady:
                {
                    var c2SMessage = pb.C2S_ReadyMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[KCP] BattleMsgReady -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId}");

                    var room = GameManager.Instance.GetRoom(c2SMessage.RoomId);
                    if (room == null) break;
                    if (!room.Readies.Contains(c2SMessage.PlayerId)) room.Readies.Add(c2SMessage.PlayerId);
                    for (var i = 0; i < room.Readies.Count; i++){
                        var gamer = GameManager.Instance.GetGamerById(room.Readies[i]);
                        gamer.BattleData.Pos = (int)(gamer.LogicData.ID - GameSetting.DefaultPlayerIdBase - 1);
                        SendBattleReadyMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, room.RoomId, room.Readies);
                    }
                    if (room.Readies.Count != GameSetting.RoomMaxPlayerCount) break;
                    OnServerBattleStart(room);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgHeartbeat:
                {
                    var c2SMessage = pb.C2S_HeartbeatMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[KCP] BattleMsgHeartbeat -> playerId:{c2SMessage.PlayerId} timestamp:{c2SMessage.TimeStamp}");

                    SendBattleHeartbeatMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk, c2SMessage.TimeStamp);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgFrame:
                {
                    var c2SMessage = pb.C2S_FrameMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[KCP] BattleMsgFrame -> connectionId:{connectionId} frame:{c2SMessage.Frame} Datum:{c2SMessage.Datum.ToByteArray()[0]}");

                    var data = c2SMessage.Datum.ToByteArray()[0];
                    var pos = (byte)(data & 0x01);
                    var gamer = GameManager.Instance.GetGamerByPos(pos);
                    if (gamer == null) {
                        System.Console.WriteLine($"[KCP] BattleMsgFrame -> Pos:{pos} Not Found!");
                        break;
                    }
                    var room = GameManager.Instance.GetRoom(gamer.LogicData.RoomId);
                    System.Console.WriteLine($"[KCP] BattleMsgFrame -> connectionId:{connectionId} gameId:{gamer.LogicData.ID} clientFrame:{c2SMessage.Frame} data:{data} serverFrame:{room.AuthoritativeFrame}");
                    
                    gamer.BattleData.LastSentFrame = c2SMessage.Frame;
                    gamer.BattleData.Frames[c2SMessage.Frame] = data;
                    break;
                }
            }
        }, kcpServerTransport.Shutdown);
    }

    private void OnkcpDisconnected(int connectionId)
    {
        System.Console.WriteLine($"onDisconnected connectionId: {connectionId}");
    }

    private void OnKcpError(int connectionId, kcp2k.ErrorCode errorCode, string error)
    {
        System.Console.WriteLine($"onError connectionId: {connectionId} errorCode: {errorCode} error: {error}");
    }

    public void KcpStart()
    {
        kcpServerTransport.Start();
    }

    public void KcpUpdate()
    {
        kcpServerTransport.Update();
    }

    public void KcpShutdown()
    {
        kcpServerTransport.Shutdown();
    }

    public void SendBattleConnectMessage(int connectionId, pb.BattleErrorCode errorCode)
    {
        var s2CMessage = new pb.S2C_ConnectMsg() { ErrorCode = errorCode };
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgConnect, s2CMessage, connectionId);
    }

    public void SendBattleReadyMessage(int connectionId, pb.BattleErrorCode errorCode, uint roomId, List<uint> readies)
    {
        var s2CMessage = new pb.S2C_ReadyMsg() { ErrorCode = errorCode, RoomId = roomId };
        s2CMessage.Status.AddRange(readies);
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgReady, s2CMessage, connectionId);
    }

    public void SendBattleStartMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, ulong timestamp)
    {
        var s2CMessage = new pb.S2C_StartMsg() { ErrorCode = errorCode, Frame = frame, TimeStamp = timestamp };
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgStart, s2CMessage, connectionId);
    }

    public void SendBattleHeartbeatMessage(int connectionId, pb.BattleErrorCode errorCode, ulong timestamp)
    {
        var s2CMessage = new pb.S2C_HeartbeatMsg() { ErrorCode = errorCode, TimeStamp = timestamp };
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgHeartbeat, s2CMessage, connectionId);
    }

    public void SendBattleFrameMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, uint playerCount, byte[] datum)
    {
        var s2CMessage = new pb.S2C_FrameMsg() { ErrorCode = errorCode, Frame = frame, PlayerCount = playerCount, Datum = ByteString.CopyFrom(datum) };
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgFrame, s2CMessage, connectionId);
    }

    private void OnTcpDataReceived(byte[] data, int read, NetworkStream stream)
    {
        tcpServerTransport.OnMessageProcess(data, memoryStream, cmd => {
            System.Console.WriteLine($"OnTcpDataReceived data.len:{data.Length} read:{read}");
            switch (cmd)
            {
                case (byte)pb.LogicMsgID.LogicMsgLogin:
                {
                    var c2SMessage = pb.C2S_LoginMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[TCP] LogicMsgLogin -> account:{c2SMessage.Account.ToStringUtf8()} password:{c2SMessage.Password.ToStringUtf8()}");
                    var gamer = GameManager.Instance.CreateGamer(c2SMessage.Account.ToStringUtf8(), c2SMessage.Password.ToStringUtf8());
                    gamer.LogicData.NetworkStream = stream;
                    SendLogicLoginMessage(stream, pb.LogicErrorCode.LogicErrOk, gamer.LogicData.ID);
                    break;
                }
                case (byte)pb.LogicMsgID.LogicMsgCreateRoom:
                {
                    var c2SMessage = pb.C2S_CreateRoomMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[TCP] LogicMsgCreateRoom -> playerId:{c2SMessage.PlayerId}");
                    var room = GameManager.Instance.CreateRoom();
                    foreach (var g in GameManager.Instance.GamerInfos)
                        SendLogicCreateRoomMessage(g.LogicData.NetworkStream, pb.LogicErrorCode.LogicErrOk, room.RoomId);
                    break;
                }
                case (byte)pb.LogicMsgID.LogicMsgJoinRoom:
                {
                    var c2SMessage = pb.C2S_JoinRoomMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[TCP] LogicMsgJoinRoom -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId}");
                    
                    var room = GameManager.Instance.GetRoom(c2SMessage.RoomId);
                    var gamer = GameManager.Instance.GetGamerById(c2SMessage.PlayerId);
                    if (room == null || gamer == null) break;
                    gamer.LogicData.RoomId = c2SMessage.RoomId;
                    if(!room.Gamers.Contains(gamer.LogicData.ID)) room.Gamers.Add(gamer.LogicData.ID);

                    if(room.Gamers.Count == GameSetting.RoomMaxPlayerCount && !KcpActive)
                    {
                        KcpStart();
                        KcpUpdate();
                    }

                    foreach (var g in GameManager.Instance.GamerInfos)
                        SendLogicJoinRoomMessage(g.LogicData.NetworkStream, pb.LogicErrorCode.LogicErrOk, room.RoomId, room.Gamers);
                    break;
                }
            }
        }, tcpServerTransport.Shutdown);
    }

    public void TcpStart()
    {
        tcpServerTransport.Start();
    }

    public void TcpShutdown()
    {
        tcpServerTransport.Shutdown();
    }

    public void SendLogicLoginMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint playerId)
    {
        var s2CMessage = new pb.S2C_LoginMsg() { ErrorCode = errorCode, PlayerId = playerId };
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgLogin, s2CMessage, stream);
    }

    public void SendLogicCreateRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId)
    {
        var s2CMessage = new pb.S2C_CreateRoomMsg() { ErrorCode = errorCode, RoomId = roomId };
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgCreateRoom, s2CMessage, stream);
    }

    public void SendLogicJoinRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId, List<uint> all)
    {
        var s2CMessage = new pb.S2C_JoinRoomMsg() { ErrorCode = errorCode, RoomId = roomId };
        s2CMessage.All.AddRange(all);
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgJoinRoom, s2CMessage, stream);
    }

    private void OnServerBattleStart(RoomInfo room)
    {
        room.BattleStopwatch.Start();
        for (var i = 0; i < room.Gamers.Count; i++){
            var gamer = GameManager.Instance.GetGamerById(room.Gamers[i]);
            SendBattleStartMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, (uint)room.AuthoritativeFrame, (ulong)room.BattleStopwatch.ElapsedMilliseconds);
        }
        var byteArray = new byte[room.Gamers.Count];
        Task.Run(async () =>{
            while (true) {
                try
                {
                    System.Console.WriteLine($"OnServerBattleStart AuthoritativeFrame -> {room.AuthoritativeFrame}");
                    for (var i = 0; i < room.Gamers.Count; i++)
                    {
                        var gamer = GameManager.Instance.GetGamerById(room.Gamers[i]);
                        byteArray[i] = (byte)((gamer.BattleData.Frames[room.AuthoritativeFrame] & ~0x01) | (byte)gamer.BattleData.Pos);
                    }
                    for (var i = 0; i < room.Gamers.Count; i++)
                    {
                        var gamer = GameManager.Instance.GetGamerById(room.Gamers[i]);
                        SendBattleFrameMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, (uint)room.AuthoritativeFrame, (uint)room.Gamers.Count, byteArray);
                    }
                    room.AuthoritativeFrame ++;
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"OnServerBattleStart BattleServerUpdate Exception ->\n{ex.Message}\n{ex.StackTrace}");
                }
                await Task.Delay(BattleSetting.BattleInterval);
            }
        });
    }

}