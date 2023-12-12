using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ERIUnitySimpleServer
{
    class Program
    {
        private static Socket socket;
        private static EndPoint endPoint;
        
        private static byte[] tmpRecvBuffer = new byte[1024];
        private static byte[] recvBuffer = new byte[256];
        private static Queue<Packet> recvQueue = new Queue<Packet>();
        
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
        }

        static void Main(string[] args)
        {
            InitLogger();
            InitUDP();
            
            var tReceive = new Thread(RecvThreadMethod);
            tReceive.Start();
        }

        /// <summary>
        /// 接受数据线程
        /// </summary>
        private static void RecvThreadMethod()
        {
            while (socket != null)
            {
                EndPoint clientPoint = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    if (socket.Available > 0 && socket.ReceiveFrom(tmpRecvBuffer, 0, tmpRecvBuffer.Length, SocketFlags.None, ref clientPoint) > 0)
                    {
                        Array.Copy(tmpRecvBuffer, 24, recvBuffer, 0, recvBuffer.Length);
                        lock (recvQueue)
                        {
                            var packet = new Packet();
                            unsafe
                            {
                                fixed (byte* dest = recvBuffer)
                                {
                                    packet.Head = *(Head*)dest;
                                }
                            }
                            packet.Data = BufferPool.GetBuffer((int)packet.Head.length);
                            Array.Copy(recvBuffer, Head.Length, packet.Data, 0, packet.Head.length);

                            recvQueue.Enqueue(packet);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(LogLevel.Exception, $"[接受数据 异常] 错误信息: {ex.Message}");
                }
                Thread.Sleep(1);
            }
        }
    }
}
