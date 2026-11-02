using System;
using System.IO;
using System.Linq;
using TripleDetection.Application.Line;
using TripleDetection.Application.Services;
using TripleDetection.Application.VmServices;
using TripleDetection.Infrastructure.Persistence;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// 班次服务测试（Phase 2，设计文档 §4.1 Shifts 表）：
    /// 开班创建记录、检测增量统计、下班关闭（写 EndAt）。
    /// </summary>
    public class ShiftServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly TripleDetectionDbContext _db;

        public ShiftServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "td_shift_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var dbPath = Path.Combine(_dir, "shift.db");
            DatabaseInitializer.EnsureDatabaseCreated(dbPath);
            _db = new TripleDetectionDbContext("Data Source=" + dbPath);
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { Directory.Delete(_dir, true); } catch { }
        }

        private ShiftService CreateService() =>
            new ShiftService(_db, new LoggingService(Path.Combine(_dir, "logs"), null), new StationContext(null));

        [Fact]
        public void 开班_创建班次记录()
        {
            var svc = CreateService();
            svc.OpenShift(taskId: 10, operatorId: 1);

            Assert.NotNull(svc.CurrentShiftId);
            var shift = _db.Shifts.Single();
            Assert.Equal(10, shift.TaskId);
            Assert.Equal(1, shift.OperatorId);
            Assert.Null(shift.EndAt);
            Assert.Equal(0, shift.TotalCount);
        }

        [Fact]
        public void 检测统计_增量更新计数()
        {
            var svc = CreateService();
            svc.OpenShift(10, 1);
            var id = svc.CurrentShiftId.Value;

            svc.RecordResult(true);
            svc.RecordResult(true);
            svc.RecordResult(false);

            var shift = _db.Shifts.Single(s => s.Id == id);
            Assert.Equal(3, shift.TotalCount);
            Assert.Equal(2, shift.OkCount);
            Assert.Equal(1, shift.NgCount);
        }

        [Fact]
        public void 下班_关闭班次写EndAt()
        {
            var svc = CreateService();
            svc.OpenShift(10, 1);
            var id = svc.CurrentShiftId.Value;

            svc.CloseShift();

            Assert.Null(svc.CurrentShiftId);
            var shift = _db.Shifts.Single(s => s.Id == id);
            Assert.NotNull(shift.EndAt);
            Assert.True(shift.EndAt.Value >= shift.StartAt);
        }

        [Fact]
        public void 未开班_统计与关闭为空操作()
        {
            var svc = CreateService();
            svc.RecordResult(true);   // 不应抛异常
            svc.CloseShift();         // 不应抛异常
            Assert.Empty(_db.Shifts);
        }
    }
}
