
/* Run specified 'Action' every day at time. */

namespace StudentExchangeBck
{
    public class Maitenance : IDisposable
    {
        readonly AutoResetEvent _evt;
        readonly Thread _thread;
        bool _abortThread;
        // Time to run maitenance
        readonly TimeSpan _time;
        // Task to run at time
        readonly Action _RunTask;

        /// <summary>
        /// Run specified 'Action' every day at time.
        /// </summary>
        /// <param name="time">Time to run every day</param>
        /// <param name="Task">Task to run every day</param>
        /// <exception cref="ArgumentException"></exception>
        public Maitenance(int time, Action Task)
        {
            // Validate & save time
            if (time <= 0 || time >= 2400) throw new ArgumentException("time <= 0 || time >= 2400");
            (var hour, var min) = (time / 100, time % 100);
            if (min >= 60) throw new ArgumentException("min >= 60");
            _time = new TimeSpan(hour, min, 0);

            // Async worker thread
            _RunTask = Task;
            _evt = new AutoResetEvent(false);
            _thread = new Thread(Worker);
            _thread.Name = "Maitenance-Worker";
            _thread.Start();
        }

        /// <summary>
        /// Async worker to run task every day at time.
        /// </summary>
        public void Worker()
        {
            while (true)
            {
                // Find time to wait until specified next time
                var now = Utilz.ToMtn();
                DateTime nextDate = new DateTime(now.Year, now.Month, now.Day, _time.Hours, _time.Minutes, 0);
                while (nextDate.Subtract(now).TotalDays <= 0)
                    nextDate = nextDate.AddDays(1);
                var wait = nextDate.Subtract(Utilz.ToMtn());

                // Wait specified time or Event is signaled
                _evt.WaitOne(wait);
                // Exit: if abort
                if (_abortThread) return;

                // Run Task
                try { _RunTask(); }
                catch { }
            }
        }

        /// <summary>
        /// Dispose: Stop worker thread.
        /// </summary>
        public void Dispose()
        {
            _abortThread = true;
            _evt.Set();
        }
    }
}
