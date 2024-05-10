/// <summary>
/// 管理器父类
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class AManager<T> : IManager where T:new()
{
    /// <summary>
    /// 实例
    /// </summary>
    private static T _instance;

    /// <summary>
    /// 管理器实例
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
    /// 初始化
    /// </summary>
    public virtual void Initialize() { }

    /// <summary>
    /// 释放
    /// </summary>
    public virtual void OnRelease() { }
    
}