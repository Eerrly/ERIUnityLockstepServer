using System.Diagnostics;
using System.Net.Sockets;
using kcp2k;

/// <summary>
/// 网络管理器
/// </summary>
public class NetworkManager : AManager<NetworkManager>
{
    /// <summary>
    /// 数据流
    /// </summary>
    private MemoryStream _memoryStream;
    /// <summary>
    /// TCP服务器
    /// </summary>
    private NetTcpServer _netTcpServer;
    /// <summary>
    /// KCPf服务器
    /// </summary>
    private NetKcpServer _netKcpServer;
    /// <summary>
    /// TCP客户端流
    /// </summary>
    private Dictionary<uint, NetworkStream> _networkTcpStreams;
    /// <summary>
    /// 战斗线程任务
    /// </summary>
    private Task _battleTask;
    /// <summary>
    /// 战斗计时器
    /// </summary>
    private Stopwatch _battleStopwatch;
    /// <summary>
    /// 取消句柄
    /// </summary>
    private CancellationTokenSource _cancellationTokenSource;
    /// <summary>
    /// 服务器权威帧
    /// </summary>
    private uint _serverAuthoritativeFrame = 0;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        _memoryStream = new MemoryStream();
        _battleStopwatch = new Stopwatch();
        _networkTcpStreams = new Dictionary<uint, NetworkStream>();
        
        _netTcpServer = new NetTcpServer();
        _netTcpServer.Initialize();
        _netTcpServer.OnProcessTcpAcceptData += OnProcessNetTcpAcceptData;

