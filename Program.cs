// 总入口
GameManager.Instance.Initialize();
MsgPoolManager.Instance.Initialize();
NetworkManager.Instance.Initialize();

LogManager.Instance.LogFilePath = "E:\\GitProjects\\ERIUnitySimpleServer\\server_log.txt";
LogManager.Instance.Initialize();

NetworkManager.Instance.TcpStart();

LogManager.Instance.Log(LogTag.Info,$"{NetworkManager.Instance.TcpUri} {NetworkManager.Instance.KcpUri}");
Console.ReadLine();