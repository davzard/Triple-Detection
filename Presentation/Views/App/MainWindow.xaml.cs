using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Prism.Commands;
using Prism.Events;
using Prism.Navigation.Regions;
using TripleDetection.Application.Line;
using TripleDetection.Application.Security;
using TripleDetection.Application.Services;
using TripleDetection.Infrastructure.Device;
using TripleDetection.Presentation.Events;
using TripleDetection.Presentation.ViewModels;
using TripleDetection.Presentation.ViewModels.Detection;

namespace TripleDetection.Presentation.Views.App
{
    public partial class MainWindow : Window
    {
        private readonly LoggingService _logService;
        private readonly MainViewModel _viewModel;
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILineController _lineController;
        private readonly IPlcGateway _plcGateway;
        private readonly IAuthorizationService? _authorization;
        private readonly ObservableCollection<TabItemViewModel> _tabItems = new ObservableCollection<TabItemViewModel>();

        private bool _isNavExpanded = true;
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();

        public MainWindow(MainViewModel viewModel, IRegionManager regionManager, LoggingService logService,
            IEventAggregator eventAggregator, ILineController lineController, IPlcGateway plcGateway,
            IAuthorizationService? authorization = null)
        {
            _viewModel = viewModel;
            _regionManager = regionManager;
            _logService = logService;
            _eventAggregator = eventAggregator;
            _lineController = lineController;
            _plcGateway = plcGateway;
            _authorization = authorization;

            InitializeComponent();

            tabBar.ItemsSource = _tabItems;
            DataContext = _viewModel;

            _navButtons["Dashboard"] = btnNavDashboard;
            _navButtons["Detection"] = btnNavDetection;
            _navButtons["Line"] = btnNavLine;
            _navButtons["Products"] = btnNavProducts;
            _navButtons["Tasks"] = btnNavTasks;
            _navButtons["Logs"] = btnNavLogs;
            _navButtons["Settings"] = btnNavSettings;
            _navButtons["UserManagement"] = btnNavUserManagement;
            _navButtons["AuditLog"] = btnNavAuditLog;
            _navButtons["DetectionHistory"] = btnNavDetectionHistory;
            _navButtons["Spc"] = btnNavSpc;
            _navButtons["ShiftReport"] = btnNavShiftReport;

            btnToggleNav.IsChecked = _isNavExpanded;

            // 订阅导航请求事件（仪表盘等视图的快捷操作按钮通过事件触发完整导航）
            _eventAggregator.GetEvent<NavigationRequestedEvent>()
                .Subscribe(args => NavigateToView(args.Target));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLogo();
            txtUsername.Text = SessionManager.CurrentUserId == 0
                ? SessionManager.CurrentUserName
                : $"{SessionManager.CurrentUserName}（{SessionManager.CurrentRole.ToDisplay()}）";
            ApplyRoleGating();
            UpdateStatusBar();
            _logService.Log("Application started");

            // 默认导航到仪表盘
            if (_regionManager != null)
                _regionManager.RequestNavigate("MainContentRegion", "Dashboard");
            OpenTab("Dashboard", "📊 仪表盘");
            StartStatusBarTimer();
        }

        /// <summary>
        /// M6 RBAC：按当前角色隐藏无权限的导航入口（体验层；安全层由 Service 强制校验兜底）。
        /// 操作员：隐藏 产品/任务/参数设置/审计日志；工程师：再隐藏 用户管理。
        /// </summary>
        private void ApplyRoleGating()
        {
            var authz = _authorization;
            if (authz == null) return;
            btnNavProducts.Visibility = authz.Can(AuthorizationAction.ProductManage) ? Visibility.Visible : Visibility.Collapsed;
            btnNavTasks.Visibility = authz.Can(AuthorizationAction.TaskManage) ? Visibility.Visible : Visibility.Collapsed;
            btnNavSettings.Visibility = authz.Can(AuthorizationAction.SettingsModify) ? Visibility.Visible : Visibility.Collapsed;
            btnNavUserManagement.Visibility = authz.Can(AuthorizationAction.UserManage) ? Visibility.Visible : Visibility.Collapsed;
            btnNavAuditLog.Visibility = authz.Can(AuthorizationAction.AuditView) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadLogo()
        {
            var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
            if (File.Exists(logoPath))
            {
                try { imgLogo.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute)); }
                catch { /* logo 加载失败不影响主界面 */ }
            }
        }

