//----------------------------------------------
//            NGUI: Next-Gen UI kit
// Copyright © 2011-2015 Tasharen Entertainment
//----------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// This improved version of the System.Collections.Generic.List that doesn't release the buffer on Clear(),
/// resulting in better performance and less garbage collection.
/// PRO: BetterList performs faster than List when you Add and Remove items (although slower if you remove from the beginning).
/// CON: BetterList performs worse when sorting the list. If your operations involve sorting, use the standard List instead.
/// </summary>

public class BetterList<T>
{
#if UNITY_FLASH

	List<T> mList = new List<T>();
	
	/// <summary>
	/// Direct access to the buffer. Note that you should not use its 'Length' parameter, but instead use BetterList.size.
	/// </summary>
	
	public T this[int i]
	{
		get { return mList[i]; }
		set { mList[i] = value; }
	}
	
	/// <summary>
	/// Compatibility with the non-flash syntax.
	/// </summary>
	
	public List<T> buffer { get { return mList; } }

	/// <summary>
	/// Direct access to the buffer's size. Note that it's only public for speed and efficiency. You shouldn't modify it.
	/// </summary>

	public int size { get { return mList.Count; } }

	/// <summary>
	/// For 'foreach' functionality.
	/// </summary>

	public IEnumerator<T> GetEnumerator () { return mList.GetEnumerator(); }

	/// <summary>
	/// Clear the array by resetting its size to zero. Note that the memory is not actually released.
	/// </summary>

	public void Clear () { mList.Clear(); }

	/// <summary>
	/// Clear the array and release the used memory.
	/// </summary>

	public void Release () { mList.Clear(); }

	/// <summary>
	/// Add the specified item to the end of the list.
	/// </summary>

	public void Add (T item) { mList.Add(item); }

	/// <summary>
	/// Insert an item at the specified index, pushing the entries back.
	/// </summary>

	public void Insert (int index, T item)
	{
		if (index > -1 && index < mList.Count) mList.Insert(index, item);
		else mList.Add(item);
	}

	/// <summary>
	/// Returns 'true' if the specified item is within the list.
	/// </summary>

	public bool Contains (T item) { return mList.Contains(item); }

	/// <summary>
	/// Return the index of the specified item.
	/// </summary>

	public int IndexOf (T item) { return mList.IndexOf(item); }

	/// <summary>
	/// Remove the specified item from the list. Note that RemoveAt() is faster and is advisable if you already know the index.
	/// </summary>

	public bool Remove (T item) { return mList.Remove(item); }

	/// <summary>
	/// Remove an item at the specified index.
	/// </summary>

	public void RemoveAt (int index) { mList.RemoveAt(index); }

	/// <summary>
	/// Remove an item from the end.
	/// </summary>

	public T Pop ()
	{
		if (buffer != null && size != 0)
		{
			T val = buffer[mList.Count - 1];
			mList.RemoveAt(mList.Count - 1);
			return val;
		}
		return default(T);
	}

	/// <summary>
	/// Mimic List's ToArray() functionality, except that in this case the list is resized to match the current size.
	/// </summary>

	public T[] ToArray () { return mList.ToArray(); }

	/// <summary>
	/// List.Sort equivalent.
	/// </summary>

	public void Sort (System.Comparison<T> comparer) { mList.Sort(comparer); }

#else

    const int minBufferSize = 32;   //最低分配的内存大小

    public BetterList()
    {

    }

    public BetterList(int capacity)
    {
        buffer = new T[capacity];
    }

	/// <summary>
	/// Direct access to the buffer. Note that you should not use its 'Length' parameter, but instead use BetterList.size.
	/// </summary>

	public T[] buffer;

	/// <summary>
	/// Direct access to the buffer's size. Note that it's only public for speed and efficiency. You shouldn't modify it.
	/// </summary>

	public int size = 0;

    public int Count
    {
        get
        {
            return size;
        }
    }

    public int Length
    {
        get 
        {
            return size;
        }
    }

	/// <summary>
	/// For 'foreach' functionality.
	/// </summary>

	[DebuggerHidden]
	[DebuggerStepThrough]
	public IEnumerator<T> GetEnumerator ()
	{
		if (buffer != null)
		{
			for (int i = 0; i < size; ++i)
			{
				yield return buffer[i];
			}
		}
	}
	
	/// <summary>
	/// Convenience function. I recommend using .buffer instead.
	/// </summary>

	[DebuggerHidden]
	public T this[int i]
	{
		get { return buffer[i]; }
		set { buffer[i] = value; }
	}

	/// <summary>
	/// Helper function that expands the size of the array, maintaining the content.
	/// </summary>

	void AllocateMore ()
	{
        T[] newList = (buffer != null) ? new T[Math.Max(buffer.Length << 1, minBufferSize)] : new T[minBufferSize];
		if (buffer != null && size > 0) buffer.CopyTo(newList, 0);
		buffer = newList;
	}

    void CheckNeedAllocateMore(int appendCount)
    {
        if (buffer == null || size + appendCount > buffer.Length)
        {
            int count = Math.Max(size + appendCount, minBufferSize);
            T[] newList = new T[count];
            if (buffer != null && size > 0) buffer.CopyTo(newList, 0);
            buffer = newList;
        }
    }

	/// <summary>
	/// Trim the unnecessary memory, resizing the buffer to be of 'Length' size.
	/// Call this function only if you are sure that the buffer won't need to resize anytime soon.
	/// </summary>

