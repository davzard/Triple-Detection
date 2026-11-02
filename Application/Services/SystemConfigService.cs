using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TripleDetection.Domain.Entities;
using TripleDetection.Infrastructure.Persistence;

namespace TripleDetection.Application.Services
{
    /// <summary>
    /// 系统配置服务：读写 SystemConfigs 表（Category + Key 分组，见设计文档 §5）。
    /// 启动时 <see cref="EnsureDefaults"/> 幂等补种默认值；读取失败返回代码内回退值，不抛异常。
    /// 注意：依赖单例 DbContext（非线程安全），所有访问经 _gate 串行化。
    /// </summary>
    public class SystemConfigService
    {
        private readonly TripleDetectionDbContext _db;
        private readonly LoggingService _logService;
        private readonly object _gate = new object();

        // ---- 配置键常量（设计文档 §5）----
        public const string CatPlc = "Plc";
        public const string KeyPlcMode = "Mode";
        public const string KeyPlcIp = "Ip";
        public const string KeyPlcPort = "Port";
        public const string KeyPlcUnitId = "UnitId";
        public const string KeyPlcRegisterMap = "RegisterMap";
        public const string KeyPlcOnDisconnect = "OnDisconnect";
        public const string KeyPlcPollIntervalMs = "PollIntervalMs";

        public const string CatDetection = "Detection";
        public const string KeyCaptureTimeoutMs = "CaptureTimeoutMs";
        public const string KeyOnCaptureTimeout = "OnCaptureTimeout";
        public const string KeyMinTriggerIntervalMs = "MinTriggerIntervalMs";

        public const string CatReject = "Reject";
        public const string KeyRejectDelayMs = "DelayMs";
        public const string KeyRejectDurationMs = "DurationMs";
        public const string KeyConsecutiveNgToStopLine = "ConsecutiveNgToStopLine";

        /// <summary>默认值表：(Category, Key, Value, Description)。EnsureDefaults 幂等补种。</summary>
        public static readonly (string Cat, string Key, string Value, string Desc)[] Defaults =
        {
            (CatPlc, KeyPlcMode, "mock", "PLC 通信模式：mock / modbus"),
            (CatPlc, KeyPlcIp, "192.168.1.200", "PLC IP 地址"),
            (CatPlc, KeyPlcPort, "502", "Modbus TCP 端口"),
            (CatPlc, KeyPlcUnitId, "1", "Modbus UnitId"),
            (CatPlc, KeyPlcRegisterMap, "plc_register_map.json", "寄存器表 JSON 文件名（Config 目录下，可选）"),
            (CatPlc, KeyPlcOnDisconnect, "StopLine", "PLC 断线策略：StopLine / ManualMode"),
            (CatPlc, KeyPlcPollIntervalMs, "500", "寄存器轮询间隔 ms"),
            (CatDetection, KeyCaptureTimeoutMs, "5000", "单次检测超时 ms（看门狗）"),
            (CatDetection, KeyOnCaptureTimeout, "TreatAsNG", "超时策略：TreatAsNG / Ignore / StopLine"),
            (CatDetection, KeyMinTriggerIntervalMs, "500", "触发防抖最小间隔 ms"),
            (CatReject, KeyRejectDelayMs, "50", "剔废命令延迟 ms（含线速补偿）"),
            (CatReject, KeyRejectDurationMs, "200", "剔废命令持续 ms"),
            (CatReject, KeyConsecutiveNgToStopLine, "10", "连续 NG 停线阈值"),
        };

        public SystemConfigService(TripleDetectionDbContext db, LoggingService logService)
        {
            _db = db;
            _logService = logService;
        }

        /// <summary>幂等补种默认配置（仅插入不存在的 Key）。启动时调用一次。</summary>
        public void EnsureDefaults()
        {
            lock (_gate)
            {
                try
                {
                    var existing = _db.SystemConfigs
                        .Where(c => !c.IsDeleted)
                        .Select(c => c.Category + "/" + c.Key)
                        .ToHashSet(StringComparer.Ordinal);

                    var now = DateTime.Now;
                    var inserted = 0;
                    foreach (var (cat, key, value, desc) in Defaults)
                    {
                        if (existing.Contains(cat + "/" + key)) continue;
                        _db.SystemConfigs.Add(new SystemConfig
                        {
                            Category = cat,
                            Key = key,
                            Value = value,
                            Description = desc,
                            CreateAt = now,
                            UpdateAt = now
                        });
                        inserted++;
                    }
                    if (inserted > 0)
                    {
                        _db.SaveChanges();
                        _logService?.LogInfo("配置", $"补种 {inserted} 项默认配置");
                    }
                }
                catch (Exception ex)
                {
                    _logService?.LogError("配置", $"EnsureDefaults 失败：{ex.Message}");
                }
            }
        }

        public string GetString(string category, string key, string fallback)
        {
            lock (_gate)
            {
                try
                {
                    var row = _db.SystemConfigs
                        .FirstOrDefault(c => !c.IsDeleted && c.Category == category && c.Key == key);
                    return row != null && !string.IsNullOrEmpty(row.Value) ? row.Value : fallback;
                }
                catch (Exception ex)
                {
                    _logService?.LogWarn("配置", $"读取 {category}/{key} 失败，使用默认值：{ex.Message}");
                    return fallback;
                }
            }
        }

        public int GetInt(string category, string key, int fallback)
        {
            var v = GetString(category, key, null);
            return int.TryParse(v, out var n) ? n : fallback;
        }

        public bool GetBool(string category, string key, bool fallback)
        {
            var v = GetString(category, key, null);
            return bool.TryParse(v, out var b) ? b : fallback;
        }

        public void Set(string category, string key, string value, string description = "")
        {
            lock (_gate)
            {
                try
                {
                    var row = _db.SystemConfigs
                        .FirstOrDefault(c => !c.IsDeleted && c.Category == category && c.Key == key);
                    if (row == null)
                    {
                        _db.SystemConfigs.Add(new SystemConfig
                        {
                            Category = category,
                            Key = key,
                            Value = value,
                            Description = description,
                            CreateAt = DateTime.Now,
                            UpdateAt = DateTime.Now
                        });
                    }
                    else
                    {
                        row.Value = value;
                        row.UpdateAt = DateTime.Now;
                    }
                    _db.SaveChanges();
                }
                catch (Exception ex)
                {
                    _logService?.LogError("配置", $"写入 {category}/{key} 失败：{ex.Message}");
                }
            }
        }
    }
}
