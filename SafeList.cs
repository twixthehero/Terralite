using System;
using System.Collections.Generic;

namespace Terralite
{
    public class SafeList<T>
    {
        private List<T> list;
        private object listLock;

        public SafeList()
        {
            list = new List<T>();
            listLock = new object();
        }

        public int Count
        {
            get { lock (listLock) { return list.Count; } }
        }

        public void Add(T obj)
        {
            lock (listLock)
            {
                list.Add(obj);
            }
        }

        public void AddRange(IEnumerable<T> range)
        {
            lock (listLock)
            {
                list.AddRange(range);
            }
        }

        public void Clear()
        {
            lock (listLock)
            {
                list.Clear();
            }
        }

        public void Remove(T obj)
        {
            lock (listLock)
            {
                list.Remove(obj);
            }
        }

        public void RemoveAt(int index)
        {
            lock (listLock)
            {
                list.RemoveAt(index);
            }
        }

        public T this[int index]
        {
            get
            {
                lock (listLock)
                {
                    return list[index];
                }
            }
        }
    }
}