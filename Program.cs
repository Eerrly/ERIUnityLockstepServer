using System;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ERIUnitySimpleServer
{
    static class Constant
    {
        public static readonly string IP = "127.0.0.1";

        public static readonly int Port = 10086;

        public static readonly int FrameInterval = 100;

        public static readonly int RecvTimeOut = 10000;

        public static readonly int RecvTimeOutFrame = RecvTimeOut / FrameInterval;
    }

    enum ACT
    {
        HEARTBEAT,
        DATA,
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
        private static Socket socket;
        private static Queue<Packet> sendQueue = new Queue<Packet>();
        private static Queue<Packet> recvQueue = new Queue<Packet>();
        private static byte[] sendBuffer;
        private static byte[] recvBuffer = new byte[1024];
        private static EndPoint endPoint;
        private static Dictionary<EndPoint, byte[]> clientPointList = new Dictionary<EndPoint, byte[]>();
        private static Dictionary<EndPoint, int> frameList = new Dictionary<EndPoint, int>();
        private static List<EndPoint> disconnectEndPoints = new List<EndPoint>();
        private static int currentFrame = 0;

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
            endPoint = new IPEndPoint(IPAddress.Parse(Constant.IP), Constant.Port);
            socket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.ReceiveTimeout = Constant.RecvTimeOut;

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
                Console.WriteLine(ex.Message);
            }
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
                        foreach (var pointInfo in clientPointList)
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
                            if (!clientPointList.TryGetValue(clientPoint, out clientEndPointBytes))
                            {
                                clientEndPointBytes = EndPointToBytes(clientPoint);
                                clientPointList.Add(clientPoint, clientEndPointBytes);
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
                    Console.WriteLine($"【异常】 {ex.Message}");
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
                            {
                                Console.WriteLine($"【客户端】帧:{currentFrame} IP地址:{clientEndPoint} 类型:{Enum.GetName(typeof(ACT), packet.head.act)}");
                                frameList[clientEndPoint] = currentFrame;
                                BufferPool.ReleaseBuff(packet.data);
                            }
                            break;
                        case ACT.DATA:
                            {
                                var message = new byte[(int)packet.head.size];
                                Array.Copy(packet.data, Head.EndPointLength, message, 0, packet.head.size);
                                Console.WriteLine($"【客户端】帧:{currentFrame} IP地址:{clientEndPoint} 类型:{Enum.GetName(typeof(ACT), packet.head.act)} 序号:{packet.head.index} 数据:{Encoding.UTF8.GetString(message)}");
                                lock (sendQueue)
                                {
                                    sendQueue.Enqueue(packet);
                                }
                            }
                            break;
                    }
                }
            }
            foreach (var item in frameList)
            {
                if (currentFrame - item.Value >= Constant.RecvTimeOutFrame)
                {
                    disconnectEndPoints.Add(item.Key);
                }
            }
            foreach (var item in disconnectEndPoints)
            {
                Console.WriteLine($"【客户端】 IP地址: {item} 断开连接！");
                clientPointList.Remove(item);
                frameList.Remove(item);
            }
            if (disconnectEndPoints.Count > 0)
            {
                disconnectEndPoints.Clear();
            }
        }

        static void Main(string[] args)
        {
            BufferPool.InitPool(32, 1024, 5, 5);
            InitUDP();

            Thread sendThread = new Thread(SendThreadMethod);
            Thread recvThread = new Thread(RecvThreadMethod);
            Timer timer = new Timer(Update, null, 0, Constant.FrameInterval);

            sendThread.Start();
            recvThread.Start();
        }
    }
}
