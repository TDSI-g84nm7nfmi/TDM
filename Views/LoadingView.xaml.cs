using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TDM.Views
{
    public partial class LoadingView : UserControl
    {
        private readonly DispatcherTimer _spinnerTimer;

        public LoadingView()
        {
            InitializeComponent();
            // 用一个低开销的 60fps DispatcherTimer 持续旋转弧线
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
                catch { }
            };
        }

        /// <summary>在 Loaded 时启动旋转，Unloaded 时停止，避免空转消耗 CPU</summary>
        public void Start()
        {
            try { _spinnerTimer.Start(); } catch { }
        }

        public void Stop()
        {
            try { _spinnerTimer.Stop(); } catch { }
        }

        /// <summary>更新状态文字</summary>
        public void SetStatus(string text)
        {
            try { StatusText.Text = text; } catch { }
        }
    }
}
