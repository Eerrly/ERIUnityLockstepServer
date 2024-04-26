using Google.Protobuf;

public class MsgPoolManager : AManager<MsgPoolManager>
{
    private Dictionary<int, Queue<IMessage>> cacheMsgDic;

    public override void Initialize()
    {
        cacheMsgDic = new Dictionary<int, Queue<IMessage>>();
    }

    public override void OnRelease()
    {
        cacheMsgDic.Clear();
    }

    public int GetMsgQueueCount<T>() where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        if (!cacheMsgDic.ContainsKey(hash))
            return 0;
        return cacheMsgDic[hash].Count;
    }

    public T Require<T>() where T : IMessage, new()
    {
        var msg = default(T);
        var hash = typeof(T).GetHashCode();
        if (!cacheMsgDic.ContainsKey(hash))
            cacheMsgDic[hash] = new Queue<IMessage>();
        if (cacheMsgDic[hash].Count > 0)
            msg = (T)cacheMsgDic[hash].Dequeue();
        msg = msg == null ? new T() : msg;
        return msg;
    }

    public void Release<T>(T msg) where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        cacheMsgDic[hash].Enqueue(msg);
    }
}