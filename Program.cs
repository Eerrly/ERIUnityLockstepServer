using System;
using System.IO;

namespace ERIUnitySimpleServer
{
    class Program
    {
        private static void InitLogger()
        {
            Logger.Initialize(Path.Combine(Directory.GetCurrentDirectory(), "server.log"), new Logger());
            Logger.SetLoggerLevel((int)LogLevel.Info | (int)LogLevel.Error | (int)LogLevel.Exception);
            Logger.log = Console.WriteLine;
            Logger.logError = Console.WriteLine;
        }

        static void Main(string[] args)
        {
            InitLogger();
            BufferPool.InitPool(32, 1024, 5, 5);
            NetController.Initialize();
        }
    }
}