	void Trim ()
	{
		if (size > 0)
		{
			if (size < buffer.Length)
			{
				T[] newList = new T[size];
				for (int i = 0; i < size; ++i) newList[i] = buffer[i];
				buffer = newList;
			}
		}
		else buffer = null;
	}

	/// <summary>
	/// Clear the array by resetting its size to zero. Note that the memory is not actually released.
	/// </summary>

	public void Clear () { size = 0; }

	/// <summary>
	/// Clear the array and release the used memory.
	/// </summary>

	public void Release () { size = 0; buffer = null; }

	/// <summary>
	/// Add the specified item to the end of the list.
	/// </summary>

	public void Add (T item)
	{
		if (buffer == null || size == buffer.Length) AllocateMore();
		buffer[size++] = item;
	}

    public void AddRange(BetterList<T> other)
    {
        CheckNeedAllocateMore(other.Count);

        for (int i = 0; i < other.Count; i++)
        {
            buffer[size++] = other[i];
        }
    }

	/// <summary>
	/// Insert an item at the specified index, pushing the entries back.
	/// </summary>

	public void Insert (int index, T item)
	{
		if (buffer == null || size == buffer.Length) AllocateMore();

		if (index > -1 && index < size)
		{
			for (int i = size; i > index; --i) buffer[i] = buffer[i - 1];
			buffer[index] = item;
			++size;
		}
		else Add(item);
	}

	/// <summary>
	/// Returns 'true' if the specified item is within the list.
	/// </summary>

	public bool Contains (T item)
	{
		if (buffer == null) return false;
		for (int i = 0; i < size; ++i) if (buffer[i].Equals(item)) return true;
		return false;
	}

	/// <summary>
	/// Return the index of the specified item.
	/// </summary>

	public int IndexOf (T item)
	{
		if (buffer == null) return -1;
		for (int i = 0; i < size; ++i) if (buffer[i].Equals(item)) return i;
		return -1;
	}

	/// <summary>
	/// Remove the specified item from the list. Note that RemoveAt() is faster and is advisable if you already know the index.
	/// </summary>

	public bool Remove (T item)
	{
		if (buffer != null)
		{
			EqualityComparer<T> comp = EqualityComparer<T>.Default;

			for (int i = 0; i < size; ++i)
			{
				if (comp.Equals(buffer[i], item))
				{
					--size;
					buffer[i] = default(T);
					for (int b = i; b < size; ++b) buffer[b] = buffer[b + 1];
					buffer[size] = default(T);
					return true;
				}
			}
		}
		return false;
	}

    public void Remove(int startIndex, int count)
    {
        int stopIndex = startIndex + count;
        for (int i = stopIndex; i < size; i++)
        {
            buffer[i - count] = buffer[i];
        }

        size -= count;
    }

	/// <summary>
	/// Remove an item at the specified index.
	/// </summary>

	public void RemoveAt (int index)
	{
		if (buffer != null && index > -1 && index < size)
		{
			--size;
			buffer[index] = default(T);
			for (int b = index; b < size; ++b) buffer[b] = buffer[b + 1];
			buffer[size] = default(T);
		}
	}

	/// <summary>
	/// Remove an item from the end.
	/// </summary>

	public T Pop ()
	{
		if (buffer != null && size != 0)
		{
			T val = buffer[--size];
			buffer[size] = default(T);
			return val;
		}
		return default(T);
	}

	/// <summary>
	/// Mimic List's ToArray() functionality, except that in this case the list is resized to match the current size.
	/// </summary>

	public T[] ToArray () { Trim(); return buffer; }

    //整理内存，保留下标start(包含)到下标stop(不包含)的元素
    public void Retain(int startIndex, int stopIndex)
    {
        int count = stopIndex - startIndex;
        for (int i = 0; i < count; i++)
        {
            buffer[i] = buffer[startIndex + i];
        }

        size = count;
    }

    //start:开始的下标
    //count:数量
    public BetterList<T> Slice(int startIndex, int count)
    {
        int bufferSize = Math.Max(minBufferSize, count);
        BetterList<T> list = new BetterList<T>(bufferSize);

        int stopIndex = startIndex + count;
        for (int i = startIndex; i < stopIndex; i++)
        {
            list.buffer[list.size++] = this.buffer[i];
        }

        return list;
    }

	/// <summary>
	/// List.Sort equivalent. Manual sorting causes no GC allocations.
	/// </summary>
	[DebuggerHidden]
	[DebuggerStepThrough]
	public void Sort (CompareFunc comparer)
	{
		int start = 0;
		int max = size - 1;
		bool changed = true;

		while (changed)
		{
			changed = false;

			for (int i = start; i < max; ++i)
			{
				// Compare the two values
				if (comparer(buffer[i], buffer[i + 1]) > 0)
				{
					// Swap the values
					T temp = buffer[i];
					buffer[i] = buffer[i + 1];
					buffer[i + 1] = temp;
					changed = true;
				}
				else if (!changed)
				{
					// Nothing has changed -- we can start here next time
					start = (i == 0) ? 0 : i - 1;
				}
			}
		}
	}

	/// <summary>
	/// Comparison function should return -1 if left is less than right, 1 if left is greater than right, and 0 if they match.
	/// </summary>

	public delegate int CompareFunc (T left, T right);
#endif
}
