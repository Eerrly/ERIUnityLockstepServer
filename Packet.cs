namespace ERIUnitySimpleServer
{
    public enum ACT
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
}
