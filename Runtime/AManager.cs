/// <summary>
/// 管理器单例基类
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class AManager<T> : IManager where T:new()
{
    private static T _instance;
    
    /// <summary>
    /// 单例
    /// </summary>
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new T();
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// 管理器初始化
    /// </summary>
    public virtual void Initialize() { }
    
    /// <summary>
    /// 管理器释放
    /// </summary>
    public virtual void OnRelease() { }
    
}