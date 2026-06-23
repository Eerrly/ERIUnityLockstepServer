using System.Net.Sockets;
using Google.Protobuf;

/// <summary>
/// 网络管理器
/// </summary>
public class NetworkManager : AManager<NetworkManager>
{
    private const string BattleExitReasonPlayerExit = "PlayerExit";
    private const string BattleExitReasonDisconnected = "Disconnected";
    private const string BattleExitReasonServerError = "ServerError";

    /// <summary>
    /// KCP 服务端对象
    /// </summary>
    private KcpServerTransport _kcpServerTransport;

    /// <summary>
    /// TCP 服务端对象
    /// </summary>
    private TcpServerTransport _tcpServerTransport;

    /// <summary>
    /// 消息解析复用流
    /// </summary>
    private MemoryStream _memoryStream;

    /// <summary>
    /// KCP 服务是否激活
    /// </summary>
    public bool KcpActive => _kcpServerTransport.Active();

    /// <summary>
    /// TCP 服务是否激活
    /// </summary>
    public bool TcpActive => _tcpServerTransport.Active();

    /// <summary>
    /// KCP 服务地址
    /// </summary>
    public Uri KcpUri => _kcpServerTransport.Uri();

    /// <summary>
    /// TCP 服务地址
    /// </summary>
    public Uri TcpUri => _tcpServerTransport.Uri();

    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize(params object[] objs)
    {
        _memoryStream = new MemoryStream();
        _kcpServerTransport = new KcpServerTransport(KcpUtil.DefaultConfig, NetSetting.KcpPort)
        {
            OnConnected = OnKcpConnected,
            OnDataReceived = OnKcpDataReceived,
            OnDisconnected = OnKcpDisconnected,
            OnError = OnKcpError,
        };
        _tcpServerTransport = new TcpServerTransport(NetSetting.NetAddress, NetSetting.TcpPort)
        {
            OnDataReceived = OnTcpDataReceived,
        };
    }

    /// <summary>
    /// KCP 连接建立
    /// </summary>
    private void OnKcpConnected(int connectionId)
    {
        LogManager.Instance.Log(LogType.Info, $"OnKcpConnected connectionId:{connectionId}");
    }

