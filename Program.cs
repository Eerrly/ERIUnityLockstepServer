// 总入口
GameManager.Instance.Initialize();
MsgPoolManager.Instance.Initialize();
NetworkManager.Instance.Initialize();
LogManager.Instance.Initialize(Path.Combine(Environment.CurrentDirectory, "server_log.txt"));

NetworkManager.Instance.TcpStart();

LogManager.Instance.Log(LogType.Info,$"{NetworkManager.Instance.TcpUri} {NetworkManager.Instance.KcpUri}");
Console.ReadLine();
