using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TripleDetection.Application.Line;
using TripleDetection.Application.Services;
using TripleDetection.Application.VmServices;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Infrastructure.Device;

namespace TripleDetection.Presentation.Views.Production
{
    public partial class LineView : UserControl
    {
        private readonly IPlcGateway _plc;
        private readonly ILineController _line;
        private readonly LineOrchestrator _orchestrator;
        private readonly ITaskService _taskService;
        private readonly LoggingService _logService;
        private readonly IDetectionEngine _engine;
        private readonly CycleTimeMonitor? _cycleMonitor;
        private readonly DispatcherTimer _timer;
        private List<ProdTask> _taskList = new List<ProdTask>();

        public LineView(IPlcGateway plc, ILineController line, LineOrchestrator orchestrator,
            ITaskService taskService, LoggingService logService,
            IDetectionEngine engine = null, CycleTimeMonitor cycleMonitor = null)
        {
            InitializeComponent();
            _plc = plc;
            _line = line;
            _orchestrator = orchestrator;
            _taskService = taskService;
            _logService = logService;
            _engine = engine;
            _cycleMonitor = cycleMonitor;

            LoadTasks();

            // M9-lite：开班前时间偏差确认（UI 弹框，设计文档 §3.9）
            if (_orchestrator.TimeDriftCheck == null)
                _orchestrator.TimeDriftCheck = offset =>
                {
                    var result = MessageBox.Show(
                        $"系统时间与 NTP 服务器偏差 {offset.TotalSeconds:F1} 秒（超过 5 秒阈值）。\n" +
                        "追溯时间戳可能不可信，建议先校准系统时钟。\n\n仍要继续开班吗？",
                        "时间偏差警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    return result == MessageBoxResult.Yes;
                };

            _line.StateChanged += (s, e) => OnLineEvent();
            _line.AlarmRaised += (s, e) => OnLineEvent();
            _logService.OnLogAdded += OnLogAdded;

            if (_plc is MockPlcGateway)
            {
                grpMock.Visibility = Visibility.Visible;
                txtPlcMode.Text = "模式: Mock（模拟）";
                if (_engine is MockDetectionEngine)
                    grpMockEngine.Visibility = Visibility.Visible;
            }
            else
            {
                txtPlcMode.Text = "模式: Modbus TCP";
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += (s, e) => RefreshDisplay();
            _timer.Start();

            RefreshDisplay();
        }

        private void LoadTasks()
        {
            _taskList = _taskService?.GetAll()
                .Where(t => t.Status == TaskStatus.Approved)
                .OrderBy(t => t.Id)
                .ToList() ?? new List<ProdTask>();
            cmbTask.ItemsSource = _taskList;
            if (_taskList.Count > 0) cmbTask.SelectedIndex = 0;
        }

        private void OnLineEvent() => RefreshDisplay();

        private void OnLogAdded(object sender, LogEntry entry)
        {
            // 只显示产线/PLC 分类的日志
            if (entry == null || (!entry.Message.StartsWith("[产线]") && !entry.Message.StartsWith("[PLC]")))
                return;
            Dispatcher.Invoke(() =>
            {
                lstLineLogs.Items.Insert(0, $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}");
                while (lstLineLogs.Items.Count > 200)
                    lstLineLogs.Items.RemoveAt(lstLineLogs.Items.Count - 1);
            });
        }

        private void RefreshDisplay()
        {
            var state = _line.State;
            txtLineState.Text = state switch
            {
                LineState.Idle => "未开班",
                LineState.Ready => "待启动",
                LineState.Running => "运行中",
                LineState.Paused => "已暂停",
                LineState.Alarm => "报警",
                _ => state.ToString()
            };
            txtLineState.Foreground = new SolidColorBrush(state switch
            {
                LineState.Running => Color.FromRgb(0, 150, 0),
                LineState.Alarm => Color.FromRgb(200, 0, 0),
                LineState.Paused => Color.FromRgb(200, 120, 0),
                LineState.Ready => Color.FromRgb(0, 100, 200),
                _ => Color.FromRgb(120, 120, 120)
            });

            txtPlcState.Text = _plc.IsConnected ? "已连接" : "未连接";
            txtPlcState.Foreground = new SolidColorBrush(_plc.IsConnected
                ? Color.FromRgb(0, 150, 0) : Color.FromRgb(200, 0, 0));

            // 相机心跳（Phase 2）
            if (_engine is ICameraHeartbeat hb && hb.LastFrameTime is DateTime lastFrame)
            {
                var ageMs = (long)(DateTime.Now - lastFrame).TotalMilliseconds;
                var healthy = ageMs < 5000;
                txtCameraState.Text = healthy ? "正常" : "超时";
                txtCameraState.Foreground = new SolidColorBrush(healthy
                    ? Color.FromRgb(0, 150, 0) : Color.FromRgb(200, 0, 0));
                txtCameraDetail.Text = $"最后帧: {ageMs}ms 前";
            }
            else
            {
                txtCameraState.Text = "--";
                txtCameraState.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120));
                txtCameraDetail.Text = "最后帧: --";
            }

            // 节拍统计（Phase 2）
            if (_cycleMonitor != null)
            {
                var (count, avg, p99) = _cycleMonitor.GetStats();
                txtCycleStats.Text = count > 0
                    ? $"节拍: 均值 {avg}ms / P99 {p99}ms（{count} 样本）"
                    : "节拍: --";
            }

            if (_line.AlarmCode > 0)
            {
                txtAlarmCode.Text = _line.AlarmCode.ToString();
                txtAlarmCode.Foreground = new SolidColorBrush(Color.FromRgb(200, 0, 0));
                txtAlarmMessage.Text = _line.AlarmMessage ?? string.Empty;
            }
            else
            {
                txtAlarmCode.Text = "无";
                txtAlarmCode.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120));
                txtAlarmMessage.Text = string.Empty;
            }

