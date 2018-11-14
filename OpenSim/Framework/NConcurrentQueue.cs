/* 13 November 2018
 *   
 * Copyright Nani Sundara 2018
 * 
 * Keeping its very simple and low level.
 * A chained list or elements with a signaling system.
 * 
 */

using System.Threading;

namespace OpenSim.Framework
{
    public class NConcurrentQueue<T>
    {
        private readonly ManualResetEvent m_signal = new ManualResetEvent(false);

        internal class q_element
        {
            public T value;
            public volatile q_element next = null;
        }

        private readonly object m_syncLock = new object();
        private q_element q_first = null;
        private q_element q_last = null;

        private bool m_active = true;
        private bool m_running = true;
        private int m_count = 0;

        public NConcurrentQueue()
        {
            m_running = true;
            m_active = true;
        }

        public void Destroy()
        {
            try
            {
                m_running = false;
                m_active = false;
                lock (m_syncLock)
                      m_signal.Set();
            }
            catch { }

            Thread.Yield();

            Clear();
        }

        ~NConcurrentQueue()
        {
            Destroy();
        }

        public void Clear()
        {
            try
            {
                q_element old_first;
                lock(m_syncLock)
                {
                    old_first = q_first;
                    // We start a new chain and then clean up the old one.
                    q_first = null;
                    q_last = null;
                    m_count = 0;
                }
                // After we have isolated the old chain we can clean up all references.
                while (old_first != null)
                {
                    q_element q = old_first;
                    old_first = q.next;
                    q.value = default(T);
                    q.next = null;
                    q = null;
                }
                old_first = null;
            }
            catch { }
        }

        public int Count()
        {
            return m_count;
        }

        public void Enqueue( T value)
        {
            q_element q = new q_element() { value = value };
            lock (m_syncLock)
            {
                // Is the queue empty?
                if (q_first == null)
                {
                    // we start a new chain.
                    q_first = q;
                    m_count = 1;
                }
                else
                {
                    // Now add the new element to the end of the chain.
                    // The logic dictates that q_last will never be null at this point.
                    q_last.next = q;
                    m_count++;
                }
                // Move the reference to the new last element.
                q_last = q;
                // Now re-activate and wake up.
                m_active = m_running;
                m_signal.Set();
            }
        }

        public bool Dequeue(out T value) 
        {
            q_element q;
            lock (m_syncLock)
            {
                // Is the queue empty?
                if (q_first == null) 
                {
                    m_count = 0;
                    value = default(T);
                    if (m_active)
                        m_signal.Reset();
                    return false; // Only return false when the chain is empty.
                }
                q = q_first;
                q_first = q.next; // can be null but that is just fine.
                m_count--;
                // Do not worry about q_last because the logic of Enqueue will deal with it.
            }
            // No more need for the lock.
            // This should be thread safe since we have isolated q from the list.
            value = q.value;
            // removing references will help the GC to get rid of the element faster.
            q.value = default(T);
            q.next = null;
            return true;
        }

        public bool TryDequeue( out T value )
        {
            try
            {
                while (m_active)
                {
                    if (Dequeue(out value))
                        return true;

                    m_signal.WaitOne();
                }
            }
            catch { }
            value = default(T);
            return false;
        }

        public bool TryDequeue(out T value, int millisecondsTimeOut)
        {
            try
            {
                if (Dequeue(out value))
                    return true;

                if (m_active)
                {
                    m_signal.WaitOne(millisecondsTimeOut);

                    if (m_active && Dequeue(out value))
                        return true;
                }
            }
            catch { }
            value = default(T);
            return false;
        }

        public void CancelWait()
        {
            try
            {
                m_active = false;
                lock (m_syncLock)
                      m_signal.Set();
            } catch { }
            Thread.Yield();

            // Do it twice to make sure.
            try
            {
                m_active = false;
                lock (m_syncLock)
                    m_signal.Set();
            }
            catch { }
            Thread.Yield();
        }
    }
}
