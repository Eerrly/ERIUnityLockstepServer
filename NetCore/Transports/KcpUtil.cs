using kcp2k;

/// <summary>
/// KCP帮助类
/// </summary>
public static class KcpUtil 
{
    /// <summary>
    /// KCP静态配置
    /// </summary>
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

    /// <summary>
    /// 将KCP消息类型转为Int值
    /// </summary>
    /// <param name="channel">KCP消息类型</param>
    /// <returns>Int值</returns>
    public static int FromKcpChannel(KcpChannel channel)
    {
        return channel == KcpChannel.Reliable ? (int)KcpChannel.Reliable : (int)KcpChannel.Unreliable;
    }

    /// <summary>
    /// 将Int值转为KCP消息类型
    /// </summary>
    /// <param name="channel">Int值</param>
    /// <returns>KCP消息类型</returns>
    public static KcpChannel ToKcpChannel(int channel)
    {
        return channel == (int)KcpChannel.Reliable ? KcpChannel.Reliable : KcpChannel.Unreliable;
    }
    
}