using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DryIoc;
using Prism.DryIoc;
using Prism.Events;
using Prism.Ioc;
using TripleDetection.Application.Line;
using TripleDetection.Application.Security;
using TripleDetection.Application.Services;
using TripleDetection.Application.SettingsServices;
using TripleDetection.Application.VmServices;
using TripleDetection.Domain.Repositories;
using TripleDetection.Infrastructure.Device;
using TripleDetection.Infrastructure.Persistence;
using TripleDetection.Infrastructure.Repositories;
using TripleDetection.Presentation.ViewModels;
using TripleDetection.Presentation.ViewModels.Auth;
using TripleDetection.Presentation.ViewModels.Detection;
using TripleDetection.Presentation.ViewModels.Production;
using TripleDetection.Presentation.ViewModels.Settings;
using TripleDetection.Presentation.Views;
using TripleDetection.Presentation.Views.App;
using TripleDetection.Presentation.Views.Audit;
using TripleDetection.Presentation.Views.Auth;
using TripleDetection.Presentation.Views.Detection;
using TripleDetection.Presentation.Views.Production;
using TripleDetection.Presentation.Views.Quality;
using TripleDetection.Presentation.Views.Settings;

public partial class App : PrismApplication
{
    private static Mutex _mutex;

    // Phase 2 服务实例（RegisterTypes 中赋值，启动/退出时直接使用）
    private CameraHealthMonitor _cameraHealthMonitor;
    private ShiftService _shiftService;
    private LineMaintenanceService _lineMaintenance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "TripleDetectionApp_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("应用程序已在运行中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        InitializeDatabase();
        base.OnStartup(e);
    }

    protected override Window CreateShell() => Container.Resolve<MainWindow>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // Prism 9：containerRegistry 是 DryIocContainerExtension，底层 DryIoc 容器通过 Instance 属性获取
        var container = (containerRegistry as Prism.Container.DryIoc.DryIocContainerExtension)?.Instance;
        if (container == null) return;

        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "tripledetection.db");
        var connectionString = $"Data Source={dbPath}";

        // Infrastructure - Connection and Repository Factory
        container.RegisterInstance<IDbConnectionFactory>(new SqliteConnectionFactory(connectionString));
        container.RegisterInstance<IRepositoryFactory>(new SqliteRepositoryFactory(connectionString));
        // DbContext 供各仓储构造函数注入（仓储仅使用其连接字符串，单实例安全）
        container.RegisterInstance(new TripleDetectionDbContext(connectionString));

        // Infrastructure - Repositories (transient)
        container.Register(typeof(IRepository<>), typeof(SqliteRepository<>), Reuse.Transient);
        container.Register<IAuditLogRepository, AuditLogRepository>(Reuse.Transient);
        container.Register<IDetectionRecordRepository, DetectionRecordRepository>(Reuse.Transient);

        // Logging Service (singleton)
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        var eventAggregator = Container.Resolve<IEventAggregator>();
        var loggingService = new LoggingService(logPath, eventAggregator);
        container.RegisterInstance(loggingService);

        // 工位上下文（单例：顺序号进程内计数）与图片归档器（生产级命名，见 ImageArchiver）
        container.Register<StationContext>(Reuse.Singleton);
        container.RegisterInstance(new ImageArchiver(eventAggregator, loggingService));

        // Application Services (transient)
        container.Register<IPasswordHashService, PasswordHashService>(Reuse.Transient);
        container.Register<IUserService, UserService>(Reuse.Transient);
        container.Register<IProductService, ProductService>(Reuse.Transient);
        container.Register<ITaskService, TaskService>(Reuse.Transient);
        container.Register<IAuditLogService, AuditLogService>(Reuse.Transient);

        // M6 RBAC：授权服务（单例；越权尝试写审计日志）。
        // 须在 IAuditLogService 之后注册并解析（其依赖审计服务）。
        container.Register<IAuthorizationService, AuthorizationService>(Reuse.Singleton);

        // 系统配置（SystemConfigs 表，启动时幂等补种默认值）
        container.RegisterInstance(new SystemConfigService((TripleDetectionDbContext)container.Resolve<TripleDetectionDbContext>(), loggingService,
            container.Resolve<IAuthorizationService>()));
        container.Resolve<SystemConfigService>().EnsureDefaults();
        container.Register<IDetectionRecordService, DetectionRecordService>(Reuse.Transient);
        container.Register<CommunicationSettingsService>(Reuse.Transient);
        container.Register<VmSettingsService>(Reuse.Transient);
        container.Register<SystemSettingsService>(Reuse.Transient);
        container.Register<DeviceControlSettingsService>(Reuse.Transient);
        container.Register<SettingsSyncService>(Reuse.Transient);
        // M7 SPC/班次报表统计服务（transient；仅读取 DetectionRecords/Shifts）
        container.Register<SpStatisticsService>(Reuse.Transient);

        // M8 HMI：蜂鸣器 + 声音编排（singleton；LineOrchestrator 构造注入）
        container.Register<IAudioBeeper, WavToneBeeper>(Reuse.Singleton);
        container.Register<HmiSoundService>(Reuse.Singleton);

        // 检测引擎 (singleton)：有 VisionMaster SDK 时用真实引擎，否则用模拟引擎
