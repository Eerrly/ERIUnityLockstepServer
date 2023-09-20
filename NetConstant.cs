namespace ERIUnitySimpleServer
{
    class NetConstant
    {
        public static readonly string IP = "127.0.0.1";

        public static readonly int Port = 10086;

        public static readonly int FrameInterval = 100;

        public static readonly int RecvTimeOut = 10000;

        public static readonly int RecvTimeOutFrame = RecvTimeOut / FrameInterval;
    }
}
