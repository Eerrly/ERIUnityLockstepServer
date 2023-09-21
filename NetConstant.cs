namespace ERIUnitySimpleServer
{
    class NetConstant
    {
        public static readonly string IP = "127.0.0.1";

        public static readonly int Port = 10086;

        public static readonly int FrameInterval = 33;

        public static readonly int RecvTimeOut = 3300;

        public static readonly int RecvTimeOutFrame = RecvTimeOut / FrameInterval;
    }
}
