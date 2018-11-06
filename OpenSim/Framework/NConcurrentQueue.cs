/* 28 October 2018
 *   
 * Copyright Nani Sundara 2018
 * 
 * A thread safe concurrent queue with cancelation option. 
 * It does not block Enqueue or Dequeue but it will wait (block) 
 * when TryDequeue can not Dequeue at once.
 * 
 */

using System.Collections.Concurrent;
using System.Threading;

namespace OpenSim.Framework
{
    public class NConcurrentQueue<T>
    {
        private ConcurrentQueue<T> m_queue = new ConcurrentQueue<T>();
        private ManualResetEvent m_event = null;
        private volatile bool m_active = true;

        public NConcurrentQueue()
        {
            m_active = true;
            m_event = new ManualResetEvent(false);
        }

        public void Destroy()
        {
            try
            {
                m_active = false;
                m_event.Set();
                Thread.Yield();
                m_event.Dispose();
            }
            catch { }

            try
            {
                while (m_queue.TryDequeue(out T value)) { }
            }
            catch { }
        }

        ~NConcurrentQueue()
        {
            Destroy();
        }

        public void Clear()
        {
            try
            {
                m_active = false;
                while (m_queue.TryDequeue(out T value)) { }
                m_active = true;
            }
            catch { }
        }

        public int Count()
        {
            return m_queue.Count;
        }

        public void Enqueue( T value)
        {
            m_queue.Enqueue(value);
            m_event.Set();
        }

        public bool Dequeue( out T value )
        {
            if (m_queue.TryDequeue( out value ))
                return true;

            value = default(T);
            return false;
        }

        public bool TryDequeue( out T value )
        {
            try
            {
                while (m_active)
                {
                    if (m_queue.TryDequeue(out value))
                        return true;                

                    // Wait for a signal or a cancel.
                    if (m_active)
                    {
                        try { }
                        finally // A finally block can not be interrupted
                        {
                            // We must avoid the rare situation where a value was enqueued
                            // and m_event was Set between the call of IsEmpty and the Reset.
                            // Using finally we make it atomic.
                            if (m_queue.IsEmpty)
                                m_event.Reset();
                        }
                        m_event.WaitOne();
                    }
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
                if (m_queue.TryDequeue(out value))
                    return true;

                // Wait for a signal or a cancel.
                if (m_active)
                {
                    try { }
                    finally // A finally block can not be interrupted
                    {
                        // We must avoid the rare situation where a value was enqueued
                        // and m_event was Set between the call of IsEmpty and the Reset.
                        // using finally we make it atomic
                        if (m_queue.IsEmpty)
                            m_event.Reset();
                    }
                    m_event.WaitOne(millisecondsTimeOut);
                }

                if (m_active)
                {
                    if (m_queue.TryDequeue(out value))
                        return true;
                }
            }
            catch { }

            value = default(T);
            return false;
        }

        public void CancelWait()
        {
            m_active = false;
            m_event.Set();
            Thread.Yield();

            m_event.Set();
            Thread.Sleep(100);

            // We only canceled the Wait,
            // we do not want to deactivate
            // the queue for future use.
            m_active = true;
        }
    }
}