        _netKcpServer = new NetKcpServer();
        _netKcpServer.Initialize();
    }

    /// <summary>
    /// 释放
    /// </summary>
    public override void OnRelease()
    {
        _memoryStream.Close();
        _battleStopwatch.Stop();
        _networkTcpStreams.Clear();
        _netTcpServer.OnProcessTcpAcceptData -= OnProcessNetTcpAcceptData;
        _netTcpServer.OnRelease();
        _netKcpServer.OnRelease();
    }

    /// <summary>
    /// 开启TCP服务器
    /// </summary>
    public void StartTcpServer()
    {
        _netTcpServer.StartServer();
    }

    /// <summary>
    /// 开启KCP服务器
    /// </summary>
    public void StartKcpServer()
    {
        _netKcpServer.StartServer();
    }

    /// <summary>
    /// KCP客户端连接回调
    /// </summary>
    /// <param name="connectionId">客户端连接ID</param>
    public void OnKcpConnected(int connectionId)
    {
        Logger.Log(LogLevel.Info, $"[KCP] OnClientConnected connectionId:{connectionId}");
    }
    
    /// <summary>
    /// KCP客户端断开连接回调
    /// </summary>
    /// <param name="connectionId">客户端连接ID</param>
    public void OnKcpDisconnected(int connectionId)
    {
        Logger.Log(LogLevel.Info, $"[KCP] OnClientDisconnected connectionId:{connectionId}");
        lock (GameManager.Instance.KcpConnectionIds)
        {
            GameManager.Instance.KcpConnectionIds.Remove(connectionId);
        }
    }

    /// <summary>
    /// KCP客户端发生错误回调
    /// </summary>
    /// <param name="connectionId">客户端连接ID</param>
    /// <param name="error">错误码</param>
    /// <param name="reason">错误原因</param>
    public void OnKcpError(int connectionId, ErrorCode error, string reason)
    {
        Logger.Log(LogLevel.Error, $"[KCP] OnServerError({connectionId}, {error}, {reason}");
        _netKcpServer.DisconnectClient(connectionId);
    }

    /// <summary>
    /// KCP客户端接受数据回调
    /// </summary>
    /// <param name="connectionId">客户端连接ID</param>
    /// <param name="message">消息</param>
    /// <param name="channel">消息类型</param>
    public void OnProcessNetKcpAcceptData(int connectionId, ArraySegment<byte> message, KcpChannel channel)
    {
        if (message.Array == null) return;
        _netKcpServer.OnData(message.ToArray(), _memoryStream, cmd =>
        {
            Logger.Log(LogLevel.Info, $"[KCP] OnData Cmd:{cmd} Length:{message.Count} Channel:{Enum.GetName(typeof(KcpChannel), channel)}");
            switch (cmd)
            {
                case (byte)pb.BattleMsgID.BattleMsgConnect:
                {
                    OnKcpProcessConnectMsg(_memoryStream, connectionId);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgReady:
                {
                    OnKcpProcessReadyMsg(_memoryStream, connectionId);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgHeartbeat:
                {
                    OnKcpProcessHeartbeatMsg(_memoryStream, connectionId);
                    break;
                }
                case (byte)pb.BattleMsgID.BattleMsgFrame:
                {
                    OnKcpProcessFrameMsg(_memoryStream, connectionId);
                    break;
                }
            }
        }, () => { _netKcpServer.CloseKcpServer(); });
    }

    /// <summary>
    /// 接受到客户端连接消息处理
    /// </summary>
    /// <param name="stream">数据流</param>
    /// <param name="connectionId">客户端连接ID</param>
    private void OnKcpProcessConnectMsg(Stream stream, int connectionId)
    {
        if(!GameManager.Instance.KcpConnectionIds.Contains(connectionId)) GameManager.Instance.KcpConnectionIds.Add(connectionId);
        GameManager.Instance.CacheFrames.TryAdd(connectionId, new int[10000]);
                    
        var c2SMsg = pb.C2S_ConnectMsg.Parser.ParseFrom(_memoryStream);
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgConnect -> playerId:{c2SMsg.PlayerId} seasonId:{c2SMsg.SeasonId}");
        var s2CMsg = new pb.S2C_ConnectMsg() { ErrorCode = pb.BattleErrorCode.BattleErrBattleOk };
        _netKcpServer.SendKcpMsg(connectionId, pb.BattleMsgID.BattleMsgConnect, s2CMsg);
    }

    /// <summary>
    /// 接收到客户端准备消息处理
    /// </summary>
    /// <param name="stream">数据流</param>
    /// <param name="connectionId">客户端连接ID</param>
    private void OnKcpProcessReadyMsg(Stream stream, int connectionId)
    {
        var c2SMsg = pb.C2S_ReadyMsg.Parser.ParseFrom(_memoryStream);
        if(!GameManager.Instance.Readys.Contains(c2SMsg.PlayerId)) GameManager.Instance.Readys.Add(c2SMsg.PlayerId);
                    
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgReady -> playerId:{c2SMsg.PlayerId} roomId:{c2SMsg.RoomId}");
        var s2CMsg = new pb.S2C_ReadyMsg()
        {
            ErrorCode = pb.BattleErrorCode.BattleErrBattleOk,
            RoomId = c2SMsg.RoomId,
        };
        s2CMsg.Status.AddRange(GameManager.Instance.Readys);
        GameManager.Instance.KcpConnectionIds.ForEach(t => _netKcpServer.SendKcpMsg(t, pb.BattleMsgID.BattleMsgReady, s2CMsg));

        if (GameManager.Instance.Readys.Count == GameManager.RoomMaxPlayerCount)
            OnServerBattleStart();
    }

    /// <summary>
    /// 接受到客户端Ping消息处理
    /// </summary>
    /// <param name="stream">数据流</param>
    /// <param name="connectionId">客户端连接ID</param>
    private void OnKcpProcessHeartbeatMsg(Stream stream, int connectionId)
    {
        var c2SMsg = pb.C2S_HeartbeatMsg.Parser.ParseFrom(_memoryStream);
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgHeartbeat -> playerId:{c2SMsg.PlayerId} timeStamp:{c2SMsg.TimeStamp}");
        var s2CMsg = new pb.S2C_HeartbeatMsg()
        {
            ErrorCode = pb.BattleErrorCode.BattleErrBattleOk,
            TimeStamp = c2SMsg.TimeStamp,
        };
        _netKcpServer.SendKcpMsg(connectionId, pb.BattleMsgID.BattleMsgHeartbeat, s2CMsg);
    }

    /// <summary>
    /// 接受到客户端帧数据消息处理
    /// </summary>
    /// <param name="stream">数据流</param>
    /// <param name="connectionId">客户端连接ID</param>
    private void OnKcpProcessFrameMsg(Stream stream, int connectionId)
    {
        var c2SMsg = pb.C2S_FrameMsg.Parser.ParseFrom(_memoryStream);
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgFrame -> connectionId:{connectionId} clientNextFrame:{c2SMsg.Frame} data:{c2SMsg.Data}");
        GameManager.Instance.CacheFrames[connectionId][c2SMsg.Frame] = c2SMsg.Data;
    }

    /// <summary>
    /// 服务器战斗开始
    /// </summary>
    private void OnServerBattleStart()
    {
        _battleStopwatch.Start();
        var startS2CMsg = new pb.S2C_StartMsg()
        {
            ErrorCode = pb.BattleErrorCode.BattleErrBattleOk,
            Frame = _serverAuthoritativeFrame,
            TimeStamp = (ulong)_battleStopwatch.ElapsedMilliseconds,
        };
        GameManager.Instance.KcpConnectionIds.ForEach(t => _netKcpServer.SendKcpMsg(t, pb.BattleMsgID.BattleMsgStart, startS2CMsg));
        StartBattleThread();
    }
    
    /// <summary>
    /// 开启战斗线程
    /// </summary>
    private void StartBattleThread()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        var frameS2CMsg = new pb.S2C_FrameMsg() { ErrorCode = pb.BattleErrorCode.BattleErrBattleOk, };
        _battleTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessBattle(cancellationToken, frameS2CMsg);
                await Task.Delay(30, cancellationToken);
            }            
        }, cancellationToken);
    }

    /// <summary>
    /// 每帧战斗处理
    /// </summary>
    /// <param name="cancellationToken">取消句柄</param>
    /// <param name="s2CFrameMsg">服务器->客户端的帧消息</param>
    /// <returns>异步任务</returns>
    private Task ProcessBattle(CancellationToken cancellationToken, pb.S2C_FrameMsg s2CFrameMsg)
    {
        try
        {
            lock (GameManager.Instance.KcpConnectionIds)
            {
                s2CFrameMsg.Frame = _serverAuthoritativeFrame;
                s2CFrameMsg.Datum.Clear();
                GameManager.Instance.KcpConnectionIds.ForEach(t=>s2CFrameMsg.Datum.Add(GameManager.Instance.CacheFrames[t][_serverAuthoritativeFrame]));
                GameManager.Instance.KcpConnectionIds.ForEach(t=>_netKcpServer.SendKcpMsg(t, pb.BattleMsgID.BattleMsgFrame, s2CFrameMsg));
            }
            _serverAuthoritativeFrame++;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Exception, e.StackTrace);
            _cancellationTokenSource.Cancel();
        }
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    /// <summary>
    /// TCP客户端接受数据回调
    /// </summary>
    /// <param name="buffer">数据</param>
    /// <param name="read">读到多少</param>
    /// <param name="stream">TCP客户端流</param>
    private void OnProcessNetTcpAcceptData(byte[] buffer, int read, NetworkStream stream)
    {
        _netTcpServer.OnData(buffer, _memoryStream, cmd =>
        {
            switch (cmd)
            {
                case (int)pb.LogicMsgID.LogicMsgLogin:
                {
                    OnTcpProcessLoginMsg(stream, pb.C2S_LoginMsg.Parser.ParseFrom(_memoryStream));
                    break;
                }
                case (int)pb.LogicMsgID.LogicMsgCreateRoom:
                {
                    OnTcpProcessCreateRoomMsg(stream, pb.C2S_CreateRoomMsg.Parser.ParseFrom(_memoryStream));
                    break;
                }
                case (int)pb.LogicMsgID.LogicMsgJoinRoom:
                {
                    OnTcpProcessJoinRoomMsg(stream, pb.C2S_JoinRoomMsg.Parser.ParseFrom(_memoryStream));
                    break;
                }
            }
        }, stream.Close);
    }

    /// <summary>
    /// 接受到客户端登录消息处理
    /// </summary>
    /// <param name="stream">客户端流</param>
    /// <param name="c2SMsg">客户端->服务器的数据</param>
    private void OnTcpProcessLoginMsg(NetworkStream stream, pb.C2S_LoginMsg c2SMsg)
    {
        Logger.Log(LogLevel.Info, $"[TCP][C2S_LoginMsg|{c2SMsg.CalculateSize()}] Account:{c2SMsg.Account.ToStringUtf8()} Password:{c2SMsg.Password.ToStringUtf8()}");
        
        var calPlayerId = (uint)(GameManager.DefaultPlayerIdBase + GameManager.Instance.PlayerIdList.Count);
        _networkTcpStreams[calPlayerId] = stream;
        GameManager.Instance.PlayerIdList.Add(calPlayerId);
        _netTcpServer.SendTcpMsg(pb.LogicMsgID.LogicMsgLogin, new pb.S2C_LoginMsg()
        {
            ErrorCode = pb.LogicErrorCode.LogicErrOk,
            PlayerId = calPlayerId,
        }, stream);
    }

    /// <summary>
    /// 接受到客户端创建房间的消息处理
    /// </summary>
    /// <param name="stream">客户端流</param>
    /// <param name="c2SMsg">客户端->服务器的数据</param>
    private void OnTcpProcessCreateRoomMsg(NetworkStream stream, pb.C2S_CreateRoomMsg c2SMsg)
    {
        Logger.Log(LogLevel.Info, $"[TCP][C2S_LoginMsg|{c2SMsg.CalculateSize()}] PlayerId:{c2SMsg.PlayerId}");

        var roomId = (uint)(GameManager.DefaultRoomIdBase + GameManager.Instance.RoomIdList.Count);
        GameManager.Instance.RoomIdList.Add(roomId);
        GameManager.Instance.RoomInfoDic[roomId] = new List<uint>();
        var s2CMsg = new pb.S2C_CreateRoomMsg()
        {
            ErrorCode = pb.LogicErrorCode.LogicErrOk,
            RoomId = roomId,
        };
        foreach (var streamInfo in _networkTcpStreams)
            _netTcpServer.SendTcpMsg(pb.LogicMsgID.LogicMsgCreateRoom, s2CMsg, streamInfo.Value);
    }

    /// <summary>
    /// 接收到客户端加入房间的消息处理
    /// </summary>
    /// <param name="stream">客户端流</param>
    /// <param name="c2SMsg">客户端->服务器的数据</param>
    private void OnTcpProcessJoinRoomMsg(NetworkStream stream, pb.C2S_JoinRoomMsg c2SMsg)
    {
        Logger.Log(LogLevel.Info, $"[TCP][C2S_LoginMsg|{c2SMsg.CalculateSize()}] PlayerId:{c2SMsg.PlayerId} RoomId:{c2SMsg.RoomId}");
        
        GameManager.Instance.JoinRoom(c2SMsg.RoomId, c2SMsg.PlayerId);
        var s2CMsg = new pb.S2C_JoinRoomMsg
        {
            ErrorCode = pb.LogicErrorCode.LogicErrOk,
            RoomId = c2SMsg.RoomId,
        };
        s2CMsg.All.AddRange(GameManager.Instance.RoomInfoDic[c2SMsg.RoomId]);
        foreach (var streamInfo in _networkTcpStreams)
            _netTcpServer.SendTcpMsg(pb.LogicMsgID.LogicMsgJoinRoom, s2CMsg, streamInfo.Value);
    }
}