namespace ERIUnitySimpleServer
{
    public struct Head
    {
        /// <summary>
        /// 数据长度
        /// </summary>
        public uint length;

        /// <summary>
        /// 命令
        /// </summary>
        public byte cmd;

        /// <summary>
        /// 动作
        /// </summary>
        public byte act;

        /// <summary>
        /// 序号
        /// </summary>
        public ushort index;

        #region 静态数据

        /// <summary>
        /// 协议头长度
        /// </summary>
        public static readonly int Length = 8;

        /// <summary>
        /// 最大接收数据长度
        /// </summary>
        public static readonly int MaxRecvLength = 1024 * 1024;

        /// <summary>
        /// 最大发送数据长度
        /// </summary>
        public static readonly int MaxSendLength = 64 * 1024;

        #endregion
    }
}