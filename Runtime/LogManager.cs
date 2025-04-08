// #define DEBUG_MODEL

using System.Text;

/// <summary>
/// 日志类型
/// </summary>
public static class LogType
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Exception = "Exception";
}

/// <summary>
/// 日志管理器
/// </summary>
public class LogManager : AManager<LogManager>
{
    private static readonly object _lock = new object();
    private Thread _loggingThread;
    private StringBuilder _logBuffer;
    private bool _isRunning;
    private string _logFilePath;
    
    public override void Initialize(params object[] objs)
    {
        _logFilePath = (string)objs[0];
        _logBuffer = new StringBuilder();
        _isRunning = true;
        File.WriteAllText(_logFilePath, "");
        _loggingThread = new Thread(new ThreadStart(WriteLogsToFile))
        {
            IsBackground = true
        };
        _loggingThread.Start();
    }

    /// <summary>
    /// 日志输入
    /// </summary>
    /// <param name="tag">日志类型</param>
    /// <param name="msg">日志</param>
    public void Log(string tag, string msg)
    {
        var message = $"{DateTime.Now} [{tag}]: {msg}";
        lock (_lock)
        {
            _logBuffer.AppendLine(message);
        }
#if !DEBUG_MODEL
        if (tag.Equals(LogType.Info) || tag.Equals(LogType.Warning))
            return;
#endif
        Console.WriteLine(message);
    }

    /// <summary>
    /// 将日志输入写到文本中
    /// </summary>
    private void WriteLogsToFile()
    {
        while (_isRunning)
        {
            lock (_lock)
            {
                if (_logBuffer.Length > 0)
                {
                    File.AppendAllText(_logFilePath, _logBuffer.ToString());
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
                File.AppendAllText(_logFilePath, _logBuffer.ToString());
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