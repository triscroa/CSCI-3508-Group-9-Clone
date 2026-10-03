namespace StudentExchangeBck
{
    public class Maitenance : IDisposable
    {
        readonly AutoResetEvent _evt;
        readonly Thread _thread;
        bool _abortThread;
        readonly TimeSpan _time;
        readonly Action _RunTask;

        public Maitenance(int time, Action Task)
        {
            if (time <= 0 || time >= 2400) throw new ArgumentException("time <= 0 || time >= 2400");
            (var hour, var min) = (time / 100, time % 100);
            if (min >= 60) throw new ArgumentException("min >= 60");
            _time = new TimeSpan(hour, min, 0);

            _RunTask = Task;
            _evt = new AutoResetEvent(false);
            _thread = new Thread(Worker);
            _thread.Name = "Maitenance-Worker";
            _thread.Start();
        }

        public void Worker()
        {
            while (true)
            {
                var now = Utilz.ToMtn();
                DateTime nextDate = new DateTime(now.Year, now.Month, now.Day, _time.Hours, _time.Minutes, 0);
                while (nextDate.Subtract(now).TotalDays <= 0)
                    nextDate = nextDate.AddDays(1);
                var wait = nextDate.Subtract(Utilz.ToMtn());

                _evt.WaitOne(wait);
                if (_abortThread) return;

                try { _RunTask(); }
                catch { }
            }
        }

        public void Dispose()
        {
            _abortThread = true;
            _evt.Set();
        }
    }
}
