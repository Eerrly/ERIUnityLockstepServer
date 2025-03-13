using System.Net.Sockets;
using Google.Protobuf;

/// <summary>
/// 网络管理器
/// </summary>
public class NetworkManager : AManager<NetworkManager>
{
    /// <summary>
    /// KCP服务器对象
    /// </summary>
    private KcpServerTransport _kcpServerTransport;
    /// <summary>
    /// TCP服务器对象
    /// </summary>
    private TcpServerTransport _tcpServerTransport;
    /// <summary>
    /// TCP服务器以及KCP服务器的数据流对象
    /// </summary>
    private MemoryStream _memoryStream;
    
    /// <summary>
    /// KCP服务器是否处于存活状态
    /// </summary>
    public bool KcpActive => _kcpServerTransport.Active();
    /// <summary>
    /// TCP服务器是否处于存活状态
    /// </summary>
    public bool TcpActive => _tcpServerTransport.Active();
    
    /// <summary>
    /// KCP服务器的地址信息
    /// </summary>
    public Uri KcpUri => _kcpServerTransport.Uri();
    /// <summary>
    /// TCP服务器的地址信息
    /// </summary>
    public Uri TcpUri => _tcpServerTransport.Uri();
    
    /// <summary>
    /// 服务器初始化
    /// </summary>
    public override void Initialize(params object[] objs)
    {
        _memoryStream = new MemoryStream();
        _kcpServerTransport = new KcpServerTransport(KcpUtil.DefaultConfig, NetSetting.KcpPort)
        {
            OnConnected = OnKcpConnected,
            OnDataReceived = OnKcpDataReceived,
            OnDisconnected = OnKcpDisconnected,
            OnError = OnKcpError
        };
        _tcpServerTransport = new TcpServerTransport(NetSetting.NetAddress, NetSetting.TcpPort)
        {
            OnDataReceived = OnTcpDataReceived
        };
    }

    /// <summary>
    /// 客户端KCP连接回调
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    private void OnKcpConnected(int connectionId)
    {
        LogManager.Instance.Log(LogType.Info,$"OnKcpConnected connectionId: {connectionId}");
    }

