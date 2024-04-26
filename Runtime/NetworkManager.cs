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

    public override void Initialize()
    {
        memoryStream = new MemoryStream();
        kcpServerTransport = new KcpServerTransport(KcpUtil.defaultConfig, NetSetting.KcpPort)
        {
            onConnected = OnKcpConnected,
            onDataReceived = OnKcpDataReceived,
            onDisconnected = OnKcpDisconnected,
            onError = OnKcpError
        };
        tcpServerTransport = new TcpServerTransport(NetSetting.NetAddress, NetSetting.TcpPort)
        {
            onDataReceived = OnTcpDataReceived
        };
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
                    if (!room.Readies.Contains(c2SMessage.PlayerId)) room.Readies.Add(c2SMessage.PlayerId);
                    foreach (var playerId in room.Readies)
                    {
                        var gamer = GameManager.Instance.GetGamerById(playerId);
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

                    var dataFrame = c2SMessage.Datum.ToByteArray()[0];
                    var pos = (byte)(dataFrame & 0x01);
                    var gamer = GameManager.Instance.GetGamerByPos(pos);
                    var room = GameManager.Instance.GetRoom(gamer.LogicData.RoomId);
                    System.Console.WriteLine($"[KCP] BattleMsgFrame -> connectionId:{connectionId} gameId:{gamer.LogicData.ID} clientFrame:{c2SMessage.Frame} data:{dataFrame} serverFrame:{room.AuthoritativeFrame}");

                    room.InputCounts[c2SMessage.Frame] |= (byte)(1 << pos);
                    gamer.BattleData.LastSentFrame = c2SMessage.Frame;
                    gamer.BattleData.Frames[c2SMessage.Frame] = dataFrame;
                    break;
                }
            }
        }, kcpServerTransport.Shutdown);
    }

    private void OnKcpDisconnected(int connectionId)
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

    private void SendBattleConnectMessage(int connectionId, pb.BattleErrorCode errorCode)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ConnectMsg>();
        s2CMessage.ErrorCode = errorCode;
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgConnect, s2CMessage, connectionId);
    }

    private void SendBattleReadyMessage(int connectionId, pb.BattleErrorCode errorCode, uint roomId, List<uint> readies)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ReadyMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.Status.Clear();
        s2CMessage.Status.AddRange(readies);
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgReady, s2CMessage, connectionId);
    }

    private void SendBattleStartMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, ulong timestamp)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_StartMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        s2CMessage.TimeStamp = timestamp;
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgStart, s2CMessage, connectionId);
    }

    private void SendBattleHeartbeatMessage(int connectionId, pb.BattleErrorCode errorCode, ulong timestamp)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_HeartbeatMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.TimeStamp = timestamp;
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgHeartbeat, s2CMessage, connectionId);
    }

    private void SendBattleFrameMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, uint playerCount, uint inputCount, byte[] datum)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_FrameMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        s2CMessage.PlayerCount = playerCount;
        s2CMessage.InputCount = inputCount;
        s2CMessage.Datum = ByteString.CopyFrom(datum);
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

    private void SendLogicLoginMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint playerId)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_LoginMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.PlayerId = playerId;
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgLogin, s2CMessage, stream);
    }

    private void SendLogicCreateRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_CreateRoomMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgCreateRoom, s2CMessage, stream);
    }

    private void SendLogicJoinRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId, List<uint> all)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_JoinRoomMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.All.Clear();
        s2CMessage.All.AddRange(all);
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgJoinRoom, s2CMessage, stream);
    }

    private void OnServerBattleStart(RoomInfo room)
    {
        room.BattleStopwatch.Start();
        for (var i = 0; i < room.Gamers.Count; i++)
        {
            var gamer = GameManager.Instance.GetGamerById(room.Gamers[i]);
            SendBattleStartMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, (uint)room.AuthoritativeFrame, (ulong)room.BattleStopwatch.ElapsedMilliseconds);
        }
        var byteArray = new byte[room.Gamers.Count];
        Task.Run(async () =>{
            while (true) {
                try
                {
                    if (room.AuthoritativeFrame >= 0)
                    {
                        if (room.AuthoritativeFrame == 0) room.InputCounts[(uint)room.AuthoritativeFrame] = 0x3;
                        for (var i = 0; i < room.Gamers.Count; i++)
                        {
                            var gamer = GameManager.Instance.GetGamerById(room.Gamers[i]);
                            byteArray[gamer.BattleData.Pos] = (byte)((gamer.BattleData.Frames[room.AuthoritativeFrame] & ~0x01) | (byte)gamer.BattleData.Pos);
                        }
                        for (var i = 0; i < room.Gamers.Count; i++)
                        {
                            var gamer = GameManager.Instance.GetGamerById(room.Gamers[i]);
                            SendBattleFrameMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, (uint)room.AuthoritativeFrame, (uint)room.Gamers.Count, room.InputCounts[(uint)room.AuthoritativeFrame], byteArray);
                        }
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