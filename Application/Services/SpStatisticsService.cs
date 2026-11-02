using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TripleDetection.Domain.Enums;
using TripleDetection.Infrastructure.Persistence;

namespace TripleDetection.Application.Services
{
    /// <summary>良率趋势点（小时粒度）</summary>
    public class YieldPoint
    {
        public DateTime Time { get; set; }
        public int Total { get; set; }
        public int Ok { get; set; }
        public int Ng => Total - Ok;
        public double YieldPercent { get; set; }
    }

    /// <summary>NG Pareto 项（按缺陷分类聚合，含累计占比）</summary>
    public class NgParetoItem
    {
        public NgReason Reason { get; set; }
        public string ReasonText { get; set; } = string.Empty;
        public int Count { get; set; }
        public double CumulativePercent { get; set; }
    }

    /// <summary>SPC 摘要：OCR 置信度（均值/P5）+ 检测耗时（P50/P99）</summary>
    public class SpcSummary
    {
        public int SampleCount { get; set; }
        public double? ConfidenceMean { get; set; }
        public double? ConfidenceP5 { get; set; }
        public long CycleP50Ms { get; set; }
        public long CycleP99Ms { get; set; }
    }

    /// <summary>班次概要（列表用）</summary>
    public class ShiftInfo
    {
        public int Id { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string Operator { get; set; } = string.Empty;
        public string TaskName { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Ok { get; set; }
        public int Ng => Total - Ok;
        public double YieldPercent { get; set; }
    }

    /// <summary>班次报表（M7，设计文档 §3.7）</summary>
    public class ShiftReport
    {
        public ShiftInfo Shift { get; set; } = new ShiftInfo();
        public long StopDurationMs { get; set; }
        public DateTime? FirstDetection { get; set; }
        public DateTime? LastDetection { get; set; }
        public SpcSummary Spc { get; set; } = new SpcSummary();
        public IReadOnlyList<NgParetoItem> NgTop { get; set; } = Array.Empty<NgParetoItem>();
    }

    /// <summary>
    /// SPC 与班次统计服务（M7，设计文档 §3.7）：
    /// 良率趋势（小时粒度）/ NG Pareto / OCR 置信度 / 检测耗时 / 班次报表。
    /// 数据源 DetectionRecords 聚合查询（SQLite 单机量级足够；DetectionTime 已有索引）。
    /// </summary>
    public class SpStatisticsService
    {
        private readonly TripleDetectionDbContext _db;

        public SpStatisticsService(TripleDetectionDbContext db)
        {
            _db = db;
        }

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(_db.Database.GetDbConnection().ConnectionString);
            conn.Open();
            return conn;
        }

        /// <summary>小时粒度良率趋势</summary>
        public IReadOnlyList<YieldPoint> GetYieldTrend(DateTime from, DateTime to)
        {
            var result = new List<YieldPoint>();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT strftime('%Y-%m-%d %H:00', DetectionTime) AS Bucket,
       COUNT(*) AS Total,
       SUM(CASE WHEN IsOK = 1 THEN 1 ELSE 0 END) AS Ok
FROM DetectionRecords
WHERE IsDeleted = 0 AND DetectionTime >= @from AND DetectionTime < @to
GROUP BY Bucket ORDER BY Bucket";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bucket = reader.GetString(0);
                var total = Convert.ToInt32(reader.GetValue(1));
                var ok = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                result.Add(new YieldPoint
                {
                    Time = DateTime.ParseExact(bucket, "yyyy-MM-dd HH:00", System.Globalization.CultureInfo.InvariantCulture),
                    Total = total,
                    Ok = ok,
                    YieldPercent = total == 0 ? 0 : Math.Round(ok * 100.0 / total, 2)
                });
            }
            return result;
        }

        /// <summary>NG Pareto（按缺陷分类降序 + 累计占比）</summary>
        public IReadOnlyList<NgParetoItem> GetNgPareto(DateTime from, DateTime to)
        {
            var rows = new List<(NgReason reason, int count)>();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT NgReason AS Reason, COUNT(*) AS Cnt
FROM DetectionRecords
WHERE IsDeleted = 0 AND IsOK = 0 AND NgReason <> 0
  AND DetectionTime >= @from AND DetectionTime < @to
GROUP BY NgReason ORDER BY Cnt DESC";
            cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((Enum.IsDefined(typeof(NgReason), reader.GetInt32(0))
                    ? (NgReason)reader.GetInt32(0) : NgReason.Other,
                    Convert.ToInt32(reader.GetValue(1))));
            }

