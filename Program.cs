// See https://aka.ms/new-console-template for more information

GameManager.Instance.Initialize();
NetworkManager.Instance.Initialize();
NetworkManager.Instance.TcpStart();

System.Console.WriteLine($"[{TimeUtil.DateTimeNowToString()}] ServerUri: {NetworkManager.Instance.KcpUri}");
Console.ReadLine();