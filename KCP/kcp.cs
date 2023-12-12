using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class KCP
{
    public const int IKCP_RTO_NDL = 30; // no delay min rto
    public const int IKCP_RTO_MIN = 100; // normal min rto
    public const int IKCP_RTO_DEF = 200;
    public const int IKCP_RTO_MAX = 60000;
    public const int IKCP_CMD_PUSH = 81; // cmd: push data
    public const int IKCP_CMD_ACK = 82; // cmd: ack
    public const int IKCP_CMD_WASK = 83; // cmd: window probe (ask)
    public const int IKCP_CMD_WINS = 84; // cmd: window size (tell)
    public const int IKCP_ASK_SEND = 1; // need to send IKCP_CMD_WASK
    public const int IKCP_ASK_TELL = 2; // need to send IKCP_CMD_WINS
    public const int IKCP_WND_SND = 32;
    public const int IKCP_WND_RCV = 32;
    public const int IKCP_MTU_DEF = 1400;
    public const int IKCP_ACK_FAST = 3;
    public const int IKCP_INTERVAL = 100;
    public const int IKCP_OVERHEAD = 24;
    public const int IKCP_DEADLINK = 10;
    public const int IKCP_THRESH_INIT = 2;
    public const int IKCP_THRESH_MIN = 2;
    public const int IKCP_PROBE_INIT = 7000; // 7 secs to probe window size
    public const int IKCP_PROBE_LIMIT = 120000; // up to 120 secs to probe window


    // encode 8 bits unsigned int
    public static int ikcp_encode8u(byte[] p, int offset, byte c)
    {
        p[0 + offset] = c;
        return 1;
    }

    // decode 8 bits unsigned int
    public static int ikcp_decode8u(byte[] p, int offset, ref byte c)
    {
        c = p[0 + offset];
        return 1;
    }

    /* encode 16 bits unsigned int (lsb) */
    public static int ikcp_encode16u(byte[] p, int offset, UInt16 w)
    {
        p[0 + offset] = (byte)(w >> 0);
        p[1 + offset] = (byte)(w >> 8);
        return 2;
    }

    /* decode 16 bits unsigned int (lsb) */
    public static int ikcp_decode16u(byte[] p, int offset, ref UInt16 c)
    {
        UInt16 result = 0;
        result |= (UInt16)p[0 + offset];
        result |= (UInt16)(p[1 + offset] << 8);
        c = result;
        return 2;
    }

    /* encode 32 bits unsigned int (lsb) */
    public static int ikcp_encode32u(byte[] p, int offset, UInt32 l)
    {
        p[0 + offset] = (byte)(l >> 0);
        p[1 + offset] = (byte)(l >> 8);
        p[2 + offset] = (byte)(l >> 16);
        p[3 + offset] = (byte)(l >> 24);
        return 4;
    }

    /* decode 32 bits unsigned int (lsb) */
    public static int ikcp_decode32u(byte[] p, int offset, ref UInt32 c)
    {
        UInt32 result = 0;
        result |= (UInt32)p[0 + offset];
        result |= (UInt32)(p[1 + offset] << 8);
        result |= (UInt32)(p[2 + offset] << 16);
        result |= (UInt32)(p[3 + offset] << 24);
        c = result;
        return 4;
    }

    public static byte[] slice(byte[] p, int start, int stop)
    {
        var bytes = new byte[stop - start];
        Array.Copy(p, start, bytes, 0, bytes.Length);
        return bytes;
    }

    public static T[] slice<T>(T[] p, int start, int stop)
    {
        var arr = new T[stop - start];
        var index = 0;
        for (var i = start; i < stop; i++)
        {
            arr[index] = p[i];
            index++;
        }

        return arr;
    }

    public static byte[] append(byte[] p, byte c)
    {
        var bytes = new byte[p.Length + 1];
        Array.Copy(p, bytes, p.Length);
        bytes[p.Length] = c;
        return bytes;
    }

    public static T[] append<T>(T[] p, T c)
    {
        var arr = new T[p.Length + 1];
        for (var i = 0; i < p.Length; i++)
            arr[i] = p[i];
        arr[p.Length] = c;
        return arr;
    }

    public static T[] append<T>(T[] p, T[] cs)
    {
        var arr = new T[p.Length + cs.Length];
        for (var i = 0; i < p.Length; i++)
            arr[i] = p[i];
        for (var i = 0; i < cs.Length; i++)
            arr[p.Length + i] = cs[i];
        return arr;
    }

    static UInt32 _imin_(UInt32 a, UInt32 b)
    {
        return a <= b ? a : b;
    }

    static UInt32 _imax_(UInt32 a, UInt32 b)
    {
        return a >= b ? a : b;
    }

    static UInt32 _ibound_(UInt32 lower, UInt32 middle, UInt32 upper)
    {
        return _imin_(_imax_(lower, middle), upper);
    }

    static Int32 _itimediff(UInt32 later, UInt32 earlier)
    {
        return ((Int32)(later - earlier));
    }

    // KCP区段定义
    public class Segment
    {
        /// <summary>
        /// conv为一个表示会话编号的整数，和tcp的 conv一样，通信双方需保证conv相同，相互的数据包才能够被认可
        /// </summary>
        public UInt32 conv = 0;
        /// <summary>
        /// cmd用来区分分片的作用。IKCP_CMD_PUSH:数据分片 IKCP_CMD_ACK:ack分片 IKCP_CMD_WASK请求告知窗口大小 IKCP_CMD_WINS:告知窗口大小
        /// </summary>
        public UInt32 cmd = 0;
        /// <summary>
        /// message中的segment分片ID（在message中的索引，由大到小，0表示最后一个分片）
        /// </summary>
        public UInt32 frg = 0;
        /// <summary>
        /// 剩余接收窗口大小(接收窗口大小-接收队列大小)
        /// </summary>
        public UInt32 wnd = 0;
        /// <summary>
        /// message发送时刻的时间戳
        /// </summary>
        public UInt32 ts = 0;
        /// <summary>
        /// message分片segment的序号
        /// </summary>
        public UInt32 sn = 0;
        /// <summary>
        /// 待接收消息序号(接收滑动窗口左端)
        /// </summary>
        public UInt32 una = 0;
        /// <summary>
        /// 下次超时重传的时间戳
        /// </summary>
        public UInt32 resendts = 0;
        /// <summary>
        /// 该分片的超时重传等待时间
        /// </summary>
        public UInt32 rto = 0;
        /// <summary>
        /// 收到ack时计算的该分片被跳过的累计次数
        /// </summary>
        public UInt32 fastack = 0;
        /// <summary>
        /// 发送分片的次数，每发送一次加一
        /// </summary>
        public UInt32 xmit = 0;
        
        public Int32 length = 0;
        public byte[] data;

        public Segment(int size, byte[] buff)
        {
            Reset(size, buff);
        }

        public void Reset(int size, byte[] buff)
        {
            conv = 0;
            cmd = 0;
            frg = 0;
            wnd = 0;
            ts = 0;
            sn = 0;
            una = 0;
            resendts = 0;
            rto = 0;
            fastack = 0;
            xmit = 0;
            length = size;
            data = buff;
        }

        // encode a segment into buffer
        public int encode(byte[] ptr, int offset)
        {
            var offset_ = offset;

            offset += ikcp_encode32u(ptr, offset, conv);
            offset += ikcp_encode8u(ptr, offset, (byte)cmd);
            offset += ikcp_encode8u(ptr, offset, (byte)frg);
            offset += ikcp_encode16u(ptr, offset, (UInt16)wnd);
            offset += ikcp_encode32u(ptr, offset, ts);
            offset += ikcp_encode32u(ptr, offset, sn);
            offset += ikcp_encode32u(ptr, offset, una);
            offset += ikcp_encode32u(ptr, offset, (uint)length);

            return offset - offset_;
        }
    }

    // kcp members.
    /// <summary>
    /// 会话ID
    /// </summary>
    UInt32 conv;
    /// <summary>
    /// 最大传输单元
    /// </summary>
    UInt32 mtu;
    /// <summary>
    /// 最大分片大小
    /// </summary>
    UInt32 mss;
    /// <summary>
    /// 连接状态（0xFFFFFFFF表示断开连接）
    /// </summary>
    UInt32 state;
    /// <summary>
    /// 第一个未确认的包
    /// </summary>
    UInt32 snd_una;
    /// <summary>
    /// 下一个待分配的包的序号
    /// </summary>
    UInt32 snd_nxt;
    /// <summary>
    /// 待接收消息序号
    /// </summary>
    UInt32 rcv_nxt;
    /// <summary>
    /// 拥塞窗口阈值
    /// </summary>
    UInt32 ssthresh;
    /// <summary>
    /// ack接收rtt浮动值
    /// </summary>
    UInt32 rx_rttval;
    /// <summary>
    /// ack接收rtt静态值
    /// </summary>
    UInt32 rx_srtt;
    /// <summary>
    /// 由ack接收延迟计算出来的重传超时时间
    /// </summary>
    UInt32 rx_rto;
    /// <summary>
    /// 最小重传超时时间
    /// </summary>
    UInt32 rx_minrto;
    /// <summary>
    /// 发送窗口大小
    /// </summary>
    UInt32 snd_wnd;
    /// <summary>
    /// 接收窗口大小
    /// </summary>
    UInt32 rcv_wnd;
    /// <summary>
    /// 远端接收窗口大小
    /// </summary>
    UInt32 rmt_wnd;
    /// <summary>
    /// 拥塞窗口大小
    /// </summary>
    UInt32 cwnd;
    /// <summary>
    /// 探查变量，IKCP_ASK_TELL表示告知远端窗口大小。IKCP_ASK_SEND表示请求远端告知窗口大小
    /// </summary>
    UInt32 probe;
    UInt32 current;
    /// <summary>
    /// 内部flush刷新间隔
    /// </summary>
    UInt32 interval;
    /// <summary>
    /// 下次flush刷新时间戳
    /// </summary>
    UInt32 ts_flush;
    UInt32 xmit;
    /// <summary>
    /// 是否启动无延迟模式
    /// </summary>
    UInt32 nodelay;
    /// <summary>
    /// 是否调用过update函数的标识
    /// </summary>
    UInt32 updated;
    /// <summary>
    /// 下次探查窗口的时间戳
    /// </summary>
    UInt32 ts_probe;
    /// <summary>
    /// 探查窗口需要等待的时间
    /// </summary>
    UInt32 probe_wait;
    /// <summary>
    /// 最大重传次数
    /// </summary>
    UInt32 dead_link;
    /// <summary>
    /// 可发送的最大数据量
    /// </summary>
    UInt32 incr;
    /// <summary>
    /// 发送消息的队列
    /// </summary>
    BetterList<Segment> snd_queue = new BetterList<Segment>();
    /// <summary>
    /// 接收消息的队列，有序队列
    /// </summary>
    BetterList<Segment> rcv_queue = new BetterList<Segment>();
    /// <summary>
    /// 发送消息的缓存
    /// </summary>
    BetterList<Segment> snd_buf = new BetterList<Segment>();
    /// <summary>
    /// 接收消息的缓存，无序队列
    /// </summary>
    BetterList<Segment> rcv_buf = new BetterList<Segment>();
    /// <summary>
    /// 待发送的ack列表
    /// </summary>
    BetterList<UInt32> acklist = new BetterList<UInt32>();

    Queue<Segment> segmentCacheList = new Queue<Segment>();
    /// <summary>
    /// 存储消息字节流的内存
    /// </summary>
    byte[] buffer;
    /// <summary>
    /// 触发快速重传的重复ack个数
    /// </summary>
    Int32 fastresend;
    /// <summary>
    /// 取消拥塞控制
    /// </summary>
    Int32 nocwnd;

    /// <summary>
    /// udp发送消息的回调函数 (buffer, size)
    /// </summary>
    Action<byte[], int> output;

    public uint State
    {
        get { return state; }
    }

    // create a new kcp control object, 'conv' must equal in two endpoint
    // from the same connection.
    public KCP(UInt32 conv_, Action<byte[], int> output_)
    {
        Reset(conv_, output_);
    }

    private void Clear(bool clearCache)
    {
        conv = 0;
        mtu = 0;
        mss = 0;
        state = 0;
        snd_una = 0;
        snd_nxt = 0;
        rcv_nxt = 0;
        ssthresh = 0;
        rx_rttval = 0;
        rx_srtt = 0;
        rx_rto = 0;
        rx_minrto = 0;
        snd_wnd = 0;
        rcv_wnd = 0;
        rmt_wnd = 0;
        cwnd = 0;
        probe = 0;
        current = 0;
        interval = 0;
        ts_flush = 0;
        xmit = 0;
        nodelay = 0;
        updated = 0;
        ts_probe = 0;
        probe_wait = 0;
        dead_link = 0;
        incr = 0;

        acklist.Clear();

        fastresend = 0;
        nocwnd = 0;

        foreach (var t in snd_queue)
        {
            DeleteSegment(t);
        }

        snd_queue.Clear();
        foreach (var t in rcv_queue)
        {
            DeleteSegment(t);
        }

        rcv_queue.Clear();
        foreach (var t in snd_buf)
        {
            DeleteSegment(t);
        }

        snd_buf.Clear();
        foreach (var t in rcv_buf)
        {
            DeleteSegment(t);
        }

        rcv_buf.Clear();
        if (clearCache)
        {
            foreach (var t in segmentCacheList)
            {
                if (t.data != _emptyArray)
                {
                    BufferPool.ReleaseBuff(t.data);
                }
            }

            segmentCacheList.Clear();
        }
    }

    public void Reset(UInt32 conv_, Action<byte[], int> output_)
    {
        Clear(false);
        conv = conv_;
        snd_wnd = IKCP_WND_SND;
        rcv_wnd = IKCP_WND_RCV;
        rmt_wnd = IKCP_WND_RCV;
        mtu = IKCP_MTU_DEF;
        mss = mtu - IKCP_OVERHEAD;

        rx_rto = IKCP_RTO_DEF;
        rx_minrto = IKCP_RTO_MIN;
        interval = IKCP_INTERVAL;
        ts_flush = IKCP_INTERVAL;
        ssthresh = IKCP_THRESH_INIT;
        dead_link = IKCP_DEADLINK;
        buffer = new byte[(mtu + IKCP_OVERHEAD) * 3];
        output = output_;
        state = 0;
    }

    ~KCP()
    {
        Clear(true);
    }

    private byte[] _emptyArray = new byte[0];

    public Segment NewSegment(int size)
    {
        Segment segment;
        if (segmentCacheList.Count > 0)
        {
            segment = segmentCacheList.Dequeue();

            if (segment == null)
            {
                Logger.Log(LogLevel.Exception, "kcp new segment == null");
                segment = new Segment(size, size == 0 ? _emptyArray : BufferPool.GetBuffer(size));
            }

            segment.Reset(size, size == 0 ? _emptyArray : BufferPool.GetBuffer(size));
            //UnityEngine.Debug.LogError("new segment from cache " + size);
        }
        else
        {
            segment = new Segment(size, size == 0 ? _emptyArray : BufferPool.GetBuffer(size));
            //UnityEngine.Debug.LogError("new segment from new " + size); 
#if UNITY_EDITOR
            Logger.Log(LogLevel.Info, "new segment " + size);
#endif
        }

        return segment;
    }

    public void DeleteSegment(Segment segment)
    {
        if (segment == null)
        {
            Logger.Log(LogLevel.Exception, "kcp delete segment == null");
            return;
        }

        if (segment.data != null && segment.data != _emptyArray)
        {
            BufferPool.ReleaseBuff(segment.data);
        }

        segment.data = null;
        segmentCacheList.Enqueue(segment);
    }

    // check the size of next message in the recv queue
    public int PeekSize()
    {
        if (0 == rcv_queue.Length)
        {
            return -1;
        }

        var seq = rcv_queue[0];

        if (0 == seq.frg)
        {
            return seq.length;
        }

        if (rcv_queue.Length < seq.frg + 1)
        {
            return -1;
        }

        int length = 0;

        for (int i = 0, count = rcv_queue.Count; i < count; i++)
        {
            var item = rcv_queue[i];
            length += item.length;
            if (0 == item.frg)
                break;
        }

        return length;
    }

    // user/upper level recv: returns size, returns below zero for EAGAIN
    public int Recv(byte[] buffer)
    {
        if (0 == rcv_queue.Length)
        {
            return -1;
        }

        var peekSize = PeekSize();
        if (0 > peekSize)
        {
            return -2;
        }

        if (peekSize > buffer.Length)
        {
            return -3;
        }

        var fast_recover = false;
        if (rcv_queue.Length >= rcv_wnd) fast_recover = true;

        // merge fragment.
        var count = 0;
        var n = 0;

        for (int i = 0; i < rcv_queue.Count; i++)
        {
            var seg = rcv_queue[i];
            Array.Copy(seg.data, 0, buffer, n, seg.length);
            n += seg.length;
            count++;
            if (0 == seg.frg) break;
        }

        if (0 < count)
        {
            for (var i = 0; i < count; ++i)
            {
                DeleteSegment(rcv_queue[i]);
            }

            rcv_queue.Remove(0, count);
        }

        // move available data from rcv_buf -> rcv_queue
        count = 0;
        for (int i = 0; i < rcv_buf.Count; i++)
        {
            var seg = rcv_buf[i];
            if (seg.sn == rcv_nxt && rcv_queue.Length < rcv_wnd)
            {
                rcv_queue.Add(seg);
                rcv_nxt++;
                count++;
            }
            else
            {
                break;
            }
        }

        if (0 < count)
        {
            rcv_buf.Remove(0, count);
        }

        // fast recover
        if (rcv_queue.Length < rcv_wnd && fast_recover)
        {
            // ready to send back IKCP_CMD_WINS in ikcp_flush
            // tell remote my window size
            probe |= IKCP_ASK_TELL;
        }

        return n;
    }

    public int Send(byte[] buffer)
    {
        return Send(buffer, 0, buffer.Length);
    }

    //edit by liange@2018.12.11
    // user/upper level send, returns below zero for error
    public int Send(byte[] buffer, int offset, int length)
    {
        if (0 == length) return -1;

        var count = 0;

        if (length < mss)
            count = 1;
        else
            count = (int)(length + mss - 1) / (int)mss;

        if (255 < count) return -2;

        if (0 == count) count = 1;

        for (var i = 0; i < count; i++)
        {
            var size = 0;
            if (length - offset > mss)
                size = (int)mss;
            else
                size = length - offset;

            var seg = NewSegment(size);
            Array.Copy(buffer, offset, seg.data, 0, size);
            offset += size;
            seg.frg = (UInt32)(count - i - 1);
            snd_queue.Add(seg);
        }

        return 0;
    }

    //---------------------------------------------------------------------
    // parse ack
    //---------------------------------------------------------------------

    // update ack.
    void update_ack(Int32 rtt)
    {
        if (0 == rx_srtt)
        {
            rx_srtt = (UInt32)rtt;
            rx_rttval = (UInt32)rtt / 2;
        }
        else
        {
            Int32 delta = (Int32)((UInt32)rtt - rx_srtt);
            if (0 > delta) delta = -delta;

            rx_rttval = (3 * rx_rttval + (uint)delta) / 4;
            rx_srtt = (UInt32)((7 * rx_srtt + rtt) / 8);
            if (rx_srtt < 1) rx_srtt = 1;
        }

        //edit by liange@2017.9.7 C原版使用的interval，C#版本直接使用的1，现改为同c原版
        //var rto = (int)(rx_srtt + _imax_(1, 4 * rx_rttval));
        var rto = (int)(rx_srtt + _imax_(interval, 4 * rx_rttval));
        rx_rto = _ibound_(rx_minrto, (UInt32)rto, IKCP_RTO_MAX);
    }

    void shrink_buf()
    {
        if (snd_buf.Length > 0)
            snd_una = snd_buf[0].sn;
        else
            snd_una = snd_nxt;
    }

    void parse_ack(UInt32 sn)
    {
        if (_itimediff(sn, snd_una) < 0 || _itimediff(sn, snd_nxt) >= 0)
        {
            return;
        }

        for (int i = 0; i < snd_buf.Count; i++)
        {
            var seg = snd_buf[i];

            if (sn == seg.sn)
            {
                DeleteSegment(snd_buf[i]);
                snd_buf.RemoveAt(i);
                break;
            }

            if (_itimediff(sn, seg.sn) < 0)
            {
                break;
            }
        }
    }

    void parse_una(UInt32 una)
    {
        var count = 0;

        for (int i = 0; i < snd_buf.Count; i++)
        {
            var seg = snd_buf[i];
            if (_itimediff(una, seg.sn) > 0)
                count++;
            else
                break;
        }

        if (0 < count)
        {
            for (var i = 0; i < count; ++i)
            {
                DeleteSegment(snd_buf[i]);
            }

            snd_buf.Remove(0, count);
        }
    }

    //add by liange@2017.9.6
    void parse_fastack(uint sn)
    {
        if (_itimediff(sn, snd_una) < 0 || _itimediff(sn, snd_nxt) >= 0)
            return;

        for (int i = 0; i < snd_buf.Count; i++)
        {
            var seg = snd_buf[i];
            if (_itimediff(sn, seg.sn) < 0)
            {
                break;
            }
            else if (sn != seg.sn)
            {
                seg.fastack++;
            }
        }
    }

    //---------------------------------------------------------------------
    // ack append
    //---------------------------------------------------------------------
    void ack_push(UInt32 sn, UInt32 ts)
    {
        acklist.Add(sn);
        acklist.Add(ts);
    }

    void ack_get(int p, ref UInt32 sn, ref UInt32 ts)
    {
        sn = acklist[p * 2 + 0];
        ts = acklist[p * 2 + 1];
    }

    //---------------------------------------------------------------------
    // parse data
    //---------------------------------------------------------------------
    void parse_data(Segment newseg)
    {
        var sn = newseg.sn;
        if (_itimediff(sn, rcv_nxt + rcv_wnd) >= 0 || _itimediff(sn, rcv_nxt) < 0) return;

        var n = rcv_buf.Length - 1;
        var after_idx = -1;
        var repeat = false;
        for (var i = n; i >= 0; i--)
        {
            var seg = rcv_buf[i];
            if (seg.sn == sn)
            {
                repeat = true;
                break;
            }

            if (_itimediff(sn, seg.sn) > 0)
            {
                after_idx = i;
                break;
            }
        }

        if (!repeat)
        {
            if (after_idx == -1)
            {
                rcv_buf.Insert(0, newseg);
            }

            else
            {
                rcv_buf.Insert(after_idx + 1, newseg);
            }
        }
        else
        {
            DeleteSegment(newseg);
        }

        // move available data from rcv_buf -> rcv_queue
        var count = 0;
        for (int i = 0; i < rcv_buf.Count; i++)
        {
            var seg = rcv_buf[i];
            if (seg.sn == rcv_nxt && rcv_queue.Length < rcv_wnd)
            {
                rcv_queue.Add(seg);
                rcv_nxt++;
                count++;
            }
            else
            {
                break;
            }
        }

        if (0 < count)
        {
            rcv_buf.Remove(0, count);
        }
    }

    public int Input(byte[] data)
    {
        int offset = 0;
        int dataSize = data.Length;

        return Input(data, ref offset, dataSize);
    }

    //edit by liange@2017.9.6
    // when you received a low level packet (eg. UDP packet), call it
    public int Input(byte[] data, ref int offset, int dataSize)
    {
        if (dataSize < IKCP_OVERHEAD)
        {
            return 0; //C原版返回-1
        }

        var s_una = snd_una;
        uint maxack = 0;
        int flag = 0;

        while (true)
        {
            UInt32 ts = 0;
            UInt32 sn = 0;
            UInt32 length = 0;
            UInt32 una = 0;
            UInt32 conv_ = 0;

            UInt16 wnd = 0;

            byte cmd = 0;
            byte frg = 0;

            if (dataSize - offset < IKCP_OVERHEAD)
            {
                break;
            }

            offset += ikcp_decode32u(data, offset, ref conv_);

            if (conv != conv_)
            {
                offset = offset - 4;
                return -1;
            }

            offset += ikcp_decode8u(data, offset, ref cmd);
            offset += ikcp_decode8u(data, offset, ref frg);
            offset += ikcp_decode16u(data, offset, ref wnd);
            offset += ikcp_decode32u(data, offset, ref ts);
            offset += ikcp_decode32u(data, offset, ref sn);
            offset += ikcp_decode32u(data, offset, ref una);
            offset += ikcp_decode32u(data, offset, ref length);

            if (dataSize - offset < length)
            {
                offset = offset - IKCP_OVERHEAD;
                return -2;
            }

            switch (cmd)
            {
                case IKCP_CMD_PUSH:
                case IKCP_CMD_ACK:
                case IKCP_CMD_WASK:
                case IKCP_CMD_WINS:
                    break;
                default:
                    return -3;
            }

            rmt_wnd = (UInt32)wnd;
            parse_una(una);
            shrink_buf();

            if (IKCP_CMD_ACK == cmd)
            {
                if (_itimediff(current, ts) >= 0)
                {
                    update_ack(_itimediff(current, ts));
                }

                parse_ack(sn);
                shrink_buf();

                if (flag == 0)
                {
                    flag = 1;
                    maxack = sn;
                }
                else
                {
                    if (_itimediff(sn, maxack) > 0)
                    {
                        maxack = sn;
                    }
                }
            }
            else if (IKCP_CMD_PUSH == cmd)
            {
                if (_itimediff(sn, rcv_nxt + rcv_wnd) < 0)
                {
                    ack_push(sn, ts);
                    if (_itimediff(sn, rcv_nxt) >= 0)
                    {
                        var seg = NewSegment((int)length);
                        seg.conv = conv_;
                        seg.cmd = (UInt32)cmd;
                        seg.frg = (UInt32)frg;
                        seg.wnd = (UInt32)wnd;
                        seg.ts = ts;
                        seg.sn = sn;
                        seg.una = una;

                        if (length > 0) Array.Copy(data, offset, seg.data, 0, length);

                        parse_data(seg);
                    }
                }
            }
            else if (IKCP_CMD_WASK == cmd)
            {
                // ready to send back IKCP_CMD_WINS in Ikcp_flush
                // tell remote my window size
                probe |= IKCP_ASK_TELL;
            }
            else if (IKCP_CMD_WINS == cmd)
            {
                // do nothing
            }
            else
            {
                return -3;
            }

            offset += (int)length;
        }

        if (flag != 0)
        {
            //C#版没有,C语言版本有此函数
            parse_fastack(maxack);
        }

        if (_itimediff(snd_una, s_una) > 0)
        {
            if (cwnd < rmt_wnd)
            {
                var mss_ = mss;
                if (cwnd < ssthresh)
                {
                    cwnd++;
                    incr += mss_;
                }
                else
                {
                    if (incr < mss_)
                    {
                        incr = mss_;
                    }

                    incr += (mss_ * mss_) / incr + (mss_ / 16);
                    if ((cwnd + 1) * mss_ <= incr) cwnd++;
                }

                if (cwnd > rmt_wnd)
                {
                    cwnd = rmt_wnd;
                    incr = rmt_wnd * mss_;
                }
            }
        }

        return 0;
    }

    Int32 wnd_unused()
    {
        if (rcv_queue.Length < rcv_wnd)
            return (Int32)(int)rcv_wnd - rcv_queue.Length;
        return 0;
    }

    // flush pending data
    public void flush()
    {
        var current_ = current;
        var buffer_ = buffer;
        var change = 0;
        var lost = 0;

        if (0 == updated) return;

        var seg = NewSegment(0);
        seg.conv = conv;
        seg.cmd = IKCP_CMD_ACK;
        seg.wnd = (UInt32)wnd_unused();
        seg.una = rcv_nxt;

        // flush acknowledges
        var count = acklist.Length / 2;
        var offset = 0;
        for (var i = 0; i < count; i++)
        {
            if (offset + IKCP_OVERHEAD > mtu)
            {
                output(buffer, offset);
                //Array.Clear(buffer, 0, offset);
                offset = 0;
            }

            ack_get(i, ref seg.sn, ref seg.ts);
            offset += seg.encode(buffer, offset);
        }

        acklist.Clear();

        // probe window size (if remote window size equals zero)
        if (0 == rmt_wnd)
        {
            if (0 == probe_wait)
            {
                probe_wait = IKCP_PROBE_INIT;
                ts_probe = current + probe_wait;
            }
            else
            {
                if (_itimediff(current, ts_probe) >= 0)
                {
                    if (probe_wait < IKCP_PROBE_INIT)
                        probe_wait = IKCP_PROBE_INIT;
                    probe_wait += probe_wait / 2;
                    if (probe_wait > IKCP_PROBE_LIMIT)
                        probe_wait = IKCP_PROBE_LIMIT;
                    ts_probe = current + probe_wait;
                    probe |= IKCP_ASK_SEND;
                }
            }
        }
        else
        {
            ts_probe = 0;
            probe_wait = 0;
        }

        // flush window probing commands
        if ((probe & IKCP_ASK_SEND) != 0)
        {
            seg.cmd = IKCP_CMD_WASK;
            if (offset + IKCP_OVERHEAD > (int)mtu)
            {
                output(buffer, offset);
                //Array.Clear(buffer, 0, offset);
                offset = 0;
            }

            offset += seg.encode(buffer, offset);
        }

        //edit by liange@2017.9.7日 C#版本中没有，原版有
        // flush window probing commands
        if ((probe & IKCP_ASK_TELL) != 0)
        {
            seg.cmd = IKCP_CMD_WINS;
            if (offset + IKCP_OVERHEAD > (int)mtu)
            {
                output(buffer, offset);
                //Array.Clear(buffer, 0, offset);
                offset = 0;
            }

            offset += seg.encode(buffer, offset);
        }

        probe = 0;

        // calculate window size
        var cwnd_ = _imin_(snd_wnd, rmt_wnd);
        if (0 == nocwnd)
            cwnd_ = _imin_(cwnd, cwnd_);

        count = 0;
        for (var k = 0; k < snd_queue.Length; k++)
        {
            if (_itimediff(snd_nxt, snd_una + cwnd_) >= 0) break;

            var newseg = snd_queue[k];
            newseg.conv = conv;
            newseg.cmd = IKCP_CMD_PUSH;
            newseg.wnd = seg.wnd;
            newseg.ts = current_;
            newseg.sn = snd_nxt;
            newseg.una = rcv_nxt;
            newseg.resendts = current_;
            newseg.rto = rx_rto;
            newseg.fastack = 0;
            newseg.xmit = 0;
            snd_buf.Add(newseg);
            snd_nxt++;
            count++;
        }

        if (0 < count)
        {
            snd_queue.Remove(0, count);
        }

        // calculate resent
        var resent = (UInt32)fastresend;
        if (fastresend <= 0) resent = 0xffffffff;
        var rtomin = rx_rto >> 3;
        if (nodelay != 0) rtomin = 0;

        // flush data segments
        for (int i = 0; i < snd_buf.Count; i++)
        {
            var segment = snd_buf[i];
            var needsend = false;
            var debug = _itimediff(current_, segment.resendts);
            if (0 == segment.xmit) // initial transmit
            {
                needsend = true;
                segment.xmit++;
                segment.rto = rx_rto;
                segment.resendts = current_ + segment.rto + rtomin;
            }
            else if (_itimediff(current_, segment.resendts) >= 0) // RTO
            {
                needsend = true;
                segment.xmit++;
                xmit++;
                if (0 == nodelay)
                    segment.rto += rx_rto;
                else
                    segment.rto += rx_rto / 2;
                segment.resendts = current_ + segment.rto;
                lost = 1;
            }
            else if (segment.fastack >= resent) // fast retransmit
            {
                needsend = true;
                segment.xmit++;
                segment.fastack = 0;
                segment.resendts = current_ + segment.rto;
                change++;
            }

            if (needsend)
            {
                segment.ts = current_;
                segment.wnd = seg.wnd;
                segment.una = rcv_nxt;

                var need = IKCP_OVERHEAD + segment.length;
                if (offset + need > mtu)
                {
                    output(buffer, offset);
                    //Array.Clear(buffer, 0, offset);
                    offset = 0;
                }

                offset += segment.encode(buffer, offset);
                if (segment.length > 0)
                {
                    Array.Copy(segment.data, 0, buffer, offset, segment.length);
                    offset += segment.length;
                }

                if (segment.xmit >= dead_link)
                {
                    //state = 0;
                    //edit by liange@2017.9.7日 C#原版为0，C语言版为-1
                    state = 0xFFFFFFFF;
                }
            }
        }

        // flash remain segments
        if (offset > 0)
        {
            output(buffer, offset);
            //Array.Clear(buffer, 0, offset);
            offset = 0;
        }

        // update ssthresh
        if (change != 0)
        {
            var inflight = snd_nxt - snd_una;
            ssthresh = inflight / 2;
            if (ssthresh < IKCP_THRESH_MIN)
                ssthresh = IKCP_THRESH_MIN;
            cwnd = ssthresh + resent;
            incr = cwnd * mss;
        }

        if (lost != 0)
        {
            ssthresh = cwnd / 2;
            if (ssthresh < IKCP_THRESH_MIN)
                ssthresh = IKCP_THRESH_MIN;
            cwnd = 1;
            incr = mss;
        }

        if (cwnd < 1)
        {
            cwnd = 1;
            incr = mss;
        }

        DeleteSegment(seg);
    }

    // update state (call it repeatedly, every 10ms-100ms), or you can ask
    // ikcp_check when to call it again (without ikcp_input/_send calling).
    // 'current' - current timestamp in millisec.
    public void Update(UInt32 current_, bool flush_)
    {
        current = current_;

        if (0 == updated)
        {
            updated = 1;
            ts_flush = current;
        }

        var slap = _itimediff(current, ts_flush);

        if (slap >= 10000 || slap < -10000)
        {
            ts_flush = current;
            slap = 0;
        }

        if (slap >= 0)
        {
            ts_flush += interval;
            if (_itimediff(current, ts_flush) >= 0)
                ts_flush = current + interval;
            flush();
        }
        else if (flush_)
        {
            flush();
        }
    }

    // Determine when should you invoke ikcp_update:
    // returns when you should invoke ikcp_update in millisec, if there
    // is no ikcp_input/_send calling. you can call ikcp_update in that
    // time, instead of call update repeatly.
    // Important to reduce unnacessary ikcp_update invoking. use it to
    // schedule ikcp_update (eg. implementing an epoll-like mechanism,
    // or optimize ikcp_update when handling massive kcp connections)
    public UInt32 Check(UInt32 current_)
    {
        if (0 == updated) return current_;

        var ts_flush_ = ts_flush;
        var tm_flush_ = 0x7fffffff;
        var tm_packet = 0x7fffffff;
        var minimal = 0;

        if (_itimediff(current_, ts_flush_) >= 10000 || _itimediff(current_, ts_flush_) < -10000)
        {
            ts_flush_ = current_;
        }

        if (_itimediff(current_, ts_flush_) >= 0) return current_;

        tm_flush_ = (int)_itimediff(ts_flush_, current_);

        for (int i = 0; i < snd_buf.Count; i++)
        {
            var seg = snd_buf[i];
            var diff = _itimediff(seg.resendts, current_);
            if (diff <= 0) return current_;
            if (diff < tm_packet) tm_packet = (int)diff;
        }

        minimal = (int)tm_packet;
        if (tm_packet >= tm_flush_) minimal = (int)tm_flush_;
        if (minimal >= interval) minimal = (int)interval;

        return current_ + (UInt32)minimal;
    }

    // change MTU size, default is 1400
    public int SetMtu(Int32 mtu_)
    {
        if (mtu_ < 50 || mtu_ < (Int32)IKCP_OVERHEAD) return -1;

        var buffer_ = new byte[(mtu_ + IKCP_OVERHEAD) * 3];
        if (null == buffer_) return -2;

        mtu = (UInt32)mtu_;
        mss = mtu - IKCP_OVERHEAD;
        buffer = buffer_;
        return 0;
    }

    public int Interval(Int32 interval_)
    {
        if (interval_ > 5000)
        {
            interval_ = 5000;
        }
        else if (interval_ < 10)
        {
            interval_ = 10;
        }

        interval = (UInt32)interval_;
        return 0;
    }

    /**
     int ikcp_nodelay(ikcpcb *kcp, int nodelay, int interval, int resend, int nc)
   - nodelay ：是否启用 nodelay模式，0不启用；1启用。
   - interval ：协议内部工作的 interval，单位毫秒，比如 10ms或者 20ms
   - resend ：快速重传模式，默认0关闭，可以设置2（2次ACK跨越将会直接重传）
   - nc ：是否关闭流控，默认是0代表不关闭，1代表关闭。
   - 普通模式：`ikcp_nodelay(kcp, 0, 40, 0, 0);
   - 极速模式： ikcp_nodelay(kcp, 1, 10, 2, 1);
     */

    // fastest: ikcp_nodelay(kcp, 1, 20, 2, 1)
    // nodelay: 0:disable(default), 1:enable
    // interval: internal update timer interval in millisec, default is 100ms
    // resend: 0:disable fast resend(default), 1:enable fast resend
    // nc: 0:normal congestion control(default), 1:disable congestion control
    public int NoDelay(int nodelay_, int interval_, int resend_, int nc_)
    {
        if (nodelay_ > 0)
        {
            nodelay = (UInt32)nodelay_;
            if (nodelay_ != 0)
                rx_minrto = IKCP_RTO_NDL;
            else
                rx_minrto = IKCP_RTO_MIN;
        }

        if (interval_ >= 0)
        {
            if (interval_ > 5000)
            {
                interval_ = 5000;
            }
            else if (interval_ < 1)
            {
                interval_ = 1;
            }

            interval = (UInt32)interval_;
        }

        if (resend_ >= 0) fastresend = resend_;

        if (nc_ >= 0) nocwnd = nc_;

        return 0;
    }

    //该调用将会设置协议的最大发送窗口和最大接收窗口大小，默认为32. 这个可以理解为 TCP的 SND_BUF 和 RCV_BUF，只不过单位不一样 SND/RCV_BUF 单位是字节，这个单位是包。
    // set maximum window size: sndwnd=32, rcvwnd=32 by default
    public int WndSize(int sndwnd, int rcvwnd)
    {
        if (sndwnd > 0)
            snd_wnd = (UInt32)sndwnd;

        if (rcvwnd > 0)
            rcv_wnd = (UInt32)rcvwnd;
        return 0;
    }

    // get how many packet is waiting to be sent
    public int WaitSnd()
    {
        return snd_buf.Length + snd_queue.Length;
    }
}