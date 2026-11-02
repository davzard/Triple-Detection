using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TripleDetection.Application.Line;
using TripleDetection.Application.VmServices;

namespace TripleDetection.Presentation.Views.Production
{
    /// <summary>
    /// 大显示模式（设计文档 §3.8）：全屏绿/红背景 + 100pt+ 结果大字，3m 外可读。
    /// F11 切换（由 MainWindow 全局处理；本窗口内按 F11 同样关闭）。
    /// </summary>
    public partial class BigResultWindow : Window
    {
        private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0, 140, 0));
        private static readonly Brush NgBrush = new SolidColorBrush(Color.FromRgb(170, 0, 0));
        private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        private static readonly Brush AlarmBrush = new SolidColorBrush(Color.FromRgb(120, 0, 0));

        private readonly Action _onClose;
        private string _lastResultText = "等待";
        private Brush _lastResultBrush = IdleBrush;

        public BigResultWindow(IDetectionEngine engine, ILineController line, Action onClose)
        {
            InitializeComponent();
            _onClose = onClose ?? (() => { });

            engine.DetectionResult += OnDetectionResult;
            line.StateChanged += OnLineStateChanged;
            line.AlarmRaised += OnLineAlarm;

            Closed += (s, e) =>
            {
                engine.DetectionResult -= OnDetectionResult;
                line.StateChanged -= OnLineStateChanged;
                line.AlarmRaised -= OnLineAlarm;
            };

            UpdateStateText(line.State);
        }

        private void OnDetectionResult(object sender, DetectionResult result)
        {
            Dispatcher.Invoke(() =>
            {
                bool isAlarm = false;
                // 报警状态下不覆盖报警显示
                if (txtResult.Text == "报警") isAlarm = true;
                if (isAlarm) return;

                _lastResultText = result.IsOK ? "OK" : "NG";
                _lastResultBrush = result.IsOK ? OkBrush : NgBrush;
                Background = _lastResultBrush;
                txtResult.Text = _lastResultText;
                txtDetail.Text = result.IsOK
                    ? $"批次 {result.BatchNumber}"
                    : $"NG：{result.NgDetail}";
            });
        }

        private void OnLineStateChanged(object sender, LineStateChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStateText(e.To);
                if (e.To == LineState.Alarm)
                    ShowAlarm();
                else if (e.From == LineState.Alarm)
                    RestoreLastResult();
            });
        }

        private void OnLineAlarm(object sender, LineAlarmEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e == null) return;
                ShowAlarm();
                txtDetail.Text = $"报警码 {e.AlarmCode}：{e.Message}";
            });
        }

        private void ShowAlarm()
        {
            Background = AlarmBrush;
            txtResult.Text = "报警";
        }

        private void RestoreLastResult()
        {
            Background = _lastResultBrush;
            txtResult.Text = _lastResultText;
            txtDetail.Text = "";
        }

        private void UpdateStateText(LineState state)
        {
            txtState.Text = "产线状态: " + state switch
            {
                LineState.Idle => "空闲",
                LineState.Ready => "就绪",
                LineState.Running => "运行中",
                LineState.Paused => "已暂停",
                LineState.Alarm => "报警",
                _ => state.ToString()
            };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                e.Handled = true;
                _onClose();
                return;
            }
            base.OnKeyDown(e);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => _onClose();
    }
}