            // 按钮可用性
            btnStartShift.IsEnabled = state == LineState.Idle;
            btnStartAutoRun.IsEnabled = state == LineState.Ready || state == LineState.Paused;
            btnStopAutoRun.IsEnabled = state == LineState.Running;
            btnPause.IsEnabled = state == LineState.Running;
            btnResetAlarm.IsEnabled = state == LineState.Alarm;
            btnEndShift.IsEnabled = state != LineState.Idle;

            if (_plc is MockPlcGateway mock)
            {
                txtTriggerCount.Text = $"触发次数: {mock.TriggerCount}";
                txtPlcWrites.Text = $"App 写入: 结果={mock.LastResult} 剔除={mock.LastRejectDelayMs}/{mock.LastRejectDurationMs}ms " +
                                    $"停线码={mock.LastLineStopAlarmCode} 工位状态={mock.LastStationStatus}";
            }
        }

        // ===== 班次操作 =====

        private void BtnStartShift_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTask.SelectedItem is not ProdTask task)
            {
                MessageBox.Show("请先选择当班任务", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var ok = _orchestrator.StartShift(task.Id);
            if (!ok)
                MessageBox.Show("开班失败，请查看日志", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshDisplay();
        }

        private void BtnStartAutoRun_Click(object sender, RoutedEventArgs e)
        {
            _orchestrator.StartAutoRun();
            RefreshDisplay();
        }

        private void BtnStopAutoRun_Click(object sender, RoutedEventArgs e)
        {
            _orchestrator.StopAutoRun();
            RefreshDisplay();
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _orchestrator.Pause();
            RefreshDisplay();
        }

        private void BtnResetAlarm_Click(object sender, RoutedEventArgs e)
        {
            _orchestrator.ResetAlarm();
            RefreshDisplay();
        }

        private void BtnEndShift_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确认下班？将停止自动运行。", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
            _orchestrator.EndShift();
            RefreshDisplay();
        }

        // ===== Mock 模拟 =====

        private void BtnSimTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (_plc is MockPlcGateway mock) mock.SimulateTrigger();
        }

        private void BtnSimRecipe_Click(object sender, RoutedEventArgs e)
        {
            if (_plc is not MockPlcGateway mock) return;
            // 换型到任务列表中的下一个产品（演示用）
            var next = _taskList.Count > 0 ? _taskList[(_taskList.IndexOf((ProdTask)cmbTask.SelectedItem) + 1) % _taskList.Count] : null;
            int recipeId = 0;
            if (next != null)
            {
                // RECIPE_ID 约定为产品 Code 的整数值；演示时直接用产品 Id
                recipeId = next.ProductId;
            }
            mock.SimulateRecipeChange(recipeId);
        }

        private void BtnSimDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_plc is MockPlcGateway mock) mock.SimulateDisconnect();
        }

        private void BtnSimReconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_plc is MockPlcGateway mock) mock.SimulateReconnect();
        }

        // ===== Phase 2 Mock 模拟（相机 / 方案崩溃）=====

        private void BtnSimCameraLoss_Click(object sender, RoutedEventArgs e)
        {
            if (_engine is MockDetectionEngine mock) mock.SimulateCameraLoss();
        }

        private void BtnSimCameraRestore_Click(object sender, RoutedEventArgs e)
        {
            if (_engine is MockDetectionEngine mock) mock.SimulateCameraRestore();
        }

        private void BtnSimSolutionCrash_Click(object sender, RoutedEventArgs e)
        {
            if (_engine is MockDetectionEngine mock) mock.SimulateSolutionCrash();
        }
    }
}
