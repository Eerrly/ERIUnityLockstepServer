using kcp2k;

public static class KcpUtil 
{
    public static readonly KcpConfig defaultConfig = new KcpConfig(
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