using Google.Protobuf;

public class MsgPoolManager : AManager<MsgPoolManager>
{
    private Dictionary<int, Queue<IMessage>> _cacheMsgDic;

    public override void Initialize()
    {
        _cacheMsgDic = new Dictionary<int, Queue<IMessage>>();
    }

    public override void OnRelease()
    {
        _cacheMsgDic.Clear();
    }

    public int GetMsgQueueCount<T>() where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        if (!_cacheMsgDic.ContainsKey(hash))
            return 0;
        return _cacheMsgDic[hash].Count;
    }

    public T Require<T>() where T : IMessage, new()
    {
        var msg = default(T);
        var hash = typeof(T).GetHashCode();
        if (!_cacheMsgDic.ContainsKey(hash))
            _cacheMsgDic[hash] = new Queue<IMessage>();
        if (_cacheMsgDic[hash].Count > 0)
            msg = (T)_cacheMsgDic[hash].Dequeue();
        msg = msg == null ? new T() : msg;
        return msg;
    }

    public void Release<T>(T msg) where T : IMessage
    {
        var hash = typeof(T).GetHashCode();
        _cacheMsgDic[hash].Enqueue(msg);
    }
}