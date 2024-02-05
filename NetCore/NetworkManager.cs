using System.Diagnostics;
using System.Net.Sockets;
using kcp2k;

public class NetworkManager
{
    private static NetworkManager _instance;
    public static NetworkManager Instance => _instance ?? (_instance = new NetworkManager());

    private MemoryStream _memoryStream;
    private NetTcpServer _netTcpServer;
    private NetKcpServer _netKcpServer;
    private Dictionary<uint, NetworkStream> _networkTcpStreams;

    private Task _battleTask;
    private Stopwatch _battleStopwatch;
    private CancellationTokenSource _cancellationTokenSource;
    
    private uint _serverFrame = 0;
    public void Initialize()
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

    public void StartTcpServer()
    {
        _netTcpServer.StartServer(NetConstant.TcpAddress, NetConstant.TcpPort);
    }

    public void StartKcpServer()
    {
        _netKcpServer.StartServerTick();
    }

    public void OnKcpConnected(int connectionId)
    {
        Logger.Log(LogLevel.Info, $"[KCP] OnClientConnected connectionId:{connectionId}");
    }

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

    private void OnKcpProcessConnectMsg(Stream stream, int connectionId)
    {
        if(!GameManager.Instance.ConnectionIds.Contains(connectionId)) GameManager.Instance.ConnectionIds.Add(connectionId);
        GameManager.Instance.CacheFrames.TryAdd(connectionId, new int[10000]);
                    
        var c2SMsg = pb.C2S_ConnectMsg.Parser.ParseFrom(_memoryStream);
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgConnect -> playerId:{c2SMsg.PlayerId} seasonId:{c2SMsg.SeasonId}");
        var s2CMsg = new pb.S2C_ConnectMsg() { ErrorCode = pb.BattleErrorCode.BattleErrBattleOk };
        _netKcpServer.SendKcpMsg(connectionId, pb.BattleMsgID.BattleMsgConnect, s2CMsg);
    }

    private void OnKcpProcessReadyMsg(Stream stream, int connectionId)
    {
        var c2SMsg = pb.C2S_ReadyMsg.Parser.ParseFrom(_memoryStream);
        if(!GameManager.Instance.Readys.Contains(c2SMsg.PlayerId)) GameManager.Instance.Readys.Add(c2SMsg.PlayerId);
                    
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgReady -> playerId:{c2SMsg.PlayerId} roomId:{c2SMsg.RoomId}");
        var s2CMsg = new pb.S2C_ReadyMsg() { ErrorCode = pb.BattleErrorCode.BattleErrBattleOk, };
        s2CMsg.Status.AddRange(GameManager.Instance.Readys);
        GameManager.Instance.ConnectionIds.ForEach(t => _netKcpServer.SendKcpMsg(t, pb.BattleMsgID.BattleMsgReady, s2CMsg));

        if (GameManager.Instance.Readys.Count == GameManager.RoomMaxPlayerCount)
            OnServerBattleStart();
    }

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

    private void OnKcpProcessFrameMsg(Stream stream, int connectionId)
    {
        var c2SMsg = pb.C2S_FrameMsg.Parser.ParseFrom(_memoryStream);
        Logger.Log(LogLevel.Info, $"[KCP] BattleMsgFrame -> connectionId:{connectionId} clientNextFrame:{c2SMsg.Frame} data:{c2SMsg.Data}");
        GameManager.Instance.CacheFrames[connectionId][c2SMsg.Frame] = c2SMsg.Data;
    }

    public void OnKcpDisconnected(int connectionId)
    {
        Logger.Log(LogLevel.Info, $"[KCP] OnClientDisconnected connectionId:{connectionId}");
        lock (GameManager.Instance.ConnectionIds)
        {
            GameManager.Instance.ConnectionIds.Remove(connectionId);
        }
    }

    public void OnKcpError(int connectionId, ErrorCode error, string reason)
    {
        Logger.Log(LogLevel.Error, $"[KCP] OnServerError({connectionId}, {error}, {reason}");
        _netKcpServer.DisconnectClient(connectionId);
    }

    private void OnServerBattleStart()
    {
        _battleStopwatch.Start();
        var startS2CMsg = new pb.S2C_StartMsg()
        {
            ErrorCode = pb.BattleErrorCode.BattleErrBattleOk,
            Frame = _serverFrame,
            TimeStamp = (ulong)_battleStopwatch.ElapsedMilliseconds,
        };
        GameManager.Instance.ConnectionIds.ForEach(t => _netKcpServer.SendKcpMsg(t, pb.BattleMsgID.BattleMsgStart, startS2CMsg));
        StartBattleThread();
    }
    
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

    private Task ProcessBattle(CancellationToken cancellationToken, pb.S2C_FrameMsg s2CFrameMsg)
    {
        try
        {
            lock (GameManager.Instance.ConnectionIds)
            {
                s2CFrameMsg.Frame = _serverFrame;
                s2CFrameMsg.Datum.Clear();
                GameManager.Instance.ConnectionIds.ForEach(t=>s2CFrameMsg.Datum.Add(GameManager.Instance.CacheFrames[t][_serverFrame]));
                GameManager.Instance.ConnectionIds.ForEach(t=>_netKcpServer.SendKcpMsg(t, pb.BattleMsgID.BattleMsgFrame, s2CFrameMsg));
            }
            _serverFrame++;
        }
        catch (Exception e)
        {
            Logger.Log(LogLevel.Exception, e.StackTrace);
            _cancellationTokenSource.Cancel();
        }
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

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

    private void OnTcpProcessCreateRoomMsg(NetworkStream stream, pb.C2S_CreateRoomMsg c2SMsg)
    {
        Logger.Log(LogLevel.Info, $"[TCP][C2S_LoginMsg|{c2SMsg.CalculateSize()}] PlayerId:{c2SMsg.PlayerId}");

        var calRoomId = (uint)(GameManager.DefaultRoomIdBase + GameManager.Instance.RoomIdList.Count);
        GameManager.Instance.RoomIdList.Add(calRoomId);
        GameManager.Instance.RoomInfoDic[calRoomId] = new List<uint>();
        var s2CMsg = new pb.S2C_CreateRoomMsg()
        {
            ErrorCode = pb.LogicErrorCode.LogicErrOk,
            RoomId = calRoomId,
        };
        foreach (var streamInfo in _networkTcpStreams)
            _netTcpServer.SendTcpMsg(pb.LogicMsgID.LogicMsgCreateRoom, s2CMsg, streamInfo.Value);
    }

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