// 总入口
GameManager.Instance.Initialize();
MsgPoolManager.Instance.Initialize();
NetworkManager.Instance.Initialize();
NetworkManager.Instance.TcpStart();

System.Console.WriteLine($"ServerTcpUri: {NetworkManager.Instance.TcpUri} ServerKcpUri: {NetworkManager.Instance.KcpUri}");
Console.ReadLine();