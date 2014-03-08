using System;
using System.Threading;

namespace RaptorDB
{
    internal class CronJob
    {
        internal readonly CronSchedule _cron_schedule = new CronSchedule();
        private readonly ThreadStart _thread_start;
        internal Thread _thread;

        public CronJob(string schedule, ThreadStart thread_start)
        {
            _cron_schedule = new CronSchedule(schedule);
            _thread_start = thread_start;
            _thread = new Thread(thread_start);
        }

        private object _lock = new object();
        public void execute(DateTime date_time)
        {
            lock (_lock)
            {
                if (!_cron_schedule.isTime(date_time))
                    return;

                if (_thread.ThreadState == ThreadState.Running)
                    return;

                _thread = new Thread(_thread_start);
                _thread.Start();
            }
        }

        public void abort()
        {
          _thread.Abort();  
        }

    }
}
