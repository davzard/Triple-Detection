using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TripleDetection.Application.Line;
using TripleDetection.Application.Services;
using TripleDetection.Application.VmServices;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Entities.Queries;
using TripleDetection.Domain.Enums;
using TripleDetection.Infrastructure.Device;
using TripleDetection.Infrastructure.Persistence;
using TripleDetection.Presentation.Models;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// 产线编排器端到端测试（Mock PLC + 确定性引擎）：
    /// 开班上下文、触发→检测→判定/剔废、超时策略、连续 NG 停线、换型、断线。
    /// </summary>
    public class LineOrchestratorTests : IDisposable
    {
        private readonly string _dir;

        public LineOrchestratorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "td_orch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // ===== 测试替身 =====

        private class StubDetectionEngine : IDetectionEngine
        {
            public enum Mode { Ok, Ng, NeverReturn }
            public Mode EngineMode = Mode.Ok;
            public volatile bool SolutionLoaded;
            public int RunOnceCount;
            public (int TaskId, int ProductId, string Batch) Context;
            public readonly Dictionary<string, string> Globals = new Dictionary<string, string>();

            public event EventHandler<DetectionResult> DetectionResult;
            public event EventHandler SolutionCrashed;
            public bool IsSolutionLoaded => SolutionLoaded;
            public bool HasProcedure => true;
            public bool IsContinuousRun => false;

            public void SimulateCrash()
            {
                SolutionLoaded = false;
                SolutionCrashed?.Invoke(this, EventArgs.Empty);
            }

            public void LoadSolution(string solPath) => SolutionLoaded = true;
            public List<string> GetAllProcedureNames() => new List<string> { "P1" };
            public void SetProcedure(string procedureName) { }
            public void SetCurrentTaskContext(int taskId, int productId, string batchNumber, DateTime? productionDate, DateTime? expirationDate)
                => Context = (taskId, productId, batchNumber);

            public void RunOnce()
            {
                Interlocked.Increment(ref RunOnceCount);
                if (EngineMode == Mode.NeverReturn) return; // 模拟相机/方案卡死
                DetectionResult?.Invoke(this, EngineMode == Mode.Ok
                    ? new DetectionResult { IsOK = true, ElapsedMs = 50 }
                    : new DetectionResult { IsOK = false, NgReason = NgReason.OcrFailed, NgDetail = "test", ElapsedMs = 50 });
            }

            public void SetContinuousRun(bool enable) { }
            public void Stop() { }
            public void SetGlobalVariableString(string name, string value) => Globals[name] = value;
            public string GetGlobalVariableString(string name) => Globals.TryGetValue(name, out var v) ? v : null;
        }

        private class StubTaskService : ITaskService
        {
            public readonly List<ProdTask> Tasks = new List<ProdTask>();
            public IEnumerable<ProdTask> GetAll() => Tasks;
            public IEnumerable<ProdTask> GetByStatus(TaskStatus status) => Tasks.Where(t => t.Status == status);
            public ProdTask GetById(int id) => Tasks.FirstOrDefault(t => t.Id == id);
            public void Create(ProdTask task, string createBy, int currentUserId) { }
            public void Update(ProdTask task, string updateBy, int currentUserId) { }
            public void Approve(int id, string reviewedBy, int currentUserId) { }
            public void UpdateStatus(int id, TaskStatus status, string updateBy, int currentUserId) { }
            public void Delete(int id, string updateBy, int currentUserId) { }
            public IPagedResult<ProdTask> Query(PagedQuery query) => throw new NotSupportedException();
            public IPagedResult<ProdTask> Query(TaskQuery query) => throw new NotSupportedException();
        }

        private class StubProductService : IProductService
        {
            public readonly List<Product> Products = new List<Product>();
            public IEnumerable<Product> GetAll() => Products;
            public Product GetById(int id) => Products.FirstOrDefault(p => p.Id == id);
            public void Create(Product product, string createBy, int currentUserId) { }
            public void Update(Product product, string updateBy, int currentUserId) { }
            public void Delete(int id, string updateBy, int currentUserId) { }
            public IPagedResult<Product> Query(PagedQuery query) => throw new NotSupportedException();
            public IPagedResult<Product> Query(ProductQuery query) => throw new NotSupportedException();
        }

        private class StubRecordService : IDetectionRecordService
        {
            public readonly List<DetectionRecord> Saved = new List<DetectionRecord>();
            public void Save(DetectionRecord record) => Saved.Add(record);
            public IPagedResult<DetectionRecord> Query(DetectionRecordQuery query) => throw new NotSupportedException();
            public IEnumerable<DetectionRecord> Export(DetectionRecordQuery query) => Saved;
        }

        // ===== 装配 =====

        private class Fixture
        {
            public LineOrchestrator Orchestrator;
            public MockPlcGateway Plc;
            public LineController Line;
            public StubDetectionEngine Engine;
            public StubTaskService Tasks;
            public StubProductService Products;
            public StubRecordService Records;
            public SystemConfigService Config;
            public DetectionWatchdog Watchdog;
        }

        private Fixture Create(string onTimeout = "TreatAsNG", int timeoutMs = 5000, int ngThreshold = 3, int maxReloadAttempts = 2)
        {
            var log = new LoggingService(_dir, null);
            var dbPath = Path.Combine(_dir, "orch.db");
            DatabaseInitializer.EnsureDatabaseCreated(dbPath); // 建表（含 SystemConfigs）
            var db = new TripleDetectionDbContext("Data Source=" + dbPath);
            var config = new SystemConfigService(db, log);
            config.EnsureDefaults();
            config.Set(SystemConfigService.CatDetection, SystemConfigService.KeyCaptureTimeoutMs, timeoutMs.ToString());
            config.Set(SystemConfigService.CatDetection, SystemConfigService.KeyOnCaptureTimeout, onTimeout);
            config.Set(SystemConfigService.CatRecovery, SystemConfigService.KeyMaxReloadAttempts, maxReloadAttempts.ToString());

            var plc = new MockPlcGateway(log);
            var line = new LineController(log, ngThreshold, "StopLine");
            var engine = new StubDetectionEngine();
            var watchdog = new DetectionWatchdog();
            var tasks = new StubTaskService();
            var products = new StubProductService();
            var records = new StubRecordService();
            var station = new StationContext(null);

            var orch = new LineOrchestrator(plc, line, engine, watchdog, config, log, records, tasks, products, station);
            orch.Start();

            return new Fixture { Orchestrator = orch, Plc = plc, Line = line, Engine = engine, Tasks = tasks, Products = products, Records = records, Config = config, Watchdog = watchdog };
        }

        private static (Product product, ProdTask task) SeedShiftData(StubProductService products, StubTaskService tasks)
        {
            var product = new Product { Id = 1, Code = "P001", Name = "产品1", SolFilePath = "x.sol" };
            var task = new ProdTask
            {
                Id = 10,
                Name = "任务1",
                ProductId = 1,
                Status = TaskStatus.Approved,
                BatchNumber = "B1",
                ProductionDate = new DateTime(2026, 11, 1),
                ExpirationDate = new DateTime(2027, 11, 1)
            };
            products.Products.Add(product);
            tasks.Tasks.Add(task);
            return (product, task);
        }

        private static bool WaitFor(Func<bool> cond, int ms = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                if (cond()) return true;
                Thread.Sleep(20);
            }
            return cond();
        }

        // ===== 开班 =====

        [Fact]
        public void StartShift_ValidTask_TransitionsToReady_SetsEngineContext()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);

            Assert.True(f.Orchestrator.StartShift(10));

            Assert.Equal(LineState.Ready, f.Line.State);
            Assert.True(f.Engine.IsSolutionLoaded);
            Assert.Equal((10, 1, "B1"), f.Engine.Context);
            // 三期 ToVM
            Assert.Equal("B1", f.Engine.Globals["BN"]);
            Assert.Equal("20261101", f.Engine.Globals["Mfg"]);
            Assert.Equal("20271101", f.Engine.Globals["EXP"]);
            // STATION_STATUS 异步写入 0（非运行）
            Assert.True(WaitFor(() => f.Plc.LastStationStatus == 0));
        }

        [Fact]
        public void StartShift_PendingTask_ReturnsFalse_StaysIdle()
        {
            var f = Create();
            var (_, task) = SeedShiftData(f.Products, f.Tasks);
            task.Status = TaskStatus.Pending;

            Assert.False(f.Orchestrator.StartShift(10));
            Assert.Equal(LineState.Idle, f.Line.State);
        }

        [Fact]
        public void StartShift_UnknownTask_ReturnsFalse()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);

            Assert.False(f.Orchestrator.StartShift(999));
            Assert.Equal(LineState.Idle, f.Line.State);
        }

        // ===== 触发 → 检测 → 判定 =====

        [Fact]
        public void Trigger_OkResult_WritesOk_NoReject()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateTrigger();

            Assert.Equal(1, f.Engine.RunOnceCount);
            Assert.True(WaitFor(() => f.Plc.LastResult == DetectionVerdict.Ok));
            Assert.Equal(0, f.Plc.LastRejectDelayMs); // OK 不剔废
        }

        [Fact]
        public void Trigger_NgResult_WritesNgAndReject()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);
            f.Engine.EngineMode = StubDetectionEngine.Mode.Ng;
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateTrigger();

            Assert.True(WaitFor(() => f.Plc.LastResult == DetectionVerdict.Ng));
            Assert.True(WaitFor(() => f.Plc.LastRejectDelayMs == 50 && f.Plc.LastRejectDurationMs == 200));
        }

        [Fact]
        public void Trigger_WhenNotRunning_Ignored()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);
            f.Orchestrator.StartShift(10); // Ready，未自动运行

            f.Plc.SimulateTrigger();

            Assert.Equal(0, f.Engine.RunOnceCount);
            Assert.Equal(DetectionVerdict.Idle, f.Plc.LastResult);
        }

        // ===== 超时策略 =====

        [Fact]
        public void Trigger_Timeout_TreatAsNG_SavesTimeoutRecord_AndRejects()
        {
            var f = Create(onTimeout: "TreatAsNG", timeoutMs: 300);
            SeedShiftData(f.Products, f.Tasks);
            f.Engine.EngineMode = StubDetectionEngine.Mode.NeverReturn;
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateTrigger();

            Assert.True(WaitFor(() => f.Records.Saved.Count == 1, 5000), "应合成超时 NG 记录");
            var rec = f.Records.Saved[0];
            Assert.False(rec.IsOK);
            Assert.Equal(NgReason.Timeout, rec.NgReason);
            Assert.Equal(10, rec.TaskId);
            Assert.Equal("B1", rec.BatchNumber);

            Assert.True(WaitFor(() => f.Plc.LastResult == DetectionVerdict.Ng));
            Assert.True(WaitFor(() => f.Plc.LastRejectDelayMs == 50));
            // 单次超时不停线
            Assert.Equal(LineState.Running, f.Line.State);
        }

        [Fact]
        public void Trigger_Timeout_IgnorePolicy_NoRecord_NoReject()
        {
            var f = Create(onTimeout: "Ignore", timeoutMs: 300);
            SeedShiftData(f.Products, f.Tasks);
            f.Engine.EngineMode = StubDetectionEngine.Mode.NeverReturn;
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateTrigger();

            Thread.Sleep(800); // 等超时触发
            Assert.Empty(f.Records.Saved);
            Assert.Equal(DetectionVerdict.Idle, f.Plc.LastResult);
            Assert.Equal(0, f.Plc.LastRejectDelayMs);
        }

        [Fact]
        public void Trigger_Timeout_StopLinePolicy_RaisesAlarm()
        {
            var f = Create(onTimeout: "StopLine", timeoutMs: 300);
            SeedShiftData(f.Products, f.Tasks);
            f.Engine.EngineMode = StubDetectionEngine.Mode.NeverReturn;
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateTrigger();

            Assert.True(WaitFor(() => f.Line.State == LineState.Alarm, 5000));
            Assert.Equal(LineAlarmCode.ConsecutiveTimeout, f.Line.AlarmCode);
            Assert.True(WaitFor(() => f.Plc.LastLineStopAlarmCode == (int)LineAlarmCode.ConsecutiveTimeout));
        }

        // ===== 连续 NG 停线 =====

        [Fact]
        public void ThreeNg_Alarm1001_WritesLineStop()
        {
            var f = Create(ngThreshold: 3);
            SeedShiftData(f.Products, f.Tasks);
            f.Engine.EngineMode = StubDetectionEngine.Mode.Ng;
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            for (int i = 0; i < 3; i++)
                f.Plc.SimulateTrigger(); // 引擎同步回调，_detecting 已清零，可连续触发

            Assert.True(WaitFor(() => f.Line.State == LineState.Alarm));
            Assert.Equal(LineAlarmCode.ConsecutiveNg, f.Line.AlarmCode);
            Assert.True(WaitFor(() => f.Plc.LastLineStopAlarmCode == (int)LineAlarmCode.ConsecutiveNg));
        }

        // ===== 换型 =====

        [Fact]
        public void RecipeChange_KnownProduct_SwitchesContext_AndResumes()
        {
            var f = Create();
            var (_, task1) = SeedShiftData(f.Products, f.Tasks);
            f.Products.Products.Add(new Product { Id = 2, Code = "P002", Name = "产品2", SolFilePath = "y.sol" });
            f.Tasks.Tasks.Add(new ProdTask
            {
                Id = 20,
                Name = "任务2",
                ProductId = 2,
                Status = TaskStatus.Approved,
                BatchNumber = "B2",
                ProductionDate = new DateTime(2026, 11, 2),
                ExpirationDate = new DateTime(2027, 11, 2)
            });
            f.Orchestrator.StartShift(task1.Id);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateRecipeChange(2); // 按产品 Id 匹配

            Assert.True(WaitFor(() => f.Engine.Context.TaskId == 20));
            Assert.Equal("B2", f.Engine.Globals["BN"]);
            Assert.Equal(LineState.Running, f.Line.State); // 换型后自动恢复
        }

        [Fact]
        public void RecipeChange_UnknownRecipe_RaisesAlarm5001()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateRecipeChange(999);

            Assert.True(WaitFor(() => f.Line.State == LineState.Alarm));
            Assert.Equal(LineAlarmCode.RecipeNotFound, f.Line.AlarmCode);
            Assert.True(WaitFor(() => f.Plc.LastLineStopAlarmCode == (int)LineAlarmCode.RecipeNotFound));
        }

        // ===== PLC 断线 =====

        [Fact]
        public void PlcDisconnected_StopLinePolicy_Alarm3001()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Plc.SimulateDisconnect();

            Assert.True(WaitFor(() => f.Line.State == LineState.Alarm));
            Assert.Equal(LineAlarmCode.PlcDisconnected, f.Line.AlarmCode);
            Assert.True(WaitFor(() => f.Plc.LastLineStopAlarmCode == (int)LineAlarmCode.PlcDisconnected));
        }

        // ===== Phase 2：方案崩溃自动恢复（设计文档 §3.3.4/§3.3.5）=====

        [Fact]
        public void Crash_自动重载_产线保持运行()
        {
            var f = Create();
            SeedShiftData(f.Products, f.Tasks);
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();
            Assert.Equal(LineState.Running, f.Line.State);

            f.Engine.SimulateCrash();

            Assert.True(WaitFor(() => f.Engine.SolutionLoaded), "方案应被自动重载");
            Assert.Equal(LineState.Running, f.Line.State);
            Assert.Equal(0, f.Line.AlarmCode);
        }

        [Fact]
        public void Crash_超过重载上限_Alarm2002()
        {
            var f = Create(maxReloadAttempts: 1);
            SeedShiftData(f.Products, f.Tasks);
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Engine.SimulateCrash(); // 第 1 次：在限额内，重载成功
            Assert.True(WaitFor(() => f.Engine.SolutionLoaded));
            Assert.Equal(LineState.Running, f.Line.State);

            f.Engine.SimulateCrash(); // 第 2 次：超过限额 → Alarm 2002
            Assert.True(WaitFor(() => f.Line.State == LineState.Alarm));
            Assert.Equal(LineAlarmCode.SolutionCrash, f.Line.AlarmCode);
            Assert.True(WaitFor(() => f.Plc.LastLineStopAlarmCode == (int)LineAlarmCode.SolutionCrash));
        }

        [Fact]
        public void Crash_恢复后正常结果_重置崩溃计数()
        {
            var f = Create(maxReloadAttempts: 1);
            SeedShiftData(f.Products, f.Tasks);
            f.Orchestrator.StartShift(10);
            f.Orchestrator.StartAutoRun();

            f.Engine.SimulateCrash(); // 第 1 次崩溃：重载成功（计数=1，达上限）
            Assert.True(WaitFor(() => f.Engine.SolutionLoaded));

            // 一次正常检测结果 → 计数清零
            f.Plc.SimulateTrigger();
            Assert.True(WaitFor(() => f.Engine.RunOnceCount >= 1));

            // 再次崩溃：计数已重置，应能再次重载而非直接报警
            f.Engine.SimulateCrash();
            Assert.True(WaitFor(() => f.Engine.SolutionLoaded));
            Assert.Equal(LineState.Running, f.Line.State);
            Assert.Equal(0, f.Line.AlarmCode);
        }
    }
}
