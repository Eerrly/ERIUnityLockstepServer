public static class MemoryStreamEx
{
    public static void Reset(this System.IO.MemoryStream stream)
    {
        stream.Position = 0;
        stream.SetLength(0);
    }
}