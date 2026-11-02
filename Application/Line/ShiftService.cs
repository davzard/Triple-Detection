using System;
using Microsoft.EntityFrameworkCore;
using TripleDetection.Application.Services;
using TripleDetection.Application.VmServices;
using TripleDetection.Domain.Entities;
using TripleDetection.Infrastructure.Persistence;

namespace TripleDetection.Application.Line
{
    /// <summary>
    /// 班次服务（Phase 2 班次实体，设计文档 §4.1 Shifts 表）：
    /// 开班创建班次记录、下班关闭、检测过程中增量统计。
    /// 完整的自动加载/换型/报表能力在 Phase 3（ShiftService M7）扩展。
    /// </summary>
    public class ShiftService : IDisposable
    {
        private readonly TripleDetectionDbContext _db;
        private readonly LoggingService _logService;
        private readonly StationContext _stationContext;
        private readonly object _gate = new object();
        private Shift? _current;

        public ShiftService(TripleDetectionDbContext db, LoggingService logService, StationContext stationContext)
        {
            _db = db;
            _logService = logService;
            _stationContext = stationContext;
        }

        /// <summary>当前班次 Id（未开班为 null）</summary>
        public int? CurrentShiftId
        {
            get { lock (_gate) return _current?.Id; }
        }

        /// <summary>开班：创建班次记录。失败仅记日志，不阻断开班。</summary>
        public void OpenShift(int taskId, int operatorId)
        {
            try
            {
                var shift = new Shift
                {
                    StartAt = DateTime.Now,
                    OperatorId = operatorId,
                    TaskId = taskId,
                    LineCode = _stationContext?.GetStationCode() ?? "UNKNOWN",
                    CreateBy = "system",
                    UpdateBy = "system"
                };
                _db.Shifts.Add(shift);
                _db.SaveChanges();
                lock (_gate) _current = shift;
                _logService?.LogInfo("产线", $"班次已创建：ShiftId={shift.Id} 任务={taskId} 操作员={operatorId}");
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"班次创建失败：{ex.Message}");
            }
        }

        /// <summary>增量统计一次检测结果（线程安全）。</summary>
        public void RecordResult(bool isOK)
        {
            Shift shift;
            lock (_gate) shift = _current;
            if (shift == null) return;
            try
            {
                lock (_gate)
                {
                    shift.TotalCount++;
                    if (isOK) shift.OkCount++; else shift.NgCount++;
                    shift.UpdateAt = DateTime.Now;
                    _db.Entry(shift).State = EntityState.Modified;
                    _db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                _logService?.LogWarn("产线", $"班次统计更新失败：{ex.Message}");
            }
        }

        /// <summary>下班：关闭当前班次（写 EndAt）。无当前班次时为空操作。</summary>
        public void CloseShift()
        {
            Shift shift;
            lock (_gate) shift = _current;
            if (shift == null) return;
            try
            {
                lock (_gate)
                {
                    shift.EndAt = DateTime.Now;
                    shift.UpdateAt = DateTime.Now;
                    _db.Entry(shift).State = EntityState.Modified;
                    _db.SaveChanges();
                    _current = null;
                }
                _logService?.LogInfo("产线", $"班次已关闭：ShiftId={shift.Id} 总数={shift.TotalCount} OK={shift.OkCount} NG={shift.NgCount}");
            }
            catch (Exception ex)
            {
                _logService?.LogError("产线", $"班次关闭失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            // 应用退出兜底：未关闭的班次补 EndAt
            try { CloseShift(); } catch { }
        }
    }
}