    /// <summary>
    /// 收到客户端KCP消息时的回调处理函数
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="data">字节数据数组</param>
    /// <param name="channel">KCP消息类型</param>
    private void OnKcpDataReceived(int connectionId, ArraySegment<byte> data, kcp2k.KcpChannel channel)
    {
        if (!KcpActive)
        {
            LogManager.Instance.Log(LogType.Warning,$"Kcp Not Active!");
            return;
        }
        LogManager.Instance.Log(LogType.Info,$"OnKcpDataReceived connectionId: {connectionId} data.len: {data.Count} channel: {channel}");
        if (data.Array == null) {
            LogManager.Instance.Log(LogType.Warning,$"OnKcpDataReceived data.Array == null");
            return;
        }

        var gameManager = GameManager.Instance;
        _kcpServerTransport.OnMessageProcess(data.ToArray(), _memoryStream, cmd => {
            LogManager.Instance.Log(LogType.Info,$"KCP OnMessageProcess -> Cmd:{cmd} Length:{data.Count} Channel:{Enum.GetName(typeof(kcp2k.KcpChannel), channel)}");
            switch (cmd) 
            {
                case (byte)pb.BattleMsgID.BattleMsgConnect:
                {
                    var c2SMessage = pb.C2S_ConnectMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info,$"BattleMsgConnect -> playerId:{c2SMessage.PlayerId} seasonId:{c2SMessage.SeasonId}");
                    foreach (var kv in gameManager.GamerInfoDic) 
                        if(kv.Value.LogicData.ID == c2SMessage.PlayerId) gameManager.UpdateGamerConnectionId(c2SMessage.PlayerId, connectionId);
                    SendBattleConnectMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgReady:
                {
                    var c2SMessage = pb.C2S_ReadyMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info,$"BattleMsgReady -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId}");

                    var room = gameManager.GetRoom(c2SMessage.RoomId);
                    if (!room.Readies.Contains(c2SMessage.PlayerId)) room.Readies.Add(c2SMessage.PlayerId);
                    foreach (var playerId in room.Readies)
                    {
                        var gamer = gameManager.GetGamerById(playerId);
                        gameManager.UpdateGamerPos(playerId, (int)(gamer.LogicData.ID - GameSetting.DefaultPlayerIdBase - 1));
                        SendBattleReadyMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, room.RoomId, room.Readies);
                    }
                    // 人数满了开启战斗
                    if (room.Readies.Count != GameSetting.RoomMaxPlayerCount) break;
                    
                    OnServerBattleStart(room);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgHeartbeat:
                {
                    var c2SMessage = pb.C2S_HeartbeatMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info,$"BattleMsgHeartbeat -> playerId:{c2SMessage.PlayerId} timestamp:{c2SMessage.TimeStamp}");

                    SendBattleHeartbeatMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk, c2SMessage.TimeStamp);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgFrame:
                {
                    var c2SMessage = pb.C2S_FrameMsg.Parser.ParseFrom(_memoryStream);
                    
                    // 直接访问字节数组并位运算提取战斗POS，byteString通过索引可以get，但没办法set
                    var dataFrame = c2SMessage.Datum[0];
                    var pos = (byte)(dataFrame & 0x01);
                    
                    var gamer = gameManager.GetGamerByPos(pos);
                    var room = gameManager.GetRoom(gamer.LogicData.RoomId);
                    LogManager.Instance.Log(LogType.Info,$"BattleMsgFrame -> connectionId:{connectionId} gameId:{gamer.LogicData.ID} clientFrame:{c2SMessage.Frame} data:{dataFrame} serverFrame:{room.AuthoritativeFrame}");
                    
                    // 使用位运算更新房间的输入计数 [0 0] 2bit，索引分别代表玩家0玩家1，0:未操作 1:操作
                    room.InputCounts[c2SMessage.Frame] |= (byte)(1 << pos);
                    gamer.BattleData.Frames[c2SMessage.Frame] = dataFrame;
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgCheck:
                {
                    var c2SMessage = pb.C2S_CheckMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info,$"BattleMsgCheck -> frame:{c2SMessage.Frame} pos:{c2SMessage.Pos} md5:{c2SMessage.Md5}");
                    
                    var gamer = gameManager.GetGamerByPos(c2SMessage.Pos);
                    var room = gameManager.GetRoom(gamer.LogicData.RoomId);
                    if (!room.BattleCheckMap.ContainsKey(c2SMessage.Frame))
                        room.BattleCheckMap[c2SMessage.Frame] = new List<int>(GameSetting.RoomMaxPlayerCount);
                    room.BattleCheckMap[c2SMessage.Frame].Add(c2SMessage.Md5);

                    // 当校验数据收集全后，广播给所有玩家
                    if (room.BattleCheckMap[c2SMessage.Frame].Count() == GameSetting.RoomMaxPlayerCount)
                    {
                        var errorCode = room.BattleCheckMap[c2SMessage.Frame].Distinct().Count() == 1 ? pb.BattleErrorCode.BattleErrBattleOk : pb.BattleErrorCode.BattleErrDiff;
                        foreach (var gamerId in room.Gamers)
                        {
                            gamer = gameManager.GetGamerById(gamerId);
                            SendBattleCheckMessage(gamer.BattleData.ConnectionId, errorCode, c2SMessage.Frame);
                        }
                    }
                    break;
                }
            }
        }, _kcpServerTransport.Shutdown);
    }

    /// <summary>
    /// KCP服务器发送客户端断开连接时回调
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    private void OnKcpDisconnected(int connectionId)
    {
        LogManager.Instance.Log(LogType.Error,$"OnKcpDisconnected connectionId: {connectionId}");
        var gamer = GameManager.Instance.GetGamerByConnectionId(connectionId);
        var room = GameManager.Instance.GetRoom(gamer.LogicData.RoomId);
        room.Readies.Remove(gamer.LogicData.ID);
    }

    /// <summary>
    /// KCP服务器发送错误时回调
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="error">错误原因</param>
    private void OnKcpError(int connectionId, kcp2k.ErrorCode errorCode, string error)
    {
        LogManager.Instance.Log(LogType.Error, $"OnKcpError connectionId: {connectionId} errorCode: {errorCode} error: {error}");
    }

    /// <summary>
    /// 开启KCP服务器
    /// </summary>
    public void KcpStart()
    {
        _kcpServerTransport.Start();
    }

    /// <summary>
    /// KCP服务器轮询
    /// </summary>
    public void KcpUpdate()
    {
        _kcpServerTransport.Update();
    }

    /// <summary>
    /// 关闭KCP服务器
    /// </summary>
    public void KcpShutdown()
    {
        _kcpServerTransport.Shutdown();
    }

    /// <summary>
    /// 回复客户端战斗连接消息
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="errorCode">错误码</param>
    private void SendBattleConnectMessage(int connectionId, pb.BattleErrorCode errorCode)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ConnectMsg>();
        s2CMessage.ErrorCode = errorCode;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgConnect, s2CMessage, connectionId);
    }

    /// <summary>
    /// 回复客户端战斗准备消息
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="roomId">房间ID</param>
    /// <param name="readies">房间内所有已准备的玩家ID</param>
    private void SendBattleReadyMessage(int connectionId, pb.BattleErrorCode errorCode, uint roomId, List<uint> readies)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ReadyMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.Status.Clear();
        s2CMessage.Status.AddRange(readies);
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgReady, s2CMessage, connectionId);
    }

    /// <summary>
    /// 回复客户端战斗开始消息
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="frame">帧号</param>
    /// <param name="timestamp">时间戳</param>
    private void SendBattleStartMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, ulong timestamp)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_StartMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        s2CMessage.TimeStamp = timestamp;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgStart, s2CMessage, connectionId);
    }

    /// <summary>
    /// 回复客户端战斗心跳消息
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="timestamp">时间戳</param>
    private void SendBattleHeartbeatMessage(int connectionId, pb.BattleErrorCode errorCode, ulong timestamp)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_HeartbeatMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.TimeStamp = timestamp;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgHeartbeat, s2CMessage, connectionId);
    }

    /// <summary>
    /// 回复客户端战斗帧数据消息
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="frame">帧号</param>
    /// <param name="playerCount">玩家数量</param>
    /// <param name="inputCount">操作数量</param>
    /// <param name="datum">玩家帧数据</param>
    private void SendBattleFrameMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, uint playerCount, uint inputCount, byte[] datum)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_FrameMsg>(true);
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        s2CMessage.PlayerCount = playerCount;
        s2CMessage.InputCount = inputCount;
        // .proto bytes类型 的必要转换
        s2CMessage.Datum = ByteString.CopyFrom(datum);
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgFrame, s2CMessage, connectionId);
    }

    /// <summary>
    /// 回复客户端战斗校验消息
    /// </summary>
    /// <param name="connectionId">客户端KCP连接ID</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="frame">帧号</param>
    private void SendBattleCheckMessage(int connectionId, pb.BattleErrorCode errorCode, int frame)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_CheckMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgCheck, s2CMessage, connectionId);
    }

    /// <summary>
    /// 收到客户端TCP消息时的回调处理函数
    /// </summary>
    /// <param name="data">字节数据数组</param>
    /// <param name="read">已读</param>
    /// <param name="stream"></param>
    private void OnTcpDataReceived(byte[] data, int read, NetworkStream stream)
    {
        if (!TcpActive)
        {
            LogManager.Instance.Log(LogType.Warning, $"Tcp Not Active!");
            return;
        }

        var gameManager = GameManager.Instance;
        _tcpServerTransport.OnMessageProcess(data, _memoryStream, cmd => {
            LogManager.Instance.Log(LogType.Info, $"OnTcpDataReceived data.len:{data.Length} read:{read}");
            switch (cmd)
            {
                case (byte)pb.LogicMsgID.LogicMsgLogin:
                {
                    var c2SMessage = pb.C2S_LoginMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"LogicMsgLogin -> account:{c2SMessage.Account.ToStringUtf8()} password:{c2SMessage.Password.ToStringUtf8()}");
                    var gamer = gameManager.GetOrCreateGamer(c2SMessage.Account.ToStringUtf8(), c2SMessage.Password.ToStringUtf8());
                    gamer.LogicData.NetworkStream = stream;
                    SendLogicLoginMessage(stream, pb.LogicErrorCode.LogicErrOk, gamer.LogicData.ID);
                    break;
                }
                case (byte)pb.LogicMsgID.LogicMsgCreateRoom:
                {
                    var c2SMessage = pb.C2S_CreateRoomMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info,$"LogicMsgCreateRoom -> playerId:{c2SMessage.PlayerId}");
                    var room = gameManager.CreateRoom();
                    // 某一个客户端创建房间时，广播给每一个客户端
                    foreach (var kv in gameManager.GamerInfoDic)
                        SendLogicCreateRoomMessage(kv.Value.LogicData.NetworkStream, pb.LogicErrorCode.LogicErrOk, room.RoomId);
                    break;
                }
                case (byte)pb.LogicMsgID.LogicMsgJoinRoom:
                {
                    var c2SMessage = pb.C2S_JoinRoomMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info,$"LogicMsgJoinRoom -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId}");
                    
                    var room = gameManager.GetRoom(c2SMessage.RoomId);
                    var gamer = gameManager.GetGamerById(c2SMessage.PlayerId);
                    gamer.LogicData.RoomId = c2SMessage.RoomId;
                    if(!room.Gamers.Contains(gamer.LogicData.ID)) room.Gamers.Add(gamer.LogicData.ID);

                    // 当房间人数到了可以战斗开启的人数时，开启KCP服务器并且开始轮询
                    if(room.Gamers.Count == GameSetting.RoomMaxPlayerCount && !KcpActive)
                    {
                        KcpStart();
                        KcpUpdate();
                    }
                    // 某一个客户端加入房间时，广播给每一个客户端
                    foreach (var kv in gameManager.GamerInfoDic)
                        SendLogicJoinRoomMessage(kv.Value.LogicData.NetworkStream, pb.LogicErrorCode.LogicErrOk, room.RoomId, room.Gamers);
                    break;
                }
            }
        }, _tcpServerTransport.Shutdown);
    }

    /// <summary>
    /// 开启TCP服务器
    /// </summary>
    public void TcpStart()
    {
        _tcpServerTransport.Start();
    }

    /// <summary>
    /// 关闭TCP服务器
    /// </summary>
    public void TcpShutdown()
    {
        _tcpServerTransport.Shutdown();
    }

    /// <summary>
    /// 回复玩家登录消息
    /// </summary>
    /// <param name="stream">客户端流</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="playerId">玩家ID</param>
    private void SendLogicLoginMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint playerId)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_LoginMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.PlayerId = playerId;
        _tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgLogin, s2CMessage, stream);
    }

    /// <summary>
    /// 回复玩家创建房间消息
    /// </summary>
    /// <param name="stream">客户端流</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="roomId">房间ID</param>
    private void SendLogicCreateRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_CreateRoomMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        _tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgCreateRoom, s2CMessage, stream);
    }

    /// <summary>
    /// 回复玩家加入房间消息
    /// </summary>
    /// <param name="stream">客户端流</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="roomId">房间ID</param>
    /// <param name="all">房间内所有玩家的ID</param>
    private void SendLogicJoinRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId, List<uint> all)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_JoinRoomMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.All.Clear();
        s2CMessage.All.AddRange(all);
        _tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgJoinRoom, s2CMessage, stream);
    }

    /// <summary>
    /// 服务器战斗帧轮询
    /// </summary>
    /// <param name="room">房间对象</param>
    private void OnServerBattleStart(RoomInfo room)
    {
        var gameManager = GameManager.Instance;
        room.BattleStopwatch.Start();
        for (var i = 0; i < room.Gamers.Count; i++)
        {
            var gamer = gameManager.GetGamerById(room.Gamers[i]);
            SendBattleStartMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, (uint)room.AuthoritativeFrame, (ulong)room.BattleStopwatch.ElapsedMilliseconds);
        }
        var byteArray = new byte[room.Gamers.Count];
        Task.Run(async () =>{
            while (true) {
                try
                {
                    if (room.AuthoritativeFrame >= 0)
                    {
                        // 第0帧，强行将玩家标记为已操作，必要！目的是为了让客户端那边有第0帧的操作，这样子玩家可能因为前面几帧还卡着也能当作没动来看待
                        if (room.AuthoritativeFrame == 0) room.InputCounts[(uint)room.AuthoritativeFrame] = 0x3;
                        // 赋值操作数据，默认为0
                        for (var i = 0; i < room.Gamers.Count; i++)
                        {
                            var gamer = gameManager.GetGamerById(room.Gamers[i]);
                            byteArray[gamer.BattleData.Pos] = (byte)((gamer.BattleData.Frames[room.AuthoritativeFrame] & ~0x01) | (byte)gamer.BattleData.Pos);
                        }
                        // 将所有玩家的操作数据，广播给每一个准备了的客户端
                        for (var i = 0; i < room.Gamers.Count; i++)
                        {
                            var gamer = gameManager.GetGamerById(room.Gamers[i]);
                            if (!room.Readies.Contains(gamer.LogicData.ID)) 
                                continue;
                            SendBattleFrameMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, (uint)room.AuthoritativeFrame, (uint)room.Gamers.Count, room.InputCounts[(uint)room.AuthoritativeFrame], byteArray);
                        }
                    }
                    room.AuthoritativeFrame ++;
                }
                catch (Exception ex)
                {
                    LogManager.Instance.Log(LogType.Exception, $"OnServerBattleStart BattleServerUpdate Exception ->\n{ex.Message}\n{ex.StackTrace}");
                }
                await Task.Delay(BattleSetting.BattleInterval);
            }
        });
    }

}