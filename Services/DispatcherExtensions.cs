using System;
using System.Windows.Threading;

namespace TDM.Services
{
    public static class DispatcherExtensions
    {
        public static void DelayInvoke(this Dispatcher dispatcher, Action action, int milliseconds)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try { action(); } catch { }
            };
            timer.Start();
        }
    }
}
