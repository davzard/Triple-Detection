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
using TripleDetection.Application.Services;
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
        private readonly ObservableCollection<TabItemViewModel> _tabItems = new ObservableCollection<TabItemViewModel>();

        private bool _isNavExpanded = true;
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();

        public MainWindow(MainViewModel viewModel, IRegionManager regionManager, LoggingService logService, IEventAggregator eventAggregator)
        {
            _viewModel = viewModel;
            _regionManager = regionManager;
            _logService = logService;
            _eventAggregator = eventAggregator;

            InitializeComponent();

            tabBar.ItemsSource = _tabItems;
            DataContext = _viewModel;

            _navButtons["Dashboard"] = btnNavDashboard;
            _navButtons["Detection"] = btnNavDetection;
            _navButtons["Products"] = btnNavProducts;
            _navButtons["Tasks"] = btnNavTasks;
            _navButtons["Logs"] = btnNavLogs;
            _navButtons["Settings"] = btnNavSettings;
            _navButtons["UserManagement"] = btnNavUserManagement;
            _navButtons["AuditLog"] = btnNavAuditLog;
            _navButtons["DetectionHistory"] = btnNavDetectionHistory;

            btnToggleNav.IsChecked = _isNavExpanded;

            // 订阅导航请求事件（仪表盘等视图的快捷操作按钮通过事件触发完整导航）
            _eventAggregator.GetEvent<NavigationRequestedEvent>()
                .Subscribe(args => NavigateToView(args.Target));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLogo();
            txtUsername.Text = SessionManager.CurrentUserName;
            UpdateStatusBar();
            _logService.Log("Application started");

            // 默认导航到仪表盘
            if (_regionManager != null)
                _regionManager.RequestNavigate("MainContentRegion", "Dashboard");
            OpenTab("Dashboard", "📊 仪表盘");
            StartStatusBarTimer();
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
