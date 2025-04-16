using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Head
{
    /// <summary>
    /// 消息命令类型
    /// </summary>
    public byte _cmd;
    /// <summary>
    /// 消息体长度
    /// </summary>
    public int _length;

    public static readonly int HeadLength = 5;
}