        private void StartStatusBarTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => UpdateStatusBar();
            timer.Start();
        }

        private void UpdateStatusBar()
        {
            txtCurrentTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            txtCurrentUser.Text = $"当前用户: {txtUsername.Text}";
            try
            {
                var state = _lineController?.State ?? LineState.Idle;
                var stateText = state switch
                {
                    LineState.Idle => "未开班",
                    LineState.Ready => "待启动",
                    LineState.Running => "运行中",
                    LineState.Paused => "已暂停",
                    LineState.Alarm => $"报警[{_lineController.AlarmCode}]",
                    _ => state.ToString()
                };
                var plcText = _plcGateway?.IsConnected == true ? "PLC已连" : "PLC断开";
                txtLineStatus.Text = $"产线: {stateText} | {plcText}";
            }
            catch { /* 状态栏刷新失败不影响主界面 */ }
        }

        private void BtnNav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
                NavigateToView(tag);
        }

        private void NavigateToView(string tag)
        {
            if (_regionManager != null)
            {
                _regionManager.RequestNavigate("MainContentRegion", tag);
            }

            UpdateNavButtonStyles(tag);
            OpenTab(tag, GetViewDisplayName(tag));
            txtStatus.Text = $"当前: {tag}";
            _logService.Log($"导航到: {tag}");
        }

        private string GetViewDisplayName(string tag)
        {
            switch (tag)
            {
                case "Dashboard": return "📊 仪表盘";
                case "Detection": return "🔍 检测执行";
                case "Line": return "🏭 产线集成";
                case "Products": return "📦 产品管理";
                case "Tasks": return "📋 任务管理";
                case "Logs": return "📝 操作日志";
                case "Settings": return "⚙️ 系统配置";
                case "UserManagement": return "👤 用户权限";
                case "AuditLog": return "📋 审计日志";
                case "DetectionHistory": return "📜 检测记录";
                default: return tag;
            }
        }

        private void OpenTab(string tag, string displayName)
        {
            var existing = _tabItems.FirstOrDefault(t => t.Tag == tag);
            if (existing != null)
            {
                foreach (var item in _tabItems)
                    item.IsActive = item.Tag == tag;
                return;
            }

            foreach (var item in _tabItems)
                item.IsActive = false;

            _tabItems.Add(new TabItemViewModel
            {
                Tag = tag,
                DisplayName = displayName,
                IsActive = true,
                IsClosable = tag != "Dashboard",
                SelectCommand = new DelegateCommand<object>(param => SelectTab(param as string)),
                CloseCommand = new DelegateCommand<object>(param => CloseTab(param as string))
            });
        }

        private void SelectTab(string tag)
        {
            if (!string.IsNullOrEmpty(tag))
                NavigateToView(tag);
        }

        private void CloseTab(string tag)
        {
            if (string.IsNullOrEmpty(tag) || _tabItems.Count <= 1)
                return;

            var item = _tabItems.FirstOrDefault(t => t.Tag == tag);
            if (item != null)
            {
                bool wasActive = item.IsActive;
                _tabItems.Remove(item);

                if (wasActive)
                {
                    var nextTab = _tabItems.FirstOrDefault();
                    if (nextTab != null)
                        NavigateToView(nextTab.Tag);
                }
            }
        }

        private void UpdateNavButtonStyles(string activeTag)
        {
            foreach (var kvp in _navButtons)
            {
                kvp.Value.Style = kvp.Key == activeTag
                    ? (Style)FindResource("NavButtonActiveStyle")
                    : (Style)FindResource("NavButtonStyle");
            }
        }

        private void BtnToggleNav_Click(object sender, RoutedEventArgs e)
        {
            _isNavExpanded = !_isNavExpanded;
            if (_isNavExpanded)
            {
                navColumn.Width = new GridLength(200);
                foreach (var btn in _navButtons.Values)
                    btn.HorizontalContentAlignment = HorizontalAlignment.Left;
            }
            else
            {
                navColumn.Width = new GridLength(48);
                foreach (var btn in _navButtons.Values)
                    btn.HorizontalContentAlignment = HorizontalAlignment.Center;
            }
        }

        private void BtnNotifications_Click(object sender, RoutedEventArgs e)
        {
            NavigateToView("Logs");
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确认退出系统？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _logService.Log("用户退出系统");
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _logService.Log("Application closing");
        }
    }
}