    /// <summary>
    /// 收到 KCP 消息
    /// </summary>
    private void OnKcpDataReceived(int connectionId, ArraySegment<byte> data, kcp2k.KcpChannel channel)
    {
        if (!KcpActive)
        {
            LogManager.Instance.Log(LogType.Warning, "Kcp Not Active!");
            return;
        }

        LogManager.Instance.Log(LogType.Info, $"OnKcpDataReceived connectionId:{connectionId} data.len:{data.Count} channel:{channel}");
        if (data.Array == null)
        {
            LogManager.Instance.Log(LogType.Warning, "OnKcpDataReceived data.Array == null");
            return;
        }

        var gameManager = GameManager.Instance;
        _kcpServerTransport.OnMessageProcess(data.ToArray(), _memoryStream, cmd =>
        {
            LogManager.Instance.Log(LogType.Info, $"KCP OnMessageProcess -> Cmd:{cmd} Length:{data.Count} Channel:{Enum.GetName(typeof(kcp2k.KcpChannel), channel)}");
            switch (cmd)
            {
                case (byte)pb.BattleMsgID.BattleMsgConnect:
                {
                    var c2SMessage = pb.C2S_ConnectMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"BattleMsgConnect -> playerId:{c2SMessage.PlayerId} seasonId:{c2SMessage.SeasonId}");

                    if (!gameManager.TryGetGamerById(c2SMessage.PlayerId, out var gamer) || gamer == null)
                    {
                        SendBattleConnectMessage(connectionId, pb.BattleErrorCode.BattleErrData);
                        break;
                    }

                    gameManager.UpdateGamerConnectionId(c2SMessage.PlayerId, connectionId);
                    SendBattleConnectMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgReady:
                {
                    var c2SMessage = pb.C2S_ReadyMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"BattleMsgReady -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId}");

                    var room = gameManager.GetRoom(c2SMessage.RoomId);
                    if (room.IsBattleRunning || room.IsBattleExiting)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgReady ignored because battle is running or exiting -> roomId:{room.RoomId} playerId:{c2SMessage.PlayerId}");
                        break;
                    }

                    if (!room.Readies.Contains(c2SMessage.PlayerId))
                        room.Readies.Add(c2SMessage.PlayerId);

                    foreach (var playerId in room.Readies)
                    {
                        var gamer = gameManager.GetGamerById(playerId);
                        gameManager.UpdateGamerPos(playerId, (int)(gamer.LogicData.ID - GameSetting.DefaultPlayerIdBase - 1));
                        SendBattleReadyMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, room.RoomId, room.Readies);
                    }

                    if (room.Readies.Count != GameSetting.RoomMaxPlayerCount || room.IsBattleRunning || room.IsBattleExiting)
                        break;

                    OnServerBattleStart(room);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgHeartbeat:
                {
                    var c2SMessage = pb.C2S_HeartbeatMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"BattleMsgHeartbeat -> playerId:{c2SMessage.PlayerId} timestamp:{c2SMessage.TimeStamp}");
                    SendBattleHeartbeatMessage(connectionId, pb.BattleErrorCode.BattleErrBattleOk, c2SMessage.TimeStamp);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgFrame:
                {
                    var c2SMessage = pb.C2S_FrameMsg.Parser.ParseFrom(_memoryStream);

                    if (!gameManager.TryGetGamerByConnectionId(connectionId, out var connectionGamer) || connectionGamer == null)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because connection gamer not found -> connectionId:{connectionId} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (!gameManager.TryGetRoom(connectionGamer.LogicData.RoomId, out var room) || room == null)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because room not found -> connectionId:{connectionId} roomId:{connectionGamer.LogicData.RoomId} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (!room.IsBattleRunning || room.IsBattleExiting)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because battle is not running or exiting -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (connectionGamer.BattleData.ConnectionState != BattleConnectionState.Online)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because gamer is not online -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} state:{connectionGamer.BattleData.ConnectionState}");
                        break;
                    }

                    if (c2SMessage.Frame >= (uint)BattleSetting.MaxFrameCount)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because frame is invalid -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (c2SMessage.Datum.Length == 0)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because datum is empty -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} frame:{c2SMessage.Frame}");
                        break;
                    }

                    var dataFrame = c2SMessage.Datum[0];
                    var pos = (byte)(dataFrame & 0x01);
                    if (pos >= GameSetting.RoomMaxPlayerCount)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because pos is invalid -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} pos:{pos} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (pos != connectionGamer.BattleData.Pos)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored because pos does not match connection gamer -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} expectedPos:{connectionGamer.BattleData.Pos} actualPos:{pos} frame:{c2SMessage.Frame}");
                        break;
                    }

                    LogManager.Instance.Log(LogType.Info, $"BattleMsgFrame -> connectionId:{connectionId} gameId:{connectionGamer.LogicData.ID} clientFrame:{c2SMessage.Frame} data:{dataFrame} serverFrame:{room.AuthoritativeFrame}");
                    if (!room.IsBattleRunning || room.IsBattleExiting)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgFrame ignored before state write because battle is not running or exiting -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} frame:{c2SMessage.Frame}");
                        break;
                    }

                    room.InputCounts[c2SMessage.Frame] |= (byte)(1 << connectionGamer.BattleData.Pos);
                    connectionGamer.BattleData.Frames[c2SMessage.Frame] = dataFrame;
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgCheck:
                {
                    var c2SMessage = pb.C2S_CheckMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"BattleMsgCheck -> frame:{c2SMessage.Frame} pos:{c2SMessage.Pos} md5:{c2SMessage.Md5}");

                    if (!gameManager.TryGetGamerByConnectionId(connectionId, out var connectionGamer) || connectionGamer == null)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgCheck ignored because connection gamer not found -> connectionId:{connectionId} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (!gameManager.TryGetRoom(connectionGamer.LogicData.RoomId, out var room) || room == null)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgCheck ignored because room not found -> connectionId:{connectionId} roomId:{connectionGamer.LogicData.RoomId} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (!room.IsBattleRunning || room.IsBattleExiting)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgCheck ignored because battle is not running or exiting -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (connectionGamer.BattleData.ConnectionState != BattleConnectionState.Online)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgCheck ignored because gamer is not online -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} state:{connectionGamer.BattleData.ConnectionState}");
                        break;
                    }

                    if (c2SMessage.Frame < 0 || c2SMessage.Frame >= BattleSetting.MaxFrameCount)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgCheck ignored because frame is invalid -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (c2SMessage.Pos < 0 || c2SMessage.Pos >= GameSetting.RoomMaxPlayerCount)
                    {
                        LogManager.Instance.Log(LogType.Warning, $"BattleMsgCheck ignored because pos is invalid -> roomId:{room.RoomId} playerId:{connectionGamer.LogicData.ID} pos:{c2SMessage.Pos} frame:{c2SMessage.Frame}");
                        break;
                    }

                    if (!room.BattleCheckMap.ContainsKey(c2SMessage.Frame))
                        room.BattleCheckMap[c2SMessage.Frame] = new List<int>(GameSetting.RoomMaxPlayerCount);
                    room.BattleCheckMap[c2SMessage.Frame].Add(c2SMessage.Md5);

                    if (room.BattleCheckMap[c2SMessage.Frame].Count == GameSetting.RoomMaxPlayerCount)
                    {
                        var errorCode = room.BattleCheckMap[c2SMessage.Frame].Distinct().Count() == 1
                            ? pb.BattleErrorCode.BattleErrBattleOk
                            : pb.BattleErrorCode.BattleErrDiff;
                        foreach (var gamerId in room.Gamers)
                        {
                            var gamer = gameManager.GetGamerById(gamerId);
                            SendBattleCheckMessage(gamer.BattleData.ConnectionId, errorCode, c2SMessage.Frame);
                        }
                    }
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgExit:
                {
                    var c2SMessage = pb.C2S_BattleExitMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"BattleMsgExit -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId}");
                    HandleBattleExitMessage(connectionId, c2SMessage);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgReconnect:
                {
                    var c2SMessage = pb.C2S_BattleReconnectMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"BattleMsgReconnect -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId} lastReceivedFrame:{c2SMessage.LastReceivedFrame}");
                    HandleBattleReconnectMessage(connectionId, c2SMessage);
                    break;
                }
            }
        }, _kcpServerTransport.Shutdown);
    }

    /// <summary>
    /// KCP 断开连接
    /// </summary>
    private void OnKcpDisconnected(int connectionId)
    {
        LogManager.Instance.Log(LogType.Error, $"OnKcpDisconnected connectionId:{connectionId}");
        var gameManager = GameManager.Instance;
        if (!gameManager.TryGetGamerByConnectionId(connectionId, out var gamer) || gamer == null)
        {
            LogManager.Instance.Log(LogType.Warning, $"OnKcpDisconnected gamer not found connectionId:{connectionId}");
            return;
        }

        if (gameManager.TryGetRoom(gamer.LogicData.RoomId, out var room) &&
            room != null &&
            room.Gamers.Contains(gamer.LogicData.ID) &&
            room.IsBattleRunning &&
            !room.IsBattleExiting)
        {
            gameManager.MarkGamerDisconnected(gamer.LogicData.ID);
            LogManager.Instance.Log(LogType.Info, $"OnKcpDisconnected mark gamer disconnected only -> roomId:{room.RoomId} playerId:{gamer.LogicData.ID}");
            return;
        }

        gameManager.RemoveGamerConnectionId(gamer.LogicData.ID);
        if (room != null)
            room.Readies.Remove(gamer.LogicData.ID);
    }

    /// <summary>
    /// KCP 错误
    /// </summary>
    private void OnKcpError(int connectionId, kcp2k.ErrorCode errorCode, string error)
    {
        LogManager.Instance.Log(LogType.Error, $"OnKcpError connectionId:{connectionId} errorCode:{errorCode} error:{error}");
    }

    /// <summary>
    /// 启动 KCP
    /// </summary>
    public void KcpStart()
    {
        _kcpServerTransport.Start();
    }

    /// <summary>
    /// 更新 KCP
    /// </summary>
    public void KcpUpdate()
    {
        _kcpServerTransport.Update();
    }

    /// <summary>
    /// 关闭 KCP
    /// </summary>
    public void KcpShutdown()
    {
        _kcpServerTransport.Shutdown();
    }

    /// <summary>
    /// 发送战斗连接回包
    /// </summary>
    private void SendBattleConnectMessage(int connectionId, pb.BattleErrorCode errorCode)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ConnectMsg>();
        s2CMessage.ErrorCode = errorCode;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgConnect, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送准备回包
    /// </summary>
    private void SendBattleReadyMessage(int connectionId, pb.BattleErrorCode errorCode, uint roomId, List<uint> readies)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_ReadyMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.Status.Clear();
        s2CMessage.Status.AddRange(readies);
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgReady, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送开始回包
    /// </summary>
    private void SendBattleStartMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, ulong timestamp)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_StartMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        s2CMessage.TimeStamp = timestamp;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgStart, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送心跳回包
    /// </summary>
    private void SendBattleHeartbeatMessage(int connectionId, pb.BattleErrorCode errorCode, ulong timestamp)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_HeartbeatMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.TimeStamp = timestamp;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgHeartbeat, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送重连回包
    /// </summary>
    private void SendBattleReconnectMessage(int connectionId, pb.BattleErrorCode errorCode, uint roomId, uint authoritativeFrame, uint playerPos, string reason)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_BattleReconnectMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.AuthoritativeFrame = authoritativeFrame;
        s2CMessage.PlayerPos = playerPos;
        s2CMessage.Reason = reason;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgReconnect, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送帧消息
    /// </summary>
    private void SendBattleFrameMessage(int connectionId, pb.BattleErrorCode errorCode, uint frame, uint playerCount, uint inputCount, byte[] datum)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_FrameMsg>(true);
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        s2CMessage.PlayerCount = playerCount;
        s2CMessage.InputCount = inputCount;
        s2CMessage.Datum = ByteString.CopyFrom(datum);
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgFrame, s2CMessage, connectionId);
    }

    /// <summary>
    /// 发送校验回包
    /// </summary>
    private void SendBattleCheckMessage(int connectionId, pb.BattleErrorCode errorCode, int frame)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_CheckMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.Frame = frame;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgCheck, s2CMessage, connectionId);
    }

    /// <summary>
    /// 处理战斗重连
    /// </summary>
    private void HandleBattleReconnectMessage(int connectionId, pb.C2S_BattleReconnectMsg message)
    {
        var gameManager = GameManager.Instance;
        if (!gameManager.TryGetGamerById(message.PlayerId, out var gamer) || gamer == null)
        {
            SendBattleReconnectMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, 0, 0, "PlayerNotFound");
            return;
        }

        if (!gameManager.TryGetReconnectRoom(message.PlayerId, out var room) || room == null)
        {
            SendBattleReconnectMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, 0, 0, "ReconnectRoomNotFound");
            return;
        }

        if (gamer.LogicData.RoomId != message.RoomId || room.RoomId != message.RoomId)
        {
            SendBattleReconnectMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, 0, 0, "RoomMismatch");
            return;
        }

        if (!room.Gamers.Contains(message.PlayerId))
        {
            SendBattleReconnectMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, 0, 0, "PlayerNotInRoom");
            return;
        }

        if (!room.IsBattleRunning || room.IsBattleExiting)
        {
            SendBattleReconnectMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, 0, 0, "RoomNotRunning");
            return;
        }

        if (gamer.BattleData.Pos < 0)
        {
            SendBattleReconnectMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, 0, 0, "PlayerPosInvalid");
            return;
        }

        var authoritativeFrameSnapshot = GetLastCompletedFrame(room);
        var clampedLastReceivedFrame = Math.Min(message.LastReceivedFrame, (uint)Math.Max(0, authoritativeFrameSnapshot - 1));
        gameManager.UpdateGamerConnectionId(message.PlayerId, connectionId);
        gamer.BattleData.ConnectionState = BattleConnectionState.Reconnecting;
        gamer.BattleData.LastReceivedFrame = clampedLastReceivedFrame;

        SendBattleReconnectMessage(
            connectionId,
            pb.BattleErrorCode.BattleErrBattleOk,
            room.RoomId,
            (uint)authoritativeFrameSnapshot,
            (uint)gamer.BattleData.Pos,
            string.Empty);

        var replaySucceeded = ReplayMissingFramesToGamer(room, gamer, authoritativeFrameSnapshot, clampedLastReceivedFrame, connectionId);
        if (replaySucceeded &&
            gamer.BattleData.ConnectionId == connectionId &&
            gamer.BattleData.ConnectionState == BattleConnectionState.Reconnecting)
        {
            gamer.BattleData.ConnectionState = BattleConnectionState.Online;
        }
    }

    /// <summary>
    /// 顺序补发缺失帧
    /// </summary>
    private bool ReplayMissingFramesToGamer(RoomInfo room, GamerInfo gamer, int authoritativeFrameSnapshot, uint lastReceivedFrame, int reconnectConnectionId)
    {
        if (authoritativeFrameSnapshot < 1)
            return true;

        var replayTargetFrame = Math.Min(authoritativeFrameSnapshot, BattleSetting.MaxFrameCount - 1);
        var replayedLastFrame = lastReceivedFrame;
        while (replayedLastFrame < replayTargetFrame)
        {
            var startFrame = Math.Max(1, (int)replayedLastFrame + 1);
            for (var frame = startFrame; frame <= replayTargetFrame; frame++)
            {
                if (gamer.BattleData.ConnectionId != reconnectConnectionId ||
                    gamer.BattleData.ConnectionState != BattleConnectionState.Reconnecting)
                {
                    return false;
                }

                var datum = BuildBattleFrameBytes(room, frame);
                SendBattleFrameMessage(
                    reconnectConnectionId,
                    pb.BattleErrorCode.BattleErrBattleOk,
                    (uint)frame,
                    (uint)room.Gamers.Count,
                    room.InputCounts[frame],
                    datum);
                replayedLastFrame = (uint)frame;
                gamer.BattleData.LastReceivedFrame = replayedLastFrame;
            }

            replayTargetFrame = Math.Min(GetLastCompletedFrame(room), BattleSetting.MaxFrameCount - 1);
            if (replayTargetFrame < 1)
                break;
        }

        return true;
    }

    /// <summary>
    /// 处理主动退出战斗
    /// </summary>
    private void HandleBattleExitMessage(int connectionId, pb.C2S_BattleExitMsg message)
    {
        var gameManager = GameManager.Instance;
        if (!gameManager.TryGetGamerByConnectionId(connectionId, out var connectionGamer) || connectionGamer == null)
        {
            SendBattleExitMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, message.PlayerId, "ConnectionPlayerNotFound");
            return;
        }

        if (connectionGamer.LogicData.ID != message.PlayerId || connectionGamer.LogicData.RoomId != message.RoomId)
        {
            LogManager.Instance.Log(LogType.Warning, $"BattleMsgExit rejected because connection does not match player -> connectionId:{connectionId} connectionPlayerId:{connectionGamer.LogicData.ID} messagePlayerId:{message.PlayerId} connectionRoomId:{connectionGamer.LogicData.RoomId} messageRoomId:{message.RoomId}");
            SendBattleExitMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, message.PlayerId, "ConnectionPlayerMismatch");
            return;
        }

        if (!gameManager.TryGetRoom(message.RoomId, out var room) || room == null)
        {
            SendBattleExitMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, message.PlayerId, "RoomNotFound");
            return;
        }

        if (!room.Gamers.Contains(message.PlayerId))
        {
            SendBattleExitMessage(connectionId, pb.BattleErrorCode.BattleErrData, message.RoomId, message.PlayerId, "PlayerNotInRoom");
            return;
        }

        BroadcastRoomBattleExitToAllGamers(room, pb.BattleErrorCode.BattleErrBattleOk, message.PlayerId, BattleExitReasonPlayerExit);
    }

    /// <summary>
    /// 全房广播战斗退出
    /// </summary>
    private void BroadcastRoomBattleExitToAllGamers(RoomInfo room, pb.BattleErrorCode errorCode, uint operatorPlayerId, string reason, int? excludedConnectionId = null)
    {
        if (!TryBeginBattleExit(room))
            return;

        var gameManager = GameManager.Instance;
        var playerIds = room.Gamers.ToArray();
        var shouldResetImmediately = room.BattleTask == null;
        LogManager.Instance.Log(LogType.Info, $"BroadcastRoomBattleExitToAllGamers -> roomId:{room.RoomId} operatorPlayerId:{operatorPlayerId} reason:{reason} gamerCount:{playerIds.Length} skippedConnectionId:{(excludedConnectionId.HasValue ? excludedConnectionId.Value.ToString() : "None")}");

        try
        {
            try
            {
                if (!room.BattleCancellationTokenSource.IsCancellationRequested)
                    room.BattleCancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            foreach (var playerId in playerIds)
            {
                var gamer = gameManager.GetGamerById(playerId);
                var targetConnectionId = gamer.BattleData.ConnectionId;
                if (targetConnectionId < 0)
                    continue;

                if (excludedConnectionId.HasValue && targetConnectionId == excludedConnectionId.Value)
                {
                    LogManager.Instance.Log(LogType.Info, $"BroadcastRoomBattleExitToAllGamers skip unreachable connection -> roomId:{room.RoomId} playerId:{playerId} connectionId:{targetConnectionId}");
                    continue;
                }

                SendBattleExitMessage(targetConnectionId, errorCode, room.RoomId, operatorPlayerId, reason);
            }
        }
        finally
        {
            if (shouldResetImmediately)
                gameManager.ResetRoomBattleState(room.RoomId);
        }
    }

    /// <summary>
    /// 尝试进入战斗退出流程
    /// </summary>
    private bool TryBeginBattleExit(RoomInfo room)
    {
        lock (room)
        {
            if (room.IsBattleExiting || (!room.IsBattleRunning && room.Gamers.Count == 0))
                return false;

            room.IsBattleExiting = true;
            return true;
        }
    }

    /// <summary>
    /// 退出过程中中断战斗循环
    /// </summary>
    private static void ThrowIfBattleExitRequested(RoomInfo room, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (room.IsBattleExiting)
            throw new OperationCanceledException(cancellationToken);
    }

    /// <summary>
    /// 获取当前房间最后一帧已完成并已广播的权威帧
    /// </summary>
    private static int GetLastCompletedFrame(RoomInfo room)
    {
        return Math.Max(0, room.AuthoritativeFrame - 1);
    }

    /// <summary>
    /// 发送战斗退出消息
    /// </summary>
    private void SendBattleExitMessage(int connectionId, pb.BattleErrorCode errorCode, uint roomId, uint operatorPlayerId, string reason)
    {
        if (!KcpActive || connectionId < 0)
            return;

        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_BattleExitMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        s2CMessage.OperatorPlayerId = operatorPlayerId;
        s2CMessage.Reason = reason;
        _kcpServerTransport.SendMessage(pb.BattleMsgID.BattleMsgExit, s2CMessage, connectionId);
    }

    /// <summary>
    /// 收到 TCP 消息
    /// </summary>
    private void OnTcpDataReceived(byte[] data, int read, NetworkStream stream)
    {
        if (!TcpActive)
        {
            LogManager.Instance.Log(LogType.Warning, "Tcp Not Active!");
            return;
        }

        var gameManager = GameManager.Instance;
        _tcpServerTransport.OnMessageProcess(data, _memoryStream, cmd =>
        {
            LogManager.Instance.Log(LogType.Info, $"OnTcpDataReceived data.len:{data.Length} read:{read}");
            switch (cmd)
            {
                case (byte)pb.LogicMsgID.LogicMsgLogin:
                {
                    var c2SMessage = pb.C2S_LoginMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"LogicMsgLogin -> account:{c2SMessage.Account.ToStringUtf8()} password:{c2SMessage.Password.ToStringUtf8()}");
                    var gamer = gameManager.GetOrCreateGamer(c2SMessage.Account.ToStringUtf8(), c2SMessage.Password.ToStringUtf8());
                    gamer.LogicData.NetworkStream = stream;

                    var canReconnect = false;
                    uint reconnectRoomId = 0;
                    uint reconnectFrame = 0;
                    uint reconnectPlayerPos = 0;
                    var reconnectGamers = Array.Empty<uint>();
                    if (gameManager.TryGetReconnectRoom(gamer.LogicData.ID, out var reconnectRoom) &&
                        reconnectRoom != null)
                    {
                        var reconnectLastCompletedFrame = GetLastCompletedFrame(reconnectRoom);
                        canReconnect = true;
                        reconnectRoomId = reconnectRoom.RoomId;
                        reconnectFrame = (uint)reconnectLastCompletedFrame;
                        reconnectPlayerPos = (uint)gamer.BattleData.Pos;
                        reconnectGamers = reconnectRoom.Gamers.ToArray();
                    }

                    SendLogicLoginMessage(
                        stream,
                        pb.LogicErrorCode.LogicErrOk,
                        gamer.LogicData.ID,
                        canReconnect,
                        reconnectRoomId,
                        reconnectFrame,
                        reconnectGamers,
                        reconnectPlayerPos);
                    break;
                }
                case (byte)pb.LogicMsgID.LogicMsgCreateRoom:
                {
                    var c2SMessage = pb.C2S_CreateRoomMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"LogicMsgCreateRoom -> playerId:{c2SMessage.PlayerId}");
                    var room = gameManager.CreateRoom();
                    foreach (var kv in gameManager.GamerInfoDic)
                        SendLogicCreateRoomMessage(kv.Value.LogicData.NetworkStream, pb.LogicErrorCode.LogicErrOk, room.RoomId);
                    break;
                }
                case (byte)pb.LogicMsgID.LogicMsgJoinRoom:
                {
                    var c2SMessage = pb.C2S_JoinRoomMsg.Parser.ParseFrom(_memoryStream);
                    LogManager.Instance.Log(LogType.Info, $"LogicMsgJoinRoom -> roomId:{c2SMessage.RoomId} playerId:{c2SMessage.PlayerId}");

                    var room = gameManager.GetRoom(c2SMessage.RoomId);
                    var gamer = gameManager.GetGamerById(c2SMessage.PlayerId);
                    gamer.LogicData.RoomId = c2SMessage.RoomId;
                    if (!room.Gamers.Contains(gamer.LogicData.ID))
                        room.Gamers.Add(gamer.LogicData.ID);

                    if (room.Gamers.Count == GameSetting.RoomMaxPlayerCount && !KcpActive)
                    {
                        KcpStart();
                        KcpUpdate();
                    }

                    foreach (var kv in gameManager.GamerInfoDic)
                        SendLogicJoinRoomMessage(kv.Value.LogicData.NetworkStream, pb.LogicErrorCode.LogicErrOk, room.RoomId, room.Gamers);
                    break;
                }
            }
        }, _tcpServerTransport.Shutdown);
    }

    /// <summary>
    /// 启动 TCP
    /// </summary>
    public void TcpStart()
    {
        _tcpServerTransport.Start();
    }

    /// <summary>
    /// 关闭 TCP
    /// </summary>
    public void TcpShutdown()
    {
        _tcpServerTransport.Shutdown();
    }

    /// <summary>
    /// 发送登录回包
    /// </summary>
    private void SendLogicLoginMessage(
        NetworkStream stream,
        pb.LogicErrorCode errorCode,
        uint playerId,
        bool canReconnect,
        uint reconnectRoomId,
        uint reconnectFrame,
        IReadOnlyCollection<uint> reconnectGamers,
        uint reconnectPlayerPos)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_LoginMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.PlayerId = playerId;
        s2CMessage.CanReconnect = canReconnect;
        s2CMessage.ReconnectRoomId = reconnectRoomId;
        s2CMessage.ReconnectFrame = reconnectFrame;
        s2CMessage.ReconnectGamers.Clear();
        s2CMessage.ReconnectGamers.AddRange(reconnectGamers);
        s2CMessage.ReconnectPlayerPos = reconnectPlayerPos;
        _tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgLogin, s2CMessage, stream);
    }

    /// <summary>
    /// 发送创建房间回包
    /// </summary>
    private void SendLogicCreateRoomMessage(NetworkStream stream, pb.LogicErrorCode errorCode, uint roomId)
    {
        var s2CMessage = MsgPoolManager.Instance.Require<pb.S2C_CreateRoomMsg>();
        s2CMessage.ErrorCode = errorCode;
        s2CMessage.RoomId = roomId;
        _tcpServerTransport.SendMessage(pb.LogicMsgID.LogicMsgCreateRoom, s2CMessage, stream);
    }

    /// <summary>
    /// 发送加入房间回包
    /// </summary>
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
    /// 服务端战斗帧轮询
    /// </summary>
    private void OnServerBattleStart(RoomInfo room)
    {
        var gameManager = GameManager.Instance;
        if (room.IsBattleRunning)
            return;

        room.IsBattleRunning = true;
        room.IsBattleExiting = false;
        try
        {
            if (room.BattleCancellationTokenSource.IsCancellationRequested)
            {
                room.BattleCancellationTokenSource.Dispose();
                room.BattleCancellationTokenSource = new CancellationTokenSource();
            }
        }
        catch (ObjectDisposedException)
        {
            room.BattleCancellationTokenSource = new CancellationTokenSource();
        }

        var cancellationToken = room.BattleCancellationTokenSource.Token;
        room.BattleStopwatch.Restart();
        room.BattleTask = Task.CompletedTask;
        for (var i = 0; i < room.Gamers.Count; i++)
        {
            var gamer = gameManager.GetGamerById(room.Gamers[i]);
            gamer.BattleData.LastReceivedFrame = 0;
            Array.Clear(gamer.BattleData.Frames, 0, gamer.BattleData.Frames.Length);
            SendBattleStartMessage(gamer.BattleData.ConnectionId, pb.BattleErrorCode.BattleErrBattleOk, (uint)room.AuthoritativeFrame, (ulong)room.BattleStopwatch.ElapsedMilliseconds);
        }

        room.BattleTask = Task.Run(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        ThrowIfBattleExitRequested(room, cancellationToken);
                        if (room.AuthoritativeFrame >= 0)
                        {
                            if (room.AuthoritativeFrame >= BattleSetting.MaxFrameCount)
                                throw new Exception($"AuthoritativeFrame: {room.AuthoritativeFrame} >= MaxFrameCount: {BattleSetting.MaxFrameCount}");

                            ThrowIfBattleExitRequested(room, cancellationToken);
                            if (room.AuthoritativeFrame == 0)
                                room.InputCounts[room.AuthoritativeFrame] = 0x3;

                            ThrowIfBattleExitRequested(room, cancellationToken);
                            for (var i = 0; i < room.Gamers.Count; i++)
                            {
                                ThrowIfBattleExitRequested(room, cancellationToken);
                                var gamer = gameManager.GetGamerById(room.Gamers[i]);
                                if (gamer.BattleData.ConnectionState != BattleConnectionState.Disconnected)
                                    continue;

                                var pos = gamer.BattleData.Pos;
                                if (pos < 0)
                                    continue;

                                gamer.BattleData.Frames[room.AuthoritativeFrame] = BuildNoopInputByte(pos);
                                room.InputCounts[room.AuthoritativeFrame] |= (byte)(1 << pos);
                            }

                            var byteArray = BuildBattleFrameBytes(room, room.AuthoritativeFrame);
                            ThrowIfBattleExitRequested(room, cancellationToken);
                            for (var i = 0; i < room.Gamers.Count; i++)
                            {
                                ThrowIfBattleExitRequested(room, cancellationToken);
                                var gamer = gameManager.GetGamerById(room.Gamers[i]);
                                if (!CanSendBattleFrameToGamer(room, gamer))
                                    continue;

                                SendBattleFrameMessage(
                                    gamer.BattleData.ConnectionId,
                                    pb.BattleErrorCode.BattleErrBattleOk,
                                    (uint)room.AuthoritativeFrame,
                                    (uint)room.Gamers.Count,
                                    room.InputCounts[room.AuthoritativeFrame],
                                    byteArray);
                                gamer.BattleData.LastReceivedFrame = (uint)room.AuthoritativeFrame;
                            }
                        }

                        ThrowIfBattleExitRequested(room, cancellationToken);
                        room.AuthoritativeFrame++;
                        if (room.AuthoritativeFrame >= BattleSetting.MaxFrameCount)
                            throw new Exception($"AuthoritativeFrame: {room.AuthoritativeFrame} >= MaxFrameCount: {BattleSetting.MaxFrameCount}");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogManager.Instance.Log(LogType.Exception, $"OnServerBattleStart BattleServerUpdate Exception ->\n{ex.Message}\n{ex.StackTrace}");
                        BroadcastRoomBattleExitToAllGamers(room, pb.BattleErrorCode.BattleErrData, 0, BattleExitReasonServerError);
                        break;
                    }

                    try
                    {
                        await Task.Delay(BattleSetting.BattleInterval, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                gameManager.ResetRoomBattleState(room.RoomId);
            }
        });
    }

    /// <summary>
    /// 构建断线玩家空操作输入
    /// </summary>
    private static byte BuildNoopInputByte(int pos)
    {
        return (byte)(pos & 0x01);
    }

    /// <summary>
    /// 构建某一帧完整的战斗数据
    /// </summary>
    private static byte[] BuildBattleFrameBytes(RoomInfo room, int frame)
    {
        var gameManager = GameManager.Instance;
        var byteArray = new byte[room.Gamers.Count];
        for (var i = 0; i < room.Gamers.Count; i++)
        {
            var gamer = gameManager.GetGamerById(room.Gamers[i]);
            if (gamer.BattleData.Pos < 0 || gamer.BattleData.Pos >= byteArray.Length)
                continue;

            byteArray[gamer.BattleData.Pos] = (byte)((gamer.BattleData.Frames[frame] & ~0x01) | (byte)gamer.BattleData.Pos);
        }

        return byteArray;
    }

    /// <summary>
    /// 当前玩家是否可以接收实时帧
    /// </summary>
    private static bool CanSendBattleFrameToGamer(RoomInfo room, GamerInfo gamer)
    {
        if (!room.Readies.Contains(gamer.LogicData.ID))
            return false;

        if (gamer.BattleData.ConnectionState != BattleConnectionState.Online)
            return false;

        if (gamer.BattleData.ConnectionId < 0)
            return false;

        return gamer.BattleData.LastReceivedFrame + 1 >= room.AuthoritativeFrame;
    }
}
