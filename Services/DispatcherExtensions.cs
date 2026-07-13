using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TDM.Services
{
    public static class DispatcherExtensions
    {
        public static void DelayInvoke(this DispatcherQueue dispatcher, Action action, int milliseconds)
        {
            var timer = new DispatcherTimer
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
