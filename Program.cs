// 总入口
var logPath = Path.Combine(AppContext.BaseDirectory, "server_log.txt");

GameManager.Instance.Initialize();
MsgPoolManager.Instance.Initialize();
NetworkManager.Instance.Initialize();
LogManager.Instance.Initialize(logPath);

NetworkManager.Instance.TcpStart();

LogManager.Instance.Log(LogType.Info,$"{NetworkManager.Instance.TcpUri} {NetworkManager.Instance.KcpUri}");
Console.ReadLine();
