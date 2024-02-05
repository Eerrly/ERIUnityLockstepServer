using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Head
{
    public byte _cmd;
    public int _length;
    
    public static readonly int HeadLength = 5;
}