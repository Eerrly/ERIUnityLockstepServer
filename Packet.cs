namespace ERIUnitySimpleServer
{
    /// <summary>
    /// 数据包
    /// </summary>
    public class Packet
    {
        /// <summary>
        /// 协议头
        /// </summary>
        public Head Head;

        /// <summary>
        /// 数据
        /// </summary>
        public byte[] Data;

        /// <summary>
        /// 数据长度
        /// </summary>
        public int Length;
        
        /// <summary>
        /// 接受服务器数据的时间
        /// </summary>
        public long RecvTime;
    }
}