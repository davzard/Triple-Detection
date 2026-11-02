using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TripleDetection.Application.Services;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain.Enums;
using TripleDetection.Infrastructure.Persistence;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>
    /// SPC/班次统计服务测试（M7，设计文档 §3.7）：
    /// 小时粒度良率分桶、NG Pareto 排序与累计占比、置信度/耗时百分位、
    /// 班次窗口统计、班次报表（以实际记录为准）、百分位边界、NG 分类文案。
    /// </summary>
    public class SpStatisticsTests : IDisposable
    {
        private readonly string _dir;
        private readonly TripleDetectionDbContext _db;
        private readonly SpStatisticsService _sp;

        public SpStatisticsTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "td_sp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var dbPath = Path.Combine(_dir, "sp.db");
            DatabaseInitializer.EnsureDatabaseCreated(dbPath);
            _db = new TripleDetectionDbContext("Data Source=" + dbPath);
            _sp = new SpStatisticsService(_db);
            Seed();
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { Directory.Delete(_dir, true); } catch { }
        }

        // 2026-01-10 08:00-09:00 班次A：2 OK + 2 NG(BnMismatch)
        // 2026-01-10 09:00-   班次B：1 OK + 3 NG(OcrFailed)
        private void Seed()
        {
            var op = new User { Username = "op01", RealName = "张三", Role = "Operator" };
            var task = new ProdTask { Name = "测试任务A", ProductId = 1, ProductionDate = new DateTime(2026, 1, 10) };
            _db.Users.Add(op);
            _db.ProdTasks.Add(task);
            _db.SaveChanges();

            var d = new DateTime(2026, 1, 10);
            void Rec(DateTime time, bool ok, NgReason reason, double? conf, long ms, bool deleted = false)
            {
                _db.DetectionRecords.Add(new DetectionRecord
                {
                    TaskId = task.Id,
                    ProductId = 1,
                    IsOK = ok,
                    NgReason = reason,
                    Confidence = conf,
                    ElapsedMs = ms,
                    DetectionTime = time,
                    OperatorId = op.Id,
                    IsDeleted = deleted
                });
            }

            Rec(d.AddHours(8).AddMinutes(5), true, NgReason.None, 0.95, 100);
            Rec(d.AddHours(8).AddMinutes(15), true, NgReason.None, 0.90, 200);
            Rec(d.AddHours(8).AddMinutes(25), false, NgReason.BnMismatch, 0.50, 300);
            Rec(d.AddHours(8).AddMinutes(35), false, NgReason.BnMismatch, null, 400);
            Rec(d.AddHours(9).AddMinutes(5), true, NgReason.None, 0.80, 500);
            Rec(d.AddHours(9).AddMinutes(15), false, NgReason.OcrFailed, null, 1000);
            Rec(d.AddHours(9).AddMinutes(25), false, NgReason.OcrFailed, null, 2000);
            Rec(d.AddHours(9).AddMinutes(35), false, NgReason.OcrFailed, null, 3000);
            // 窗口外 + 已删除：均不应被统计
            Rec(new DateTime(2026, 1, 9).AddHours(8), true, NgReason.None, 0.99, 50);
            Rec(d.AddHours(8).AddMinutes(50), true, NgReason.None, 0.99, 50, deleted: true);
            _db.SaveChanges();

            _db.Shifts.Add(new Shift
            {
                StartAt = d.AddHours(8),
                EndAt = d.AddHours(9),
                OperatorId = op.Id,
                TaskId = task.Id,
                StopDurationMs = 1234
            });
            _db.Shifts.Add(new Shift
            {
                StartAt = d.AddHours(9),
                EndAt = null,
                OperatorId = op.Id,
                TaskId = task.Id
            });
            _db.SaveChanges();
        }

        private (DateTime from, DateTime to) Day() =>
            (new DateTime(2026, 1, 10), new DateTime(2026, 1, 11));

        [Fact]
        public void 良率趋势_按小时分桶()
        {
            var (from, to) = Day();
            var trend = _sp.GetYieldTrend(from, to);

            Assert.Equal(2, trend.Count);
            Assert.Equal(new DateTime(2026, 1, 10, 8, 0, 0), trend[0].Time);
            Assert.Equal(4, trend[0].Total);
            Assert.Equal(2, trend[0].Ok);
            Assert.Equal(50.0, trend[0].YieldPercent, 2);
            Assert.Equal(new DateTime(2026, 1, 10, 9, 0, 0), trend[1].Time);
            Assert.Equal(4, trend[1].Total);
            Assert.Equal(1, trend[1].Ok);
            Assert.Equal(25.0, trend[1].YieldPercent, 2);
        }

        [Fact]
        public void NgPareto_降序且累计占比()
        {
            var (from, to) = Day();
            var pareto = _sp.GetNgPareto(from, to);

            Assert.Equal(2, pareto.Count);
            Assert.Equal(NgReason.OcrFailed, pareto[0].Reason);
            Assert.Equal("OCR识别失败", pareto[0].ReasonText);
            Assert.Equal(3, pareto[0].Count);
            Assert.Equal(60.0, pareto[0].CumulativePercent, 1);
            Assert.Equal(NgReason.BnMismatch, pareto[1].Reason);
            Assert.Equal("批次号不符", pareto[1].ReasonText);
            Assert.Equal(2, pareto[1].Count);
            Assert.Equal(100.0, pareto[1].CumulativePercent, 1);
        }

        [Fact]
        public void Spc摘要_置信度与耗时百分位()
        {
            var (from, to) = Day();
            var s = _sp.GetSpcSummary(from, to);

            Assert.Equal(8, s.SampleCount);
            // 置信度仅 4 条非空：0.95/0.90/0.50/0.80
            Assert.Equal(0.7875, s.ConfidenceMean.Value, 4);
            // P5: idx=(4-1)*0.05=0.15 → 0.50*0.85 + 0.80*0.15 = 0.545
            Assert.Equal(0.545, s.ConfidenceP5.Value, 4);
            // 耗时 P50: idx=7*0.5=3.5 → 400*0.5+500*0.5=450
            Assert.Equal(450, s.CycleP50Ms);
            // 耗时 P99: idx=7*0.99=6.93 → 2000*0.07+3000*0.93=2930
            Assert.Equal(2930, s.CycleP99Ms);
        }

        [Fact]
        public void 最近班次_窗口内统计且倒序()
        {
            var shifts = _sp.GetRecentShifts(20);

            Assert.Equal(2, shifts.Count);
            // StartAt 倒序：班次B（09:00 起，未结束）在前
            Assert.Equal(new DateTime(2026, 1, 10, 9, 0, 0), shifts[0].StartAt);
            Assert.Null(shifts[0].EndAt);
            Assert.Equal(4, shifts[0].Total);
            Assert.Equal(1, shifts[0].Ok);
            Assert.Equal("张三", shifts[0].Operator);
            Assert.Equal("测试任务A", shifts[0].TaskName);

            Assert.Equal(new DateTime(2026, 1, 10, 8, 0, 0), shifts[1].StartAt);
            Assert.Equal(4, shifts[1].Total);
            Assert.Equal(2, shifts[1].Ok);
            Assert.Equal(50.0, shifts[1].YieldPercent, 2);
        }

        [Fact]
        public void 班次报表_以实际记录为准()
        {
            var shiftA = _sp.GetRecentShifts(20).Single(s => s.StartAt == new DateTime(2026, 1, 10, 8, 0, 0));
            var report = _sp.GetShiftReport(shiftA.Id);

            Assert.Equal(4, report.Shift.Total);
            Assert.Equal(2, report.Shift.Ok);
            Assert.Equal(50.0, report.Shift.YieldPercent, 2);
            Assert.Equal(1234, report.StopDurationMs);
            Assert.Equal(new DateTime(2026, 1, 10, 8, 5, 0), report.FirstDetection);
            Assert.Equal(new DateTime(2026, 1, 10, 8, 35, 0), report.LastDetection);
            Assert.Single(report.NgTop);
            Assert.Equal(NgReason.BnMismatch, report.NgTop[0].Reason);
            Assert.Equal(2, report.NgTop[0].Count);
            Assert.Equal(100.0, report.NgTop[0].CumulativePercent, 1);
        }

        [Fact]
        public void 百分位_边界情况()
        {
            Assert.Equal(0, SpStatisticsService.Percentile(new List<double>(), 50));
            Assert.Equal(7.5, SpStatisticsService.Percentile(new List<double> { 7.5 }, 99));
            // [1,2,3,4] P50: idx=3*0.5=1.5 → 2*0.5+3*0.5=2.5
            Assert.Equal(2.5, SpStatisticsService.Percentile(new List<double> { 1, 2, 3, 4 }, 50));
            // P0/P100 取端点
            Assert.Equal(1, SpStatisticsService.Percentile(new List<double> { 1, 2, 3, 4 }, 0));
            Assert.Equal(4, SpStatisticsService.Percentile(new List<double> { 1, 2, 3, 4 }, 100));
        }

        [Theory]
        [InlineData(NgReason.BnMismatch, "批次号不符")]
        [InlineData(NgReason.MfgMismatch, "生产期不符")]
        [InlineData(NgReason.ExpMismatch, "有效期不符")]
        [InlineData(NgReason.BnMissing, "批次号缺失")]
        [InlineData(NgReason.OcrFailed, "OCR识别失败")]
        [InlineData(NgReason.Timeout, "检测超时")]
        [InlineData(NgReason.None, "其他")]
        public void Ng分类文案_映射(NgReason reason, string expected)
        {
            Assert.Equal(expected, SpStatisticsService.NgReasonText(reason));
        }
    }
}
