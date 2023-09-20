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
    enum ACT
    {
        HEARTBEAT,
        DATA,
        JOIN,
    }

    public struct Head
    {
        public int size;
        public byte act;
        public short index;

        public static readonly int Length = 8;
        public static readonly int EndPointLength = 16;
    }

    public struct Packet
    {
        public Head head;
        public byte[] data;
    }

    class Program
    {
        private static readonly int MaxClientCount = 1;

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

        private static readonly byte posMask = 0x01;
        private static readonly byte yawMask = 0xF0;
        private static readonly byte keyMask = 0x0E;

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

        private static void InitLogger()
        {
            Logger.Initialize(Path.Combine(Directory.GetCurrentDirectory(), "server.log"), new Logger());
            Logger.SetLoggerLevel((int)LogLevel.Info | (int)LogLevel.Error | (int)LogLevel.Exception);
            Logger.log = Console.WriteLine;
            Logger.logError = Console.WriteLine;
        }

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
                            HeartBeatMethod(packet, clientEndPoint);
                            break;
                        case ACT.DATA:
                            DataMethod(packet, clientEndPoint);
                            break;
                        case ACT.JOIN:
                            JoinMethod(packet, clientEndPoint);
                            break;
                    }
                }
            }

            LateUpdate();
        }

        private static void LateUpdate()
        {
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

        private static void HeartBeatMethod(Packet packet, EndPoint clientEndPoint)
        {
            Logger.Log(LogLevel.Info, $"\n【客户端】 IP地址: {clientEndPoint} 服务器帧:{currentFrame} 行为:{Enum.GetName(typeof(ACT), packet.head.act)}");

            frameDic[clientEndPoint] = currentFrame;
            BufferPool.ReleaseBuff(packet.data);
        }

        private static void DataMethod(Packet packet, EndPoint clientEndPoint)
        {
            var realData = BufferPool.GetBuffer(packet.head.size);
            Array.Copy(packet.data, Head.EndPointLength, realData, 0, packet.head.size);
            using (MemoryStream stream = new MemoryStream(realData))
            {
                BinaryReader reader = new BinaryReader(stream);
                var raw = reader.ReadByte();
                var frame = reader.ReadInt32();

                Logger.Log(LogLevel.Info, $"\n【客户端】 IP地址: {clientEndPoint} 服务器帧:{currentFrame} 行为:{Enum.GetName(typeof(ACT), packet.head.act)} 序号:{packet.head.index} 数据:\n" +
                    $"[Frame:{frame} Pos:{(byte)(posMask & raw)} Yaw:{(byte)((yawMask & raw) >> 4)} Key:{(byte)((keyMask & raw) >> 1)}]");
            }
            BufferPool.ReleaseBuff(realData);
            lock (sendQueue)
            {
                sendQueue.Enqueue(packet);
            }
        }

        private static void JoinMethod(Packet packet, EndPoint clientEndPoint)
        {
            var realData = BufferPool.GetBuffer(packet.head.size);
            Array.Copy(packet.data, Head.EndPointLength, realData, 0, packet.head.size);
            Logger.Log(LogLevel.Info, $"\n【客户端】 IP地址: {clientEndPoint} 服务器帧:{currentFrame} 行为:{Enum.GetName(typeof(ACT), packet.head.act)}");
            joinDic[clientEndPoint] = 1;

            if (joinDic.Count >= MaxClientCount && !joinDic.Any((v) => { return v.Value == 0; }))
            {
                lock (sendQueue)
                {
                    sendQueue.Enqueue(packet);
                }
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
