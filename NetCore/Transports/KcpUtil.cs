using kcp2k;

/// <summary>
/// KCP服务器支持
/// </summary>
public static class KcpUtil 
{
    /// <summary>
    /// KCP服务器配置
    /// </summary>
    public static readonly KcpConfig DefaultConfig = new KcpConfig(
        DualMode: true,
        NoDelay: true,
        Interval: 1,
        FastResend: 2,
        CongestionWindow: false,
        SendWindowSize: Kcp.WND_SND,
        ReceiveWindowSize: Kcp.WND_RCV,
        Timeout: 10000,
        MaxRetransmits: Kcp.DEADLINK * 2
    );

    public static int FromKcpChannel(KcpChannel channel)
    {
        return channel == KcpChannel.Reliable ? (int)KcpChannel.Reliable : (int)KcpChannel.Unreliable;
    }

    public static KcpChannel ToKcpChannel(int channel)
    {
        return channel == (int)KcpChannel.Reliable ? KcpChannel.Reliable : KcpChannel.Unreliable;
    }
    
}