            var total = rows.Sum(r => r.count);
            double cum = 0;
            return rows.Select(r =>
            {
                cum += total == 0 ? 0 : r.count * 100.0 / total;
                return new NgParetoItem
                {
                    Reason = r.reason,
                    ReasonText = NgReasonText(r.reason),
                    Count = r.count,
                    CumulativePercent = Math.Round(cum, 1)
                };
            }).ToList();
        }

        /// <summary>OCR 置信度（均值/P5）+ 检测耗时（P50/P99）</summary>
        public SpcSummary GetSpcSummary(DateTime from, DateTime to)
        {
            var summary = new SpcSummary();
            using var conn = OpenConnection();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT Confidence FROM DetectionRecords
WHERE IsDeleted = 0 AND Confidence IS NOT NULL
  AND DetectionTime >= @from AND DetectionTime < @to";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
                var confs = new List<double>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) confs.Add(reader.GetDouble(0));
                if (confs.Count > 0)
                {
                    summary.ConfidenceMean = Math.Round(confs.Average(), 4);
                    summary.ConfidenceP5 = Math.Round(Percentile(confs.OrderBy(x => x).ToList(), 5), 4);
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT ElapsedMs FROM DetectionRecords
WHERE IsDeleted = 0 AND DetectionTime >= @from AND DetectionTime < @to";
                cmd.Parameters.AddWithValue("@from", from.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@to", to.ToString("yyyy-MM-dd HH:mm:ss"));
                var cycles = new List<long>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) cycles.Add(reader.GetInt64(0));
                if (cycles.Count > 0)
                {
                    var sorted = cycles.OrderBy(x => x).ToList();
                    // Math.Round 避免浮点误差导致截断（如 P99=2929.999… → 2929）
                    summary.CycleP50Ms = (long)Math.Round(Percentile(sorted.Select(x => (double)x).ToList(), 50));
                    summary.CycleP99Ms = (long)Math.Round(Percentile(sorted.Select(x => (double)x).ToList(), 99));
                }
            }

