using System;
using System.Threading;
using TripleDetection.Application.SettingsServices;
using TripleDetection.Infrastructure;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// 工位上下文：提供工位码（FactoryCode + ProductionLine）与检测顺序号。
    /// 顺序号在 Phase 0 为进程内计数（重启归零）；Phase 3 班次服务落地后改为班次内顺序号。
    /// 单例注册（见 App.xaml.cs），供各检测引擎共享。
    /// </summary>
    public class StationContext
    {
        private readonly SystemSettingsService _settingsService;
        private int _sequence;

        public StationContext(SystemSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <summary>工位码，如 "F001-L001"；配置缺失时返回 "UNKNOWN"。</summary>
        public string GetStationCode()
        {
            try
            {
                var s = _settingsService?.Load();
                if (s == null) return "UNKNOWN";
                var factory = string.IsNullOrEmpty(s.FactoryCode) ? "F000" : s.FactoryCode;
                var line = string.IsNullOrEmpty(s.ProductionLine) ? "L000" : s.ProductionLine;
                return $"{factory}-{line}";
            }
            catch (Exception)
            {
                // 配置读取失败不阻塞检测链路，降级为 UNKNOWN
                return "UNKNOWN";
            }
        }

        /// <summary>取下一个检测顺序号（线程安全）。</summary>
        public int NextSequence() => Interlocked.Increment(ref _sequence);
    }
}
