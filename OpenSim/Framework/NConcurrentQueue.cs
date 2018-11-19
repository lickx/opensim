/* 15 November 2018
 *   
 * Copyright Nani Sundara 2018
 * 
 * Keeping its very simple and low level. A queue and an event.
 * 
 */

using System.Collections.Generic;
using System.Threading;

namespace OpenSim.Framework
{
    public class NConcurrentQueue<T>
    {
        private readonly ManualResetEvent m_signal = new ManualResetEvent(false);
        private readonly Queue<T> m_queue = new Queue<T>();
        private readonly object m_syncLock = new object();

        private bool m_active = true;
        private bool m_running = true;

        public NConcurrentQueue()
        {
            m_running = true;
            m_active  = true;
        }

        public void Destroy()
        {
            try
            {
                m_running = false;
                m_active  = false;
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
            lock(m_syncLock)
                 m_queue.Clear();
         }

        public int Count()
        {
            lock (m_syncLock)
                  return m_queue.Count;
        }

        public void Enqueue( T value)
        {
            lock (m_syncLock)
            {
                m_active = m_running;
                m_queue.Enqueue(value);
                m_signal.Set();
            }
        }

        public bool Dequeue(out T value) 
        {
            lock (m_syncLock)
            {
                try
                {
                    if (m_queue.Count > 0)
                    {
                        value = m_queue.Dequeue();
                        return true;
                    }
                }
                finally
                {
                    // We could just have taken the last object in the queue,
                    // or the queue was empty already.
                    // In either case we reset the signal.
                    if (m_active && m_queue.Count == 0)
                        m_signal.Reset();
                }
            }
            // Emty queue.
            value = default(T);
            return false;
        }

        public bool TryDequeue( out T value )
        {
            while (m_active)
            {
                if (Dequeue(out value))
                    return true;

                if (m_active)
                    m_signal.WaitOne();
            }
            value = default(T);
            return false;
        }

        public bool TryDequeue(out T value, int millisecondsTimeOut)
        {
            if (Dequeue(out value))
                return true;

            if (m_active)
            {
                m_signal.WaitOne(millisecondsTimeOut);
                if (m_active && Dequeue(out value))
                    return true;
            }
            value = default(T);
            return false;
        }

        public void CancelWait()
        {
            try
            {
                m_active = false;
                m_signal.Set();            
            } catch { }
            Thread.Yield();

            // Do it twice to make sure.
            try
            {
                m_active = false;
                m_signal.Set();
            }
            catch { }
            Thread.Yield();
        }
    }
}
