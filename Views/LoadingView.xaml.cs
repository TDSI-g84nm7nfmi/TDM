using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TDM.Services;

namespace TDM.Views
{
    public partial class LoadingView : UserControl
    {
        private readonly DispatcherTimer _spinnerTimer;
        private bool _started;

        public LoadingView()
        {
            InitializeComponent();
            _spinnerTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _spinnerTimer.Tick += (_, _) =>
            {
                try
                {
                    SpinnerRotate.Angle = (SpinnerRotate.Angle + 6) % 360;
                }
                catch (Exception ex) { Logger.Warn("LoadingView 旋转异常: " + ex.Message); }
            };
            Loaded += (_, _) => { if (_started) _spinnerTimer.Start(); };
            Unloaded += (_, _) => _spinnerTimer.Stop();
        }

        public void Start()
        {
            _started = true;
            try { _spinnerTimer.Start(); } catch { }
        }

        public void Stop()
        {
            _started = false;
            try { _spinnerTimer.Stop(); } catch { }
        }

        public void SetStatus(string text)
        {
            try { StatusText.Text = text; } catch { }
        }
    }
}
