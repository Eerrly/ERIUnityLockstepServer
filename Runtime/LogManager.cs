
using System.Text;

public static class LogTag
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Exception = "Exception";
}

public class LogManager : AManager<LogManager>
{
    public string LogFilePath;
    
    private static readonly object _lock = new object();
    private Thread _loggingThread;
    private StringBuilder _logBuffer;
    private bool _isRunning;
    
    public override void Initialize()
    {
        File.WriteAllText(LogFilePath, "");
        
        _logBuffer = new StringBuilder();
        _isRunning = true;
        _loggingThread = new Thread(new ThreadStart(WriteLogsToFile))
        {
            IsBackground = true
        };
        _loggingThread.Start();
    }

    public void Log(string tag, string msg)
    {
        var message = $"{DateTime.Now} [{tag}]: {msg}";
        lock (_lock)
        {
            _logBuffer.AppendLine(message);
        }
        Console.WriteLine(message);
    }

    private void WriteLogsToFile()
    {
        while (_isRunning)
        {
            lock (_lock)
            {
                if (_logBuffer.Length > 0)
                {
                    File.AppendAllText(LogFilePath, _logBuffer.ToString());
                    _logBuffer.Clear();
                }
            }
            Thread.Sleep(1000);
        }
        
        // 确保在退出前将所有日志写入文件
        lock (_lock)
        {
            if (_logBuffer.Length > 0)
            {
                File.AppendAllText(LogFilePath, _logBuffer.ToString());
                _logBuffer.Clear();
            }
        }
    }

    public override void OnRelease()
    {
        _isRunning = false;
        _loggingThread.Join();
    }
}