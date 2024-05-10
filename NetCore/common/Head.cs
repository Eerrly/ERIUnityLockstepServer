using System.Runtime.InteropServices;

/// <summary>
/// 消息头
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Head
{
    /// <summary>
    /// CMD
    /// </summary>
    public byte _cmd;
    /// <summary>
    /// 消息体长度
    /// </summary>
    public int _length;
    /// <summary>
    /// 头长度
    /// </summary>
    public static readonly int HeadLength = 5;
}