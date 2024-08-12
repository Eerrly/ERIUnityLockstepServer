/// <summary>
/// 管理器接口
/// </summary>
public interface IManager
{
    /// <summary>
    /// 初始化
    /// </summary>
    void Initialize();

    /// <summary>
    /// 释放
    /// </summary>
    void OnRelease();
}