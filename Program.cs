using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Linq;

namespace ERIUnitySimpleServer
{
    /// <summary>
    /// 行为
    /// </summary>
    enum ACT
    {
        HEARTBEAT,
        DATA,
        JOIN,
    }

    /// <summary>
    /// 头数据
    /// </summary>
    public struct Head
    {
        public int size;
        public byte act;
        public short index;

        public static readonly int Length = 8;
        public static readonly int EndPointLength = 16;
    }

    /// <summary>
    /// 包体
    /// </summary>
    public struct Packet
    {
        public Head head;
        public byte[] data;
    }

    class Program
    {
        private static readonly int MaxClientCount = 2;

        private static Socket socket;
        private static Queue<Packet> sendQueue = new Queue<Packet>();
        private static Queue<Packet> recvQueue = new Queue<Packet>();
        private static byte[] sendBuffer;
        private static byte[] recvBuffer = new byte[1024];
        private static EndPoint endPoint;
        private static Dictionary<EndPoint, byte[]> clientPointDic = new Dictionary<EndPoint, byte[]>(MaxClientCount);
        private static Dictionary<EndPoint, int> frameDic = new Dictionary<EndPoint, int>(MaxClientCount);
        private static Dictionary<EndPoint, int> joinDic = new Dictionary<EndPoint, int>(MaxClientCount);
        private static List<EndPoint> disconnectEndPoints = new List<EndPoint>();
        private static int currentFrame = 0;

        /// <summary>
        /// EndPoint转字节数组
        /// </summary>
        /// <param name="clientEndPoint"></param>
        /// <returns></returns>
        private static byte[] EndPointToBytes(EndPoint clientEndPoint)
        {
            SocketAddress socketAddress = clientEndPoint.Serialize();
            byte[] buffer = BufferPool.GetBuffer(socketAddress.Size);
            for (int j = 0; j < socketAddress.Size; j++)
            {
                buffer[j] = socketAddress[j];
            }
            return buffer;
        }

        /// <summary>
        /// 字节数组转EndPoint
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private static EndPoint BytesToEndPoint(byte[] data)
        {
            EndPoint clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
            SocketAddress socketAddress = new SocketAddress(clientEndPoint.AddressFamily);
            for (int i = 0; i < Head.EndPointLength; i++)
            {
                socketAddress[i] = data[i];
            }
            return clientEndPoint.Create(socketAddress);
        }

