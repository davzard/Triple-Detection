using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Prism.Events;
using TripleDetection.Application.Services;
using TripleDetection.Domain.Entities.Queries;
using TripleDetection.Domain.Enums;
using TripleDetection.Presentation.Events;

namespace TripleDetection.Presentation.Views.App
{
    public partial class DashboardView : UserControl
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IDetectionRecordService _recordService;
        private readonly ITaskService _taskService;
        private readonly LoggingService? _logService;

        public DashboardView(IEventAggregator eventAggregator, IDetectionRecordService recordService, ITaskService taskService, LoggingService? logService = null)
        {
            _eventAggregator = eventAggregator;
            _recordService = recordService;
            _taskService = taskService;
            _logService = logService;
            InitializeComponent();
            LoadStats();
        }

        private void LoadStats()
        {
            try
            {
                var today = DateTime.Today;
                var todayRecords = _recordService.Query(new DetectionRecordQuery
                {
                    StartDate = today,
                    EndDate = today.AddDays(1).AddSeconds(-1),
                    PageSize = 100000
                }).Items.ToList();

                txtTodayOk.Text = todayRecords.Count(r => r.IsOK).ToString();
                txtTodayNg.Text = todayRecords.Count(r => !r.IsOK).ToString();
                txtTotalTasks.Text = _taskService.GetAll().Count().ToString();
                txtPending.Text = _taskService.GetByStatus(TaskStatus.Pending).Count().ToString();

                var recent = _recordService.Query(new DetectionRecordQuery { PageSize = 10 })
                    .Items
                    .Select(r => new RecentDetectionItem
                    {
                        DetectionTime = r.DetectionTime,
                        TaskName = ResolveTaskName(r.TaskId),
                        Result = r.IsOK ? "OK" : "NG",
                        ElapsedMs = r.ElapsedMs
                    })
                    .ToList();
                dgRecent.ItemsSource = recent;
            }
            catch (Exception ex)
            {
                // 统计加载失败时保留占位值，不阻塞页面渲染；但必须留痕
                _logService?.Log($"[Dashboard] 统计加载失败: {ex.Message}", "ERROR");
            }
        }

        private string ResolveTaskName(int taskId)
        {
            if (taskId <= 0) return "未知任务";
            return _taskService.GetById(taskId)?.Name ?? "未知任务";
        }

        private void BtnNewDetection_Click(object sender, RoutedEventArgs e) => NavigateTo("Detection");
        private void BtnManageProducts_Click(object sender, RoutedEventArgs e) => NavigateTo("Products");
        private void BtnViewTasks_Click(object sender, RoutedEventArgs e) => NavigateTo("Tasks");

        private void NavigateTo(string target)
        {
            _eventAggregator.GetEvent<NavigationRequestedEvent>().Publish(new NavigationRequestedEventArgs(target));
        }
    }

    /// <summary>仪表盘"最近检测"列表的展示模型（DetectionRecord 不含任务名/结果文本）</summary>
    public class RecentDetectionItem
    {
        public DateTime DetectionTime { get; set; }
        public string TaskName { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public long ElapsedMs { get; set; }
    }
}