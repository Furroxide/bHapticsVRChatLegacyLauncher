using System;
using System.Threading;

namespace OscLib.Utils
{
    public abstract class ThreadedTask
    {
        private Thread thread;

        public bool IsAlive()
            => (thread == null) ? false : thread.IsAlive;

        public abstract bool BeginInitInternal();
        public void BeginInit()
        {
            if (BeginInitInternal())
                RunThread();
        }

        public abstract bool EndInitInternal();
        public void EndInit()
        {
            if (EndInitInternal())
                KillThread();
        }

        public abstract void WithinThread();
        private void RunThread()
        {
            if (IsAlive())
                KillThread();

            thread = new Thread(GuardedWithinThread);
            thread.Start();
        }

        // On .NET Framework an exception escaping any thread ends the process, so the
        // try/catch around Main never sees a worker fail: the companion just vanished,
        // with nothing on the console but a WER dialog. Contain it here - the worker
        // stops, the rest of the process carries on, and the console says which and why.
        private void GuardedWithinThread()
        {
            try
            {
                WithinThread();
            }
            catch (ThreadAbortException)
            {
                // KillThread. An abort is how a worker is asked to stop, not a fault; the
                // runtime re-raises it at the end of this block regardless.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in {GetType().Name} Thread: {ex}");
            }
        }
        private void KillThread()
        {
            if (!IsAlive())
                return;

            thread.Abort();
            thread = null;
        }
    }
}
