/// <summary>
/// 消息包
/// </summary>
public struct Packet
{
    /// <summary>
    /// 消息头
    /// </summary>
    public Head _head;
    /// <summary>
    /// 消息体
    /// </summary>
    public byte[] _data;
}