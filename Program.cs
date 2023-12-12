using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ERIUnitySimpleServer
{
    public class CacheUnit
    {
        public byte[] data;
        public int length;
    }
    
    class Program
    {
        private static KCP _kcp;
        private static Socket socket;
        private static EndPoint endPoint;
        
        private static ushort _index = 0;
        private static byte[] _sendBuffer = new byte[Head.MaxSendLength];
        private static byte[] _recvBuffer = new byte[Head.MaxRecvLength];
        private static EndPoint clientPoint;
        
        private static Queue<Packet> _sendQueue = new Queue<Packet>();
        private static Queue<Packet> recvQueue = new Queue<Packet>();

        private static object _sendLock = new object();
        private static Queue<CacheUnit> _finalSendQueue = new Queue<CacheUnit>();
        
        private static readonly DateTime m_UtcTime = new DateTime(1970, 1, 1);
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
            endPoint = new IPEndPoint(IPAddress.Parse(NetConstant.IP), NetConstant.Port);
            socket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = NetConstant.FrameInterval * 10;

            uint IOC_IN = 0x80000000;
            uint IOC_VENDOR = 0x18000000;
            uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            socket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
            try
            {
                socket.Bind(endPoint);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Exception, $"[初始化UDP服务器 异常] 错误信息: {ex.Message}");
            }
            try
            {
                Kcp_Init(0);
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Exception, $"[初始化KCP服务器 异常] 错误信息: {ex.Message}");
            }
        }
        
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
        
        private static void Kcp_Send(byte[] data, int size)
        {
            var unit = new CacheUnit
            {
                data = BufferPool.GetBuffer(size),
                length = size
            };
            Array.Copy(data, unit.data, size);

            lock (_sendLock)
            {
                _finalSendQueue.Enqueue(unit);
            }
        }

        static void Main(string[] args)
        {
            InitLogger();
            InitUDP();
            
            var thread = new Thread(UpdateThreadMethod);
            thread.Start();
        }

        private static int _usedRecvBufferSize = 0;
        /// <summary>
        /// 接受数据线程
        /// </summary>
        private static void UpdateThreadMethod()
        {
            while (socket != null && _kcp != null)
            {
                lock (_sendLock)
                {
                    while (_sendQueue.Count > 0 && _kcp != null && _kcp.WaitSnd() < 512)
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
                        socket.SendTo(unit.data, 0, unit.length, SocketFlags.None, clientPoint);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Exception, $"发送数据发生异常 错误:{ex.Message}");
                    }
                    finally
                    {
                        if (unit != null)
                        {
                            BufferPool.ReleaseBuff(unit.data);
                            unit = null;
                        }
                    }
                }

                _kcp.Update(iclock(), needFlush);
                
                var size = Head.MaxRecvLength - _usedRecvBufferSize;
                if (size > 0)
                {
                    clientPoint = new IPEndPoint(IPAddress.Any, 0);
                    try
                    {
                        var length = socket.ReceiveFrom(_recvBuffer, 0, _recvBuffer.Length, SocketFlags.None, ref clientPoint);
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

                                    packet.RecvTime = Time;

                                    recvQueue.Enqueue(packet);
                                }
                            }
                        }
                    }
                    catch (SocketException ex)
                    {
                        Logger.Log(LogLevel.Exception, $"[接受数据 异常] 错误码: {ex.ErrorCode} 错误信息: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Exception, $"[接受数据 异常] 错误信息: {ex.Message}");
                    }
                }
                Thread.Sleep(1);
            }
        }
    }
}
