/// <summary>
/// 消息包
/// </summary>
public struct Packet
{
    /// <summary>
    /// 头数据
    /// </summary>
    public Head _head;

    /// <summary>
    /// 数据本体
    /// </summary>
    public byte[] _data;
}