Console.CancelKeyPress += OnCancelKeyPress;

// 启动函数
static void Startup()
{
    GameManager.Instance.Initialize();
    NetworkManager.Instance.Initialize();
    
    GameManager.Instance.StartServer();
}

// 按下停止运行时调用
static void OnCancelKeyPress(object? sender, EventArgs e)
{
    GameManager.Instance.OnRelease();
    NetworkManager.Instance.OnRelease();
    Console.WriteLine(">>> OnProcessExit");
}

Startup();