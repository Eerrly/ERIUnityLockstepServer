using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ERIUnitySimpleServer
{
    public class CacheUnit
    {
        public byte[] Data;
        public int Length;
    }

    public class ClientInfo
    {
        public byte Pos;
        public Dictionary<int, byte> frameInputDic;
        public bool IsReady;
        public EndPoint ClientEndPoint;

        public ClientInfo(byte pos)
        {
            Pos = pos;
            IsReady = false;
            ClientEndPoint = new IPEndPoint(IPAddress.Any, 0);
            frameInputDic = new Dictionary<int, byte>();
        }
    }
    
    class Program
    {
        private static KCP _kcp;
        private static Socket _socket;
        private static EndPoint _endPoint;
        private static EndPoint _clientPoint;

        private static ushort _index = 0;
        private static byte[] _sendBuffer = new byte[Head.MaxSendLength];
        private static byte[] _recvBuffer = new byte[Head.MaxRecvLength];
        private static EndPoint clientPoint;

        private static Queue<Packet> _sendQueue = new Queue<Packet>();
        private static Queue<Packet> _recvQueue = new Queue<Packet>();
        private static MemoryStream _recvStream;
        private static BinaryReader _binaryReader;

        private static object _sendLock = new object();
        private static Queue<CacheUnit> _finalSendQueue = new Queue<CacheUnit>();

        private static readonly DateTime m_UtcTime = new DateTime(1970, 1, 1);

        private static int _lastRecvFrame = 0;
        private static Dictionary<byte, ClientInfo> _clientInfoDic = new Dictionary<byte, ClientInfo>();

        private static UInt32 iclock()
        {
            return (UInt32)(Convert.ToInt64(DateTime.UtcNow.Subtract(m_UtcTime).TotalMilliseconds) & 0xffffffff);
        }

        private static Stopwatch _watch;

        public static long Time
        {
            get
            {
                if (_watch == null)
                {
                    _watch = new Stopwatch();
                    _watch.Start();
                }

                return _watch.ElapsedMilliseconds;
            }
        }

        /// <summary>
        /// 初始化日志打印
        /// </summary>
        private static void InitLogger()
        {
            Logger.Initialize(Path.Combine(Directory.GetCurrentDirectory(), "server.log"), new Logger());
            Logger.SetLoggerLevel((int)LogLevel.Info | (int)LogLevel.Error | (int)LogLevel.Exception);
            Logger.log = Console.WriteLine;
            Logger.logError = Console.WriteLine;
        }

        /// <summary>
        /// 初始化UDP服务器
        /// </summary>
        private static void InitUDP()
        {
            _endPoint = new IPEndPoint(IPAddress.Parse(NetConstant.IP), NetConstant.Port);
            _socket = new Socket(_endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.ReceiveTimeout = NetConstant.FrameInterval * 10;

            uint IOC_IN = 0x80000000;
            uint IOC_VENDOR = 0x18000000;
            uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            _socket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
            try
            {
                _socket.Bind(_endPoint);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Exception, $"[初始化UDP服务器] 错误信息: {ex.Message}");
            }

            try
            {
                Kcp_Init(0);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Exception, $"[初始化KCP服务器] 错误信息: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化KCP
        /// </summary>
        /// <param name="conv">会话ID</param>
        private static void Kcp_Init(uint conv)
        {
            if (_kcp != null)
            {
                _kcp.Reset(conv, Kcp_Send);
            }
            else
            {
                _kcp = new KCP(conv, Kcp_Send);
            }

            _kcp.NoDelay(1, 1, 2, 1); //快速模式
            _kcp.WndSize(512, 512);
        }

        /// <summary>
        /// KCP发送消息回调
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        private static void Kcp_Send(byte[] data, int size)
        {
            var unit = new CacheUnit
            {
                Data = BufferPool.GetBuffer(size),
                Length = size
            };
            Array.Copy(data, unit.Data, size);

            lock (_sendLock)
            {
                _finalSendQueue.Enqueue(unit);
            }
        }

        static void Main(string[] args)
        {
            _recvStream = new MemoryStream(256);
            _binaryReader = new BinaryReader(_recvStream);

            InitLogger();
            InitUDP();

            var thread = new Thread(UpdateThreadMethod);
            var timer = new Timer(Update, null, 0, NetConstant.FrameInterval);
            thread.Start();
        }

        private static int _usedRecvBufferSize = 0;
        private static int _serverFrame = -1;
        private static bool _isAllReady = false;

        private static void Update(object state)
        {
            if (!_isAllReady) return;

            if (_serverFrame >= 0)
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(stream))
                    {
                        writer.Write(_serverFrame);
                        writer.Write((byte)2);
                        var i0 = (byte)0;
                        var i1 = (byte)1;
                        if (_clientInfoDic.ContainsKey(0) && _clientInfoDic[0].frameInputDic.ContainsKey(_serverFrame)) 
                            i0 = _clientInfoDic[0].frameInputDic[_serverFrame];
                        else if (_clientInfoDic.ContainsKey(0) && _clientInfoDic[0].frameInputDic.ContainsKey(_lastRecvFrame)) 
                            i0 = _clientInfoDic[0].frameInputDic[_lastRecvFrame];
                        if (_clientInfoDic.ContainsKey(1) && _clientInfoDic[1].frameInputDic.ContainsKey(_serverFrame)) 
                            i1 = _clientInfoDic[1].frameInputDic[_serverFrame];
                        else if (_clientInfoDic.ContainsKey(1) && _clientInfoDic[1].frameInputDic.ContainsKey(_lastRecvFrame)) 
                            i1 = _clientInfoDic[1].frameInputDic[_lastRecvFrame];
                        
                        writer.Write(i0);
                        writer.Write(i1);

                        var len = (int)stream.Position;
                        var buffer = BufferPool.GetBuffer(len);
                        stream.Seek(0, SeekOrigin.Begin);
                        stream.Read(buffer, 0, len);

                        Logger.Log(LogLevel.Info, $"给客户端发送帧数据 [serverFrame]->{_serverFrame} [i0]->{i0} [i1]->{i1}");
                        SendDataToClient(buffer, NetConstant.pvpFrameCmd, NetConstant.pvpFrameAct, len);
                    }
                }
            }
            _serverFrame++;
            // lock (_recvQueue)
            // {
            //     while (_recvQueue.Count > 0)
            //     {
            //         var packet = _recvQueue.Dequeue();
            //
            //         var _stream = new MemoryStream(packet.Data);
            //         var _reader = new BinaryReader(_stream);
            //         var frameId = _reader.ReadInt32();
            //         var input = _reader.ReadByte();
            //         
            //         using (var stream = new MemoryStream())
            //         {
            //             using (var writer = new BinaryWriter(stream))
            //             {
            //                 writer.Write(_serverFrame);
            //                 writer.Write((byte)1);
            //                 writer.Write(input);
            //                 
            //                 var len = (int)stream.Position;
            //                 var buffer = BufferPool.GetBuffer(len);
            //                 stream.Seek(0, SeekOrigin.Begin);
            //                 stream.Read(buffer, 0, len);
            //             
            //                 SendDataToClient(buffer, packet.Head.cmd, packet.Head.act, len);
            //             }
            //         }
            //     }
            // }
        }

        /// <summary>
        /// 轮询
        /// </summary>
        private static void UpdateThreadMethod()
        {
            while (_socket != null && _kcp != null)
            {
                lock (_sendLock)
                {
                    while (_sendQueue.Count > 0 && _kcp.WaitSnd() < 512)
                    {
                        var packet = _sendQueue.Dequeue();
                        unsafe
                        {
                            var length = Head.Length + (int)packet.Head.length;
                            var sendBuffer = _sendBuffer;
                            fixed (byte* dest = sendBuffer)
                            {
                                packet.Head.index = _index++;
                                *((Head*)dest) = packet.Head;
                            }

                            Array.Copy(packet.Data, 0, sendBuffer, Head.Length, packet.Head.length);
                            BufferPool.ReleaseBuff(packet.Data);

                            _kcp.Send(sendBuffer, 0, length);
                        }
                    }
                }

                var needFlush = false;
                while (_finalSendQueue.Count > 0)
                {
                    CacheUnit unit = null;
                    lock (_sendLock)
                    {
                        unit = _finalSendQueue.Dequeue();
                    }

                    try
                    {
                        needFlush = true;
                        foreach (var info in _clientInfoDic)
                        {
                            _socket.SendTo(unit.Data, 0, unit.Length, SocketFlags.None, info.Value.ClientEndPoint);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Exception, $"[发送数据] 错误:{ex.Message}");
                    }
                    finally
                    {
                        if (unit != null)
                        {
                            BufferPool.ReleaseBuff(unit.Data);
                            unit = null;
                        }
                    }
                }

                _kcp.Update(iclock(), needFlush);

                var size = Head.MaxRecvLength - _usedRecvBufferSize;
                if (size > 0)
                {
                    if (_clientPoint == null) _clientPoint = new IPEndPoint(IPAddress.Any, 0);
                    try
                    {
                        var length = _socket.Available > 0 ? _socket.ReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref _clientPoint) : 0;
                        if (length > 0)
                        {
                            _usedRecvBufferSize += length;
                            var offset = 0;
                            var result = _kcp.Input(_recvBuffer, ref offset, _usedRecvBufferSize);
                            if (offset == 0 || result == -1) return;
                            _usedRecvBufferSize = _usedRecvBufferSize - offset;
                            if (_usedRecvBufferSize > 0)
                            {
                                Array.Copy(_recvBuffer, offset, _recvBuffer, 0, _usedRecvBufferSize);
                            }

                            var data = BufferPool.GetBuffer(1024);
                            for (var peekSize = _kcp.PeekSize(); peekSize > 0; peekSize = _kcp.PeekSize())
                            {
                                if (data.Length < peekSize)
                                {
                                    BufferPool.ReleaseBuff(data);
                                    data = BufferPool.GetBuffer(peekSize);
                                }

                                if (_kcp.Recv(data) > 0)
                                {
                                    var packet = new Packet();
                                    unsafe
                                    {
                                        fixed (byte* src = data)
                                        {
                                            packet.Head = *((Head*)src);
                                        }
                                    }

                                    if (peekSize != packet.Head.length + Head.Length)
                                    {
                                        Logger.Log(LogLevel.Error, $"接收数据长度不匹配 数据长度:{peekSize} 目标长度:{packet.Head.length + Head.Length}");
                                        return;
                                    }

                                    packet.Data = BufferPool.GetBuffer((int)packet.Head.length);
                                    packet.Length = (int)packet.Head.length;
                                    Array.Copy(data, Head.Length, packet.Data, 0, (int)packet.Head.length);

                                    // Logger.Log(LogLevel.Info, $"收到数据 cmd:{packet.Head.cmd} act:{packet.Head.act} length:{packet.Head.length}");

                                    _recvStream.Position = 0;
                                    _recvStream.SetLength(0);
                                    _recvStream.Write(packet.Data, 0, (int)packet.Head.length);
                                    _recvStream.Seek(0, SeekOrigin.Begin);

                                    if (packet.Head.act == NetConstant.pvpReadyAct)
                                    {
                                        var frameId = _binaryReader.ReadInt32();
                                        var playerId = _binaryReader.ReadByte();
                                        Logger.Log(LogLevel.Info, $"收到客户端发来的准备数据 [frameId]->{frameId} [playerId]->{playerId}");
                                        
                                        if(!_clientInfoDic.ContainsKey(playerId)) _clientInfoDic[playerId] = new ClientInfo(playerId);
                                        _clientInfoDic[playerId].IsReady = true;
                                        _isAllReady = _clientInfoDic.Count == 2 && _clientInfoDic.All(t => t.Value.IsReady != false);

                                        if (_isAllReady)
                                        {
                                            SendDataToClient(packet.Data, packet.Head.cmd, packet.Head.act, (int)packet.Head.length);
                                        }
                                    }
                                    else if (packet.Head.act == NetConstant.pvpPingAct)
                                    {
                                        var clientTime = _binaryReader.ReadInt64();
                                        Logger.Log(LogLevel.Info, $"收到客户端发来的Ping数据 [clientTime]->{clientTime}");
                                        SendDataToClient(packet.Data, packet.Head.cmd, packet.Head.act, (int)packet.Head.length);
                                    }
                                    else if (packet.Head.act == NetConstant.pvpFrameAct)
                                    {
                                        var frameId = _binaryReader.ReadInt32();
                                        var input = _binaryReader.ReadByte();
                                        var pos = (byte)(0x01 & input);
                                        Logger.Log(LogLevel.Info, $"收到客户端发来的操作数据 [pos]->{pos} [frameId]->{frameId} [input]->{input}");

                                        _lastRecvFrame = frameId;
                                        _clientInfoDic[pos].frameInputDic[frameId] = input;
                                        
                                        // lock (_recvQueue)
                                        // {
                                        //     _recvQueue.Enqueue(packet);
                                        // }
                                        // using (var stream = new MemoryStream())
                                        // {
                                        //     using (var writer = new BinaryWriter(stream))
                                        //     {
                                        //         writer.Write(frameId);
                                        //         writer.Write((byte)1);
                                        //         writer.Write(input);
                                        //         
                                        //         var len = (int)stream.Position;
                                        //         var buffer = BufferPool.GetBuffer(len);
                                        //         stream.Seek(0, SeekOrigin.Begin);
                                        //         stream.Read(buffer, 0, len);
                                        //     
                                        //         SendDataToClient(buffer, packet.Head.cmd, packet.Head.act, len);
                                        //     }
                                        // }
                                    }
                                }
                            }
                        }
                    }
                    catch (SocketException ex)
                    {
                        Logger.Log(LogLevel.Exception, $"[接受数据] 错误码: {ex.ErrorCode} 错误信息: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Exception, $"[接受数据] 错误信息: {ex.Message}");
                    }
                }

                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// 发送数据给服务器
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="cmd">命令</param>
        /// <param name="act">动作</param>
        /// <param name="length">数据长度</param>
        private static void SendDataToClient(byte[] data, int cmd, int act, int length)
        {
            lock (_sendLock)
            {
                var packet = new Packet();
                var head = new Head
                {
                    cmd = (byte)cmd,
                    act = (byte)act,
                    length = (uint)length
                };
                packet.Head = head;
                packet.Data = data;

                _sendQueue.Enqueue(packet);
            }
        }
    }
}