using System.Net.Sockets;
using Google.Protobuf;

/// <summary>
/// 网络交互管理器
/// </summary>
public class NetworkManager : AManager<NetworkManager>
{
    /// <summary>
    /// KCP服务器
    /// </summary>
    private KcpServerTransport kcpServerTransport;
    /// <summary>
    /// TCP服务器
    /// </summary>
    private TcpServerTransport tcpServerTransport;
    /// <summary>
    /// 数据流
    /// </summary>
    private MemoryStream memoryStream;

    /// <summary>
    /// KCP服务器是否处于激活
    /// </summary>
    public bool KcpActive => kcpServerTransport.Active();
    /// <summary>
    /// TCP服务器是否处于激活
    /// </summary>
    public bool TcpActive => tcpServerTransport.Active();

    /// <summary>
    /// 获取KCP服务器URI
    /// </summary>
    public Uri KcpUri => kcpServerTransport.Uri();
    /// <summary>
    /// 获取TCP服务器URI
    /// </summary>
    public Uri TcpUri => tcpServerTransport.Uri();

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        memoryStream = new MemoryStream();
        kcpServerTransport = new KcpServerTransport(KcpUtil.defaultConfig, NetSetting.KcpPort)
        {
            OnConnected = OnKcpConnected,
            OnDataReceived = OnKcpDataReceived,
            OnDisconnected = OnKcpDisconnected,
            OnError = OnKcpError
        };
        tcpServerTransport = new TcpServerTransport(NetSetting.NetAddress, NetSetting.TcpPort)
        {
            onDataReceived = OnTcpDataReceived
        };
    }

    /// <summary>
    /// KCP客户端连接时
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    private void OnKcpConnected(int connectionId)
    {
        System.Console.WriteLine($"OnKcpConnected connectionId: {connectionId}");
    }

    /// <summary>
    /// KCP客户端发来消息时
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <param name="data">收到的数据</param>
    /// <param name="channel">消息类型</param>
    private void OnKcpDataReceived(int connectionId, ArraySegment<byte> data, kcp2k.KcpChannel channel)
    {
        System.Console.WriteLine($"[KCP] OnKcpDataReceived connectionId: {connectionId} data.len: {data.Count} channel: {channel}");
        if (data.Array == null) {
            System.Console.WriteLine($"[KCP] OnKcpDataReceived data.Array == null");
            return;
        }
        kcpServerTransport.OnMessageProcess(data.ToArray(), memoryStream, cmd => {
            System.Console.WriteLine($"[KCP] OnMessageProcess -> Cmd:{cmd} Length:{data.Count} Channel:{Enum.GetName(typeof(kcp2k.KcpChannel), channel)}");
            switch (cmd) 
            {
                // 连接
                case (byte)pb.BattleMsgID.BattleMsgConnect:
                {
                    var c2SMessage = pb.C2S_ConnectMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[KCP] BattleMsgConnect -> playerId:{c2SMessage.PlayerId} seasonId:{c2SMessage.SeasonId}");

                    foreach (var g in GameManager.Instance.GamerInfos) if(g.LogicData.ID == c2SMessage.PlayerId) g.BattleData.ConnectionId = connectionId;
                    SendBattleConnectMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk);
                    break;
                }
                // 准备
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
                    // 当房间准备人数大于设定上限，则开启服务器战斗
                    OnServerBattleStart(room);
                    break;
                }
                // 心跳
                case (byte)pb.BattleMsgID.BattleMsgHeartbeat:
                {
                    var c2SMessage = pb.C2S_HeartbeatMsg.Parser.ParseFrom(memoryStream);
                    System.Console.WriteLine($"[KCP] BattleMsgHeartbeat -> playerId:{c2SMessage.PlayerId} timestamp:{c2SMessage.TimeStamp}");

                    SendBattleHeartbeatMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk, c2SMessage.TimeStamp);
                    break;
                }
                // 帧数据
                case (byte)pb.BattleMsgID.BattleMsgFrame:
                {
                    var c2SMessage = pb.C2S_FrameMsg.Parser.ParseFrom(memoryStream);

                    var dataFrame = c2SMessage.Datum.ToByteArray()[0];
                    var pos = (byte)(dataFrame & 0x01);
                    var gamer = GameManager.Instance.GetGamerByPos(pos);
                    var room = GameManager.Instance.GetRoom(gamer.LogicData.RoomId);
                    System.Console.WriteLine($"[KCP] BattleMsgFrame -> connectionId:{connectionId} gameId:{gamer.LogicData.ID} clientFrame:{c2SMessage.Frame} data:{dataFrame} serverFrame:{room.AuthoritativeFrame}");

                    // 帧操作人数
                    room.InputCounts[c2SMessage.Frame] |= (byte)(1 << pos);
                    gamer.BattleData.Frames[c2SMessage.Frame] = dataFrame;
                    break;
                }
            }
        }, kcpServerTransport.Shutdown);
    }

    /// <summary>
    /// KCP客户端断开连接时
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    private void OnKcpDisconnected(int connectionId)
    {
        System.Console.WriteLine($"OnKcpDisconnected connectionId: {connectionId}");
    }

    /// <summary>
    /// KCP客户端发送错误时
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="error"></param>
    private void OnKcpError(int connectionId, kcp2k.ErrorCode errorCode, string error)
    {
        System.Console.WriteLine($"OnKcpError connectionId: {connectionId} errorCode: {errorCode} error: {error}");
    }

    /// <summary>
    /// KCP服务器开始
    /// </summary>
    public void KcpStart()
    {
        kcpServerTransport.Start();
    }

    /// <summary>
    /// KCP服务器轮询
    /// </summary>
    public void KcpUpdate()
    {
        kcpServerTransport.Update();
    }

    /// <summary>
    /// KCP服务器关闭
    /// </summary>
    public void KcpShutdown()
    {
        kcpServerTransport.Shutdown();
    }

    /// <summary>
    /// 发送战斗连接消息
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <param name="errorCode">错误码</param>
    private void SendBattleConnectMessage(int connectionId, pb.BattleErrorCode errorCode)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ConnectMsg>();
        s2CMessage.ErrorCode = errorCode;
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgConnect, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送战斗准备消息
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="roomId">房间ID</param>
    /// <param name="readies">已准备的客户端ID列表</param>
    private void SendBattleReadyMessage(int connectionId, pb.BattleErrorCode errorCode, uint roomId, List<uint> readies)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ReadyMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.Status.Clear();
        s2CMessage.Status.AddRange(readies);
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgReady, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送战斗开始消息
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="frame">帧号</param>
    /// <param name="timestamp">时间戳</param>
    private void SendBattleStartMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, ulong timestamp)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_StartMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        s2CMessage.TimeStamp = timestamp;
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgStart, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送战斗心跳消息
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="timestamp">时间戳</param>
    private void SendBattleHeartbeatMessage(int connectionId, pb.BattleErrorCode errorCode, ulong timestamp)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_HeartbeatMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.TimeStamp = timestamp;
        kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgHeartbeat, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送战斗帧数据消息
    /// </summary>
    /// <param name="connectionId">KCP客户端连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="frame">帧号</param>
    /// <param name="playerCount">玩家数量</param>
    /// <param name="inputCount">每帧操作人数</param>
    /// <param name="datum">帧数据</param>
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

    /// <summary>
    /// 收到TCP客户端发来消息
    /// </summary>
    /// <param name="data">数据字节数组</param>
    /// <param name="read">已读</param>
    /// <param name="stream">客户端数据流</param>
    private void OnTcpDataReceived(byte[] data, int read, NetworkStream stream)
    {
        tcpServerTransport.OnMessageProcess(data, memoryStream, cmd => {
            System.Console.WriteLine($"[TCP] OnTcpDataReceived data.len:{data.Length} read:{read}");
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

                    // 当房间人数达到设置上限时，则开启KCP服务器，为战斗准备
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

    /// <summary>
    /// TCP服务器开启
    /// </summary>
    public void TcpStart()
    {
        tcpServerTransport.Start();
    }

    /// <summary>
    /// TCP服务器关闭
    /// </summary>
    public void TcpShutdown()
    {
        tcpServerTransport.Shutdown();
    }

    /// <summary>
    /// 发送登录消息
    /// </summary>
    /// <param name="stream">TCP客户端流</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="playerId">玩家ID</param>
    private void SendLogicLoginMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint playerId)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_LoginMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.PlayerId = playerId;
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgLogin, s2CMessage, stream);
    }

    /// <summary>
    /// 发送创建房间消息
    /// </summary>
    /// <param name="stream">TCP客户端流</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="roomId">房间ID</param>
    private void SendLogicCreateRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_CreateRoomMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgCreateRoom, s2CMessage, stream);
    }

    /// <summary>
    /// 发送加入房间消息
    /// </summary>
    /// <param name="stream">TCP客户端流</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="roomId">房间ID</param>
    /// <param name="all">房间内的所有玩家ID集合</param>
    private void SendLogicJoinRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId, List<uint> all)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_JoinRoomMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.All.Clear();
        s2CMessage.All.AddRange(all);
        tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgJoinRoom, s2CMessage, stream);
    }

    /// <summary>
    /// 服务器战斗开始
    /// </summary>
    /// <param name="room">房间对象</param>
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
                        // 第0帧时，操作人数写死为2
                        if (room.AuthoritativeFrame == 0) room.InputCounts[(uint)room.AuthoritativeFrame] = 0x3;
                        // 附加帧数据（如果没有则为默认数据）以及玩家战斗位置
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