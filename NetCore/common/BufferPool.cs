public static class BufferPool
{
    private static object _lockObject = new object();
    private static Dictionary<int, Queue<byte[]>> _buffersCache = new Dictionary<int, Queue<byte[]>>(10);
    private static int _min;
    private static int _max;
    private static int _step;

    public static void InitPool(int min, int max, int step, int count)
    {
        _buffersCache.Clear();
        _min = min < 32 ? 32 : min;
        _max = max < _min ? _min : max;
        _step = step;
        for (int size = _min; size <= _max; size += (1 << _step))
        {
            if (!_buffersCache.ContainsKey(size))
            {
                Queue<byte[]> queue = new Queue<byte[]>(count);
                for (int index = 0; index < count; ++index)
                {
                    queue.Enqueue(new byte[size]);
                }

                _buffersCache.Add(size, queue);
            }
        }
    }

    private static int GetLevel(int size)
    {
        if (size <= 0)
        {
            return -1;
        }


        if (size > _max)
        {
            return -1;
        }
        else if (size <= _min)
        {
            return _min;
        }
        else
        {
            var level = (int)(((size + (1 << _step) - 1) >> _step) << _step);
            return level;
        }

        return -1;
    }

    public static byte[] GetBuffer(int size)
    {
        if (size <= 0)
        {
            return null;
        }

        int level = GetLevel(size);
        if (level < 0)
        {
            // Logger.Log(LogLevel.Info, "new buffer " + size);
            return new byte[size];
        }

        lock (_lockObject)
        {
            Queue<byte[]> queue = null;
            if (_buffersCache.TryGetValue(level, out queue))
            {
                if (null != queue && queue.Count > 0)
                {
                    return queue.Dequeue();
                }
            }

            // Logger.Log(LogLevel.Info, "new buffer " + size);
            return new byte[level];
        }
    }

    public static void ReleaseBuff(byte[] buff)
    {
        if (null == buff)
        {
            return;
        }

        int level = buff.Length;
        if (level < _min || level > _max)
        {
            buff = null;
        }
        else
        {
            lock (_lockObject)
            {
                Queue<byte[]> queue = null;
                if (_buffersCache.TryGetValue(level, out queue))
                {
                    if (null != queue && queue.Count < 32)
                    {
                        var released = false;
                        foreach (var t in queue)
                        {
                            if (buff == t)
                            {
                                released = true;
                                break;
                            }
                        }

                        if (!released)
                        {
                            queue.Enqueue(buff);
                        }
                        else
                        {
                            Logger.Log(LogLevel.Error, "Buff Released!\n" + new System.Diagnostics.StackTrace().ToString());
                        }
                    }
                }

                buff = null;
            }
        }
    }

    public static void ReleaseAll()
    {
        lock (_lockObject)
        {
            using (var iterator = _buffersCache.GetEnumerator())
            {
                while (iterator.MoveNext())
                {
                    iterator.Current.Value.Clear();
                }
            }
        }
    }

    public static void Print()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        using (var iterator = _buffersCache.GetEnumerator())
        {
            while (iterator.MoveNext())
            {
                sb.AppendFormat("size:{0}-count:{1}\n", iterator.Current.Key, iterator.Current.Value.Count);
            }
        }

        Logger.Log(LogLevel.Error, $"buffer pool : \n{sb.ToString()}");
    }
}