        /// <summary>
        /// 初始化UDP
        /// </summary>
        private static void InitUDP()
        {
            endPoint = new IPEndPoint(IPAddress.Parse(NetConstant.IP), NetConstant.Port);
            socket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = NetConstant.RecvTimeOut;

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
                Logger.Log(LogLevel.Exception, $"【异常】 {ex.Message}");
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
        /// 发送数据线程
        /// </summary>
        private static void SendThreadMethod()
        {
            while (socket != null)
            {
                lock (sendQueue)
                {
                    if(sendQueue.Count > 0)
                    {
                        var packetInfo = sendQueue.Dequeue();
                        foreach (var pointInfo in clientPointDic)
                        {
                            sendBuffer = BufferPool.GetBuffer(Head.Length + Head.EndPointLength + packetInfo.head.size);
                            unsafe
                            {
                                fixed (byte* dest = sendBuffer)
                                {
                                    *(Head*)dest = packetInfo.head;
                                }
                            }
                            Array.Copy(packetInfo.data, 0, sendBuffer, Head.Length, Head.EndPointLength + packetInfo.head.size);
                            socket.SendTo(sendBuffer, 0, sendBuffer.Length, SocketFlags.None, pointInfo.Key);

                            BufferPool.ReleaseBuff(sendBuffer);
                        }
                        BufferPool.ReleaseBuff(packetInfo.data);
                    }
                }
                Thread.Sleep(1);
            }
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
                    if (socket.ReceiveFrom(recvBuffer, 0, recvBuffer.Length, SocketFlags.None, ref clientPoint) > 0)
                    {
                        lock (recvQueue)
                        {
                            byte[] clientEndPointBytes;
                            if (!clientPointDic.TryGetValue(clientPoint, out clientEndPointBytes))
                            {
                                clientEndPointBytes = EndPointToBytes(clientPoint);
                                clientPointDic.Add(clientPoint, clientEndPointBytes);
                            }
                            Packet packet = new Packet();
                            unsafe
                            {
                                fixed (byte* dest = recvBuffer)
                                {
                                    packet.head = *(Head*)dest;
                                }
                            }
                            packet.data = BufferPool.GetBuffer(Head.EndPointLength + packet.head.size);
                            Array.Copy(clientEndPointBytes, 0, packet.data, 0, Head.EndPointLength);
                            Array.Copy(recvBuffer, Head.Length, packet.data, Head.EndPointLength, packet.head.size);

                            recvQueue.Enqueue(packet);
                        }
                    }
                }
                catch(Exception ex)
                {
                    Logger.Log(LogLevel.Exception, $"【异常】 {ex.Message}");
                }
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// 固定帧轮询
        /// </summary>
        private static void Update(object state)
        {
            currentFrame += 1;
            lock (recvQueue)
            {
                while (recvQueue.Count > 0)
                {
                    Packet packet = recvQueue.Dequeue();
                    var clientEndPoint = BytesToEndPoint(packet.data);
                    switch ((ACT)packet.head.act)
                    {
                        case ACT.HEARTBEAT:
                            {
                                Logger.Log(LogLevel.Info, $"【客户端】帧:{currentFrame} IP地址:{clientEndPoint} 类型:{Enum.GetName(typeof(ACT), packet.head.act)}");
                                frameDic[clientEndPoint] = currentFrame;
                                BufferPool.ReleaseBuff(packet.data);
                            }
                            break;
                        case ACT.DATA:
                            {
                                var realData = BufferPool.GetBuffer(packet.head.size);
                                Array.Copy(packet.data, Head.EndPointLength, realData, 0, packet.head.size);
                                Logger.Log(LogLevel.Info, $"【客户端】帧:{currentFrame} IP地址:{clientEndPoint} 类型:{Enum.GetName(typeof(ACT), packet.head.act)} 序号:{packet.head.index} 数据:{Encoding.UTF8.GetString(realData)}");
                                BufferPool.ReleaseBuff(realData);
                                lock (sendQueue)
                                {
                                    sendQueue.Enqueue(packet);
                                }
                            }
                            break;
                        case ACT.JOIN:
                            {
                                var realData = BufferPool.GetBuffer(packet.head.size);
                                Array.Copy(packet.data, Head.EndPointLength, realData, 0, packet.head.size);
                                var joinState = BitConverter.ToInt32(realData, 0);
                                Logger.Log(LogLevel.Info, $"【客户端】帧:{currentFrame} IP地址:{clientEndPoint} 类型:{Enum.GetName(typeof(ACT), packet.head.act)} 准备状态:{joinState}");
                                joinDic[clientEndPoint] = joinState;
                                
                                if(joinDic.Count >= MaxClientCount && !joinDic.Any((v) => { return v.Value == 0; }))
                                {
                                    lock (sendQueue)
                                    {
                                        sendQueue.Enqueue(packet);
                                    }
                                }
                            }
                            break;
                    }
                }
            }
            foreach (var item in frameDic)
            {
                if (currentFrame - item.Value >= NetConstant.RecvTimeOutFrame)
                {
                    disconnectEndPoints.Add(item.Key);
                }
            }
            foreach (var item in disconnectEndPoints)
            {
                Logger.Log(LogLevel.Info, $"【客户端】 IP地址: {item} 断开连接！");
                clientPointDic.Remove(item);
                frameDic.Remove(item);
                joinDic.Remove(item);
            }
            if (disconnectEndPoints.Count > 0)
            {
                disconnectEndPoints.Clear();
            }
        }

        static void Main(string[] args)
        {
            BufferPool.InitPool(32, 1024, 5, 5);

            InitLogger();
            InitUDP();

            Thread sendThread = new Thread(SendThreadMethod);
            Thread recvThread = new Thread(RecvThreadMethod);
            Timer timer = new Timer(Update, null, 0, NetConstant.FrameInterval);

            sendThread.Start();
            recvThread.Start();
        }
    }
}