            summary.SampleCount = (int)QueryScalarLong(
                "SELECT COUNT(*) FROM DetectionRecords WHERE IsDeleted = 0 AND DetectionTime >= @from AND DetectionTime < @to",
                new[] { ("@from", from.ToString("yyyy-MM-dd HH:mm:ss")), ("@to", to.ToString("yyyy-MM-dd HH:mm:ss")) });
            return summary;
        }

        /// <summary>最近班次列表（含窗口内检测统计）</summary>
        public IReadOnlyList<ShiftInfo> GetRecentShifts(int limit = 20)
        {
            var result = new List<ShiftInfo>();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT s.Id, s.StartAt, s.EndAt,
       COALESCE(u.RealName, u.Username, '') AS Operator,
       COALESCE(t.Name, '') AS TaskName,
       COUNT(r.Id) AS Total,
       SUM(CASE WHEN r.IsOK = 1 THEN 1 ELSE 0 END) AS Ok
FROM Shifts s
LEFT JOIN Users u ON u.Id = s.OperatorId
LEFT JOIN Tasks t ON t.Id = s.TaskId
LEFT JOIN DetectionRecords r
       ON r.IsDeleted = 0 AND r.DetectionTime >= s.StartAt
      AND r.DetectionTime < COALESCE(s.EndAt, @now)
WHERE s.IsDeleted = 0
GROUP BY s.Id
ORDER BY s.StartAt DESC
LIMIT @limit";
            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var total = Convert.ToInt32(reader.GetValue(5));
                var ok = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6));
                result.Add(new ShiftInfo
                {
                    Id = reader.GetInt32(0),
                    StartAt = ParseDbDate(reader.GetString(1)),
                    EndAt = reader.IsDBNull(2) ? null : ParseDbDate(reader.GetString(2)),
                    Operator = reader.GetString(3),
                    TaskName = reader.GetString(4),
                    Total = total,
                    Ok = ok,
                    YieldPercent = total == 0 ? 0 : Math.Round(ok * 100.0 / total, 2)
                });
            }
            return result;
        }

        /// <summary>班次报表（起止/操作员/总数/OK/NG/良率/NG 明细 Top/停机时长 + SPC 摘要）</summary>
        public ShiftReport GetShiftReport(int shiftId)
        {
            var report = new ShiftReport();
            using var conn = OpenConnection();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
SELECT s.Id, s.StartAt, s.EndAt, s.StopDurationMs,
       COALESCE(u.RealName, u.Username, '') AS Operator,
       COALESCE(t.Name, '') AS TaskName
FROM Shifts s
LEFT JOIN Users u ON u.Id = s.OperatorId
LEFT JOIN Tasks t ON t.Id = s.TaskId
WHERE s.Id = @id";
                cmd.Parameters.AddWithValue("@id", shiftId);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return report;
                var startAt = ParseDbDate(reader.GetString(1));
                var endAt = reader.IsDBNull(2) ? (DateTime?)null : ParseDbDate(reader.GetString(2));
                report.Shift = new ShiftInfo
                {
                    Id = reader.GetInt32(0),
                    StartAt = startAt,
                    EndAt = endAt,
                    Operator = reader.GetString(4),
                    TaskName = reader.GetString(5)
                };
                report.StopDurationMs = reader.GetInt64(3);

                // 窗口内检测统计（以实际记录为准，不依赖班次计数器）
                var windowEnd = endAt ?? DateTime.Now;
                var trend = GetYieldTrend(startAt, windowEnd);
                report.Shift.Total = trend.Sum(p => p.Total);
                report.Shift.Ok = trend.Sum(p => p.Ok);
                report.Shift.YieldPercent = report.Shift.Total == 0 ? 0
                    : Math.Round(report.Shift.Ok * 100.0 / report.Shift.Total, 2);
                report.Spc = GetSpcSummary(startAt, windowEnd);
                report.NgTop = GetNgPareto(startAt, windowEnd).Take(5).ToList();

                using var firstCmd = conn.CreateCommand();
                firstCmd.CommandText = @"
SELECT MIN(DetectionTime), MAX(DetectionTime) FROM DetectionRecords
WHERE IsDeleted = 0 AND DetectionTime >= @from AND DetectionTime < @to";
                firstCmd.Parameters.AddWithValue("@from", startAt.ToString("yyyy-MM-dd HH:mm:ss"));
                firstCmd.Parameters.AddWithValue("@to", windowEnd.ToString("yyyy-MM-dd HH:mm:ss"));
                using var fr = firstCmd.ExecuteReader();
                if (fr.Read())
                {
                    report.FirstDetection = fr.IsDBNull(0) ? null : ParseDbDate(fr.GetString(0));
                    report.LastDetection = fr.IsDBNull(1) ? null : ParseDbDate(fr.GetString(1));
                }
            }
            return report;
        }

        // ---- 辅助 ----

        private long QueryScalarLong(string sql, (string name, string value)[] parameters)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        /// <summary>线性插值百分位（输入需升序）</summary>
        public static double Percentile(IReadOnlyList<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            if (sorted.Count == 1) return sorted[0];
            var idx = (sorted.Count - 1) * p / 100.0;
            var lo = (int)Math.Floor(idx);
            var hi = (int)Math.Ceiling(idx);
            if (lo == hi) return sorted[lo];
            var frac = idx - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }

        private static DateTime ParseDbDate(string s) =>
            DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        public static string NgReasonText(NgReason reason) => reason switch
        {
            NgReason.BnMismatch => "批次号不符",
            NgReason.MfgMismatch => "生产期不符",
            NgReason.ExpMismatch => "有效期不符",
            NgReason.BnMissing => "批次号缺失",
            NgReason.MfgMissing => "生产期缺失",
            NgReason.ExpMissing => "有效期缺失",
            NgReason.OcrFailed => "OCR识别失败",
            NgReason.Timeout => "检测超时",
            NgReason.ImageQuality => "图像质量",
            _ => "其他"
        };
    }
}
