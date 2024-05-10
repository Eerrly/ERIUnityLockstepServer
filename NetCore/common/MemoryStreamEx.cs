/// <summary>
/// 数据流拓展
/// </summary>
public static class MemoryStreamEx
{
    /// <summary>
    /// 重置数据流
    /// </summary>
    /// <param name="stream">数据流</param>
    public static void Reset(this System.IO.MemoryStream stream)
    {
        stream.Position = 0;
        stream.SetLength(0);
    }
}