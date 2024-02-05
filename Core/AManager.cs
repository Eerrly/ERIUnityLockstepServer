public class AManager<T> : IManager where T:new()
{
    private static T _instance;

    /// <summary>
    /// 静态单例
    /// </summary>
    public static T Instance => _instance ?? (_instance = new T());
    
    /// <summary>
    /// 初始化
    /// </summary>
    public virtual void Initialize() { }

    /// <summary>
    /// 释放
    /// </summary>
    public virtual void OnRelease() { }
    
}