using System.Diagnostics;
using System.Net.Sockets;
using Google.Protobuf;
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
    
    private byte[] _frameInputs;
    private MemoryStream _frameMemoryStream;
    private BinaryWriter _frameStreamWriter;
    private BinaryReader _frameStreamReader;
    private object frameLock;
    
    /// <summary>
    /// 初始化
    /// </summary>
    public override void Initialize()
    {
        frameLock = new object();
        _frameInputs = new byte[10];
        _frameMemoryStream = new MemoryStream(_frameInputs);
        _frameStreamWriter = new BinaryWriter(_frameMemoryStream);
        
        _memoryStream = new MemoryStream();
        _frameStreamReader = new BinaryReader(_memoryStream);
        _battleStopwatch = new Stopwatch();
        
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
        var c2SMsg = pb.C2S_ConnectMsg.Parser.ParseFrom(_memoryStream);
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgConnect -> playerId:{c2SMsg.PlayerId} seasonId:{c2SMsg.SeasonId}");
        var gamer = GameManager.Instance.GetGamerByPlayerId((int)c2SMsg.PlayerId);
        if (gamer == null)
        {
            Logger.Log(LogLevel.Error, $"[KCP] BattleMsgConnect PlayerId:{c2SMsg.PlayerId} Not Found!");
            return;
        }
        gamer.ConnectionId = connectionId;
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
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgReady -> playerId:{c2SMsg.PlayerId} roomId:{c2SMsg.RoomId}");

        var gamer = GameManager.Instance.GetGamerByConnectionId(connectionId);
        var roomId = -1;
        if (gamer == null)
        {
            Logger.Log(LogLevel.Error, $"[KCP] BattleMsgReady ConnectionId:{connectionId} Not Found!");
            return;
        }
        gamer.Ready = true;
        roomId = gamer.RoomId;

        var s2CMsg = new pb.S2C_ReadyMsg()
        {
            ErrorCode = pb.BattleErrorCode.BattleErrBattleOk,
            RoomId = c2SMsg.RoomId,
        };
        var room = GameManager.Instance.GetRoomByRoomId(roomId);
        if (room == null)
        {
            Logger.Log(LogLevel.Error, $"[KCP] BattleMsgReady RoomId:{roomId} Not Found!");
            return;
        }

        foreach (var g in room.Gamers.Where(g => g.Ready))
        {
            s2CMsg.Status.Add((uint)g.PlayerId);
        }
        foreach (var g in room.Gamers)
            _netKcpServer.SendKcpMsg(g.ConnectionId, pb.BattleMsgID.BattleMsgReady, s2CMsg);

        if (s2CMsg.Status.Count == GameManager.RoomMaxPlayerCount)
            OnServerBattleStart(room.RoomId);
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
        var frame = _frameStreamReader.ReadInt32();
        var data = _frameStreamReader.ReadByte();
        var pos = (byte)(0x01 & data);
        var gamer = GameManager.Instance.GetGamerByPos(pos);
        if (gamer == null)
        {
            Logger.Log(LogLevel.Error, $"[KCP] BattleMsgFrame Pos:{pos} Not Found!");
            return;
        }
        lock (frameLock)
        {
            gamer.CacheFrames[frame] = data;
        }
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgFrame -> connectionId:{connectionId} clientNextFrame:{frame} data:{data}");
    }

    /// <summary>
    /// 服务器战斗开始
    /// </summary>
    /// <param name="roomId"></param>
    private void OnServerBattleStart(int roomId)
    {
        _battleStopwatch.Start();
        var room = GameManager.Instance.GetRoomByRoomId(roomId);
        if (room == null)
        {
            Logger.Log(LogLevel.Error, $"[KCP] OnServerBattleStart RoomId:{roomId} Not Found!");
            return;
        }
        var startS2CMsg = new pb.S2C_StartMsg()
        {
            ErrorCode = pb.BattleErrorCode.BattleErrBattleOk,
            Frame = (uint)room.AuthoritativeFrame,
            TimeStamp = (ulong)_battleStopwatch.ElapsedMilliseconds,
        };
        foreach (var g in room.Gamers)
            _netKcpServer.SendKcpMsg(g.ConnectionId, pb.BattleMsgID.BattleMsgStart, startS2CMsg);
        StartBattleThread(room);
    }

    /// <summary>
    /// 开启战斗线程
    /// </summary>
    private void StartBattleThread(RoomInfo room)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _cancellationTokenSource.Token;

        var frameS2CMsg = new pb.S2C_FrameMsg() { ErrorCode = pb.BattleErrorCode.BattleErrBattleOk, };
        _battleTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessBattle(cancellationToken, frameS2CMsg, room);
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
    private Task ProcessBattle(CancellationToken cancellationToken, pb.S2C_FrameMsg s2CFrameMsg, RoomInfo room)
    {
        try
        {
            lock (frameLock)
            {
                Array.Clear(_frameInputs, 0, _frameInputs.Length);
                _frameMemoryStream.Reset();;
                _frameStreamWriter.Write(room.AuthoritativeFrame);
                _frameStreamWriter.Write(GameManager.Instance.Gamers.Count);
                foreach (var g in room.Gamers)
                {
                    var inputFrame = (byte)((g.CacheFrames[room.AuthoritativeFrame] & ~0x01) | (byte)g.Pos);
                    _frameStreamWriter.Write(inputFrame);
                }
                foreach (var g in room.Gamers)
                {
                    _netKcpServer.SendKcpMsg(g.ConnectionId, pb.BattleMsgID.BattleMsgFrame, _frameInputs);
                }
            }
            room.AuthoritativeFrame++;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Exception, e.Message + e.StackTrace);
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
        
        var calPlayerId = (uint)(GameManager.DefaultPlayerIdBase + GameManager.Instance.Gamers.Count);
        var gamer = new GamerInfo
        {
            PlayerId = (int)calPlayerId,
            NetworkStream = stream
        };
        GameManager.Instance.Gamers.Add(gamer);
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
        Logger.Log(LogLevel.Info, $"[TCP][C2S_CreateRoomMsg|{c2SMsg.CalculateSize()}] PlayerId:{c2SMsg.PlayerId}");

        var roomId = (uint)(GameManager.DefaultRoomIdBase + GameManager.Instance.Rooms.Count);
        var room = new RoomInfo
        {
            RoomId = (int)roomId,
        };
        GameManager.Instance.Rooms.Add(room);
        var s2CMsg = new pb.S2C_CreateRoomMsg()
        {
            ErrorCode = pb.LogicErrorCode.LogicErrOk,
            RoomId = roomId,
        };
        foreach (var g in GameManager.Instance.Gamers)
            _netTcpServer.SendTcpMsg(pb.LogicMsgID.LogicMsgCreateRoom, s2CMsg, g.NetworkStream);
    }

    /// <summary>
    /// 接收到客户端加入房间的消息处理
    /// </summary>
    /// <param name="stream">客户端流</param>
    /// <param name="c2SMsg">客户端->服务器的数据</param>
    private void OnTcpProcessJoinRoomMsg(NetworkStream stream, pb.C2S_JoinRoomMsg c2SMsg)
    {
        Logger.Log(LogLevel.Info, $"[TCP][C2S_JoinRoomMsg|{c2SMsg.CalculateSize()}] PlayerId:{c2SMsg.PlayerId} RoomId:{c2SMsg.RoomId}");
        
        var room = GameManager.Instance.JoinRoom(c2SMsg.RoomId, c2SMsg.PlayerId);
        var s2CMsg = new pb.S2C_JoinRoomMsg
        {
            ErrorCode = pb.LogicErrorCode.LogicErrOk,
            RoomId = c2SMsg.RoomId,
        };
        if (room == null)
        {
            Logger.Log(LogLevel.Error, $"[TCP][C2S_LoginMsg] RoomId:{c2SMsg.RoomId} Not Found!");
            return;
        }

        var all = new uint[GameManager.RoomMaxPlayerCount];
        foreach (var g in room.Gamers) all[g.Pos] = (uint)g.PlayerId;
        s2CMsg.All.AddRange(all);
        
        foreach (var g in room.Gamers)
            _netTcpServer.SendTcpMsg(pb.LogicMsgID.LogicMsgJoinRoom, s2CMsg, g.NetworkStream);
    }
}