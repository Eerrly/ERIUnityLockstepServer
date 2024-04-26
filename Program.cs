// See https://aka.ms/new-console-template for more information

GameManager.Instance.Initialize();
MsgPoolManager.Instance.Initialize();
NetworkManager.Instance.Initialize();
NetworkManager.Instance.TcpStart();

System.Console.WriteLine($"[{TimeUtil.DateTimeNowToString()}] ServerTcpUri: {NetworkManager.Instance.TcpUri} ServerKcpUri: {NetworkManager.Instance.KcpUri}");
Console.ReadLine();