#if VM_SDK
        container.Register<IDetectionEngine, VmIntegrationService>(Reuse.Singleton);
#else
        container.Register<IDetectionEngine, MockDetectionEngine>(Reuse.Singleton);
#endif

        // 产线集成（Phase 1）：PLC 网关 + 状态机 + 看门狗 + 编排器
        var configService = container.Resolve<SystemConfigService>();
        var plcMode = configService.GetString(SystemConfigService.CatPlc, SystemConfigService.KeyPlcMode, "mock");
        if (string.Equals(plcMode, "modbus", StringComparison.OrdinalIgnoreCase))
        {
            var mapJson = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config",
                configService.GetString(SystemConfigService.CatPlc, SystemConfigService.KeyPlcRegisterMap, "plc_register_map.json"));
            var map = PlcRegisterMap.Load(mapJson);
            container.RegisterInstance<IPlcGateway>(new ModbusTcpPlcGateway(loggingService, map,
                configService.GetString(SystemConfigService.CatPlc, SystemConfigService.KeyPlcIp, "192.168.1.200"),
                configService.GetInt(SystemConfigService.CatPlc, SystemConfigService.KeyPlcPort, 502),
                (byte)configService.GetInt(SystemConfigService.CatPlc, SystemConfigService.KeyPlcUnitId, 1),
                configService.GetInt(SystemConfigService.CatPlc, SystemConfigService.KeyPlcPollIntervalMs, 500),
                configService.GetInt(SystemConfigService.CatDetection, SystemConfigService.KeyMinTriggerIntervalMs, 500)));
        }
        else
        {
            container.RegisterInstance<IPlcGateway>(new MockPlcGateway(loggingService));
        }
        container.RegisterInstance(new DetectionWatchdog());
        var lineController = new LineController(loggingService,
            configService.GetInt(SystemConfigService.CatReject, SystemConfigService.KeyConsecutiveNgToStopLine, 10),
            configService.GetString(SystemConfigService.CatPlc, SystemConfigService.KeyPlcOnDisconnect, "StopLine"));
        container.RegisterInstance(lineController);

        // Phase 2：相机心跳监控（引擎实现 ICameraHeartbeat 时启用）
        var engineInstance = container.Resolve<IDetectionEngine>();
        if (engineInstance is ICameraHeartbeat heartbeatSource)
        {
            _cameraHealthMonitor = new CameraHealthMonitor(heartbeatSource, lineController,
                loggingService,
                configService.GetInt(SystemConfigService.CatCamera, SystemConfigService.KeyHeartbeatTimeoutMs, 30000));
            container.RegisterInstance(_cameraHealthMonitor);
        }

        // Phase 2：节拍监控 / 班次服务 / 数据维护（备份 + 图像清理）
        container.RegisterInstance(new CycleTimeMonitor(loggingService));
        _shiftService = new ShiftService(
            (TripleDetectionDbContext)container.Resolve<TripleDetectionDbContext>(),
            loggingService,
            container.Resolve<StationContext>());
        container.RegisterInstance(_shiftService);
        container.RegisterInstance(new DatabaseBackupService(dbPath, loggingService));
        container.RegisterInstance(new ImageRetentionService(loggingService));
        _lineMaintenance = new LineMaintenanceService(configService,
            container.Resolve<DatabaseBackupService>(),
            container.Resolve<ImageRetentionService>(),
            loggingService);
        container.RegisterInstance(_lineMaintenance);

        // 编排器按构造注入解析（依赖均为已注册单例/瞬态，首次 Resolve 时实例化）
        container.Register<LineOrchestrator>(Reuse.Singleton);

        // ViewModels (transient)
        container.Register<MainViewModel>(Reuse.Transient);
        container.Register<TabItemViewModel>(Reuse.Transient);
        container.Register<LoginViewModel>(Reuse.Transient);
        container.Register<UserManagementViewModel>(Reuse.Transient);
        container.Register<UserEditViewModel>(Reuse.Transient);
        container.Register<ProductListViewModel>(Reuse.Transient);
        container.Register<ProductEditViewModel>(Reuse.Transient);
        container.Register<TaskListViewModel>(Reuse.Transient);
        container.Register<TaskEditViewModel>(Reuse.Transient);
        container.Register<SettingsShellViewModel>(Reuse.Transient);

        // Navigation（Prism 9 移除了 RegisterTypeForNavigation，改用 RegisterForNavigation 扩展方法）
        containerRegistry.RegisterForNavigation<DashboardView>("Dashboard");
        containerRegistry.RegisterForNavigation<DetectionView>("Detection");
        containerRegistry.RegisterForNavigation<LineView>("Line");
        containerRegistry.RegisterForNavigation<ProductListView>("Products");
        containerRegistry.RegisterForNavigation<TaskListView>("Tasks");
        containerRegistry.RegisterForNavigation<LogsView>("Logs");
        containerRegistry.RegisterForNavigation<SettingsView>("Settings");
        containerRegistry.RegisterForNavigation<UserManagementView>("UserManagement");
        containerRegistry.RegisterForNavigation<AuditLogView>("AuditLog");
        containerRegistry.RegisterForNavigation<DetectionHistoryView>("DetectionHistory");
        // M7 质量分析（所有角色可见：只读统计）
        containerRegistry.RegisterForNavigation<SpcView>("Spc");
        containerRegistry.RegisterForNavigation<ShiftReportView>("ShiftReport");

        // MainWindow as singleton
        container.Register<MainWindow>(Reuse.Singleton);
    }

    protected override void OnInitialized()
    {
        var loginWindow = new LoginWindow();
        var result = loginWindow.ShowDialog();
        if (result != true) { Shutdown(); return; }
        if (MainWindow != null)
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
        StartLineOrchestrator();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var orchestrator = Container.Resolve<LineOrchestrator>();
            orchestrator?.Stop();
        }
        catch { /* 退出路径不阻塞 */ }
        try
        {
            // Phase 2：停心跳监控、关闭未结束的班次、退出前最后维护一次
            _cameraHealthMonitor?.Stop();
            _shiftService?.Dispose();
            _lineMaintenance?.RunMaintenance();
        }
        catch { /* 退出路径不阻塞 */ }
        base.OnExit(e);
    }

    private void StartLineOrchestrator()
    {
        try
        {
            var orchestrator = Container.Resolve<LineOrchestrator>();
            orchestrator?.Start();

            // M8：应用声音设置（静音/音量）
            try
            {
                var hmiSound = Container.Resolve<HmiSoundService>();
                var settings = Container.Resolve<SystemSettingsService>().Load();
                hmiSound.ApplySettings(settings.SoundMuted, settings.SoundVolume);
            }
            catch { /* 声音设置失败不影响产线启动 */ }

            // Phase 2：启动相机心跳监控
            _cameraHealthMonitor?.Start();

            // Phase 2：启动数据维护（DB 备份 + 图像清理），后台执行不阻塞 UI
            var maintenance = _lineMaintenance;
            if (maintenance != null)
                Task.Run(() =>
                {
                    try { maintenance.RunMaintenance(); }
                    catch { /* 维护失败仅记日志 */ }
                });
        }
        catch (Exception ex)
        {
            // 产线集成失败不阻塞主界面（可退化为纯手动检测模式）
            MessageBox.Show($"产线集成启动失败（已降级为手动模式）: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InitializeDatabase()
    {
        try
        {
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "tripledetection.db");
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            DatabaseInitializer.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"数据库初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}