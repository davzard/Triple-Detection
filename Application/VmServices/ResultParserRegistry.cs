using System.Collections.Generic;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// 结果协议解析器注册表：按原始串首字符自动路由（'{' → v2 JSON，其余 → v1 CSV）。
    /// VM 方案未升级前输出 v1 格式，升级后输出 v2 JSON，App 侧无需改动。
    /// </summary>
    public static class ResultParserRegistry
    {
        private static readonly IResultParser V1 = new V1CsvResultParser();
        private static readonly IResultParser V2 = new V2JsonResultParser();

        /// <summary>解析失败时返回 NG 兜底结果（NgReason=Other，NgDetail 记录原始串），保证检测链路不断。</summary>
        public static DetectionResult Parse(string? raw)
        {
            var parser = raw != null && raw.TrimStart()[0] == '{' ? V2 : V1;
            if (parser.TryParse(raw, out var result))
            {
                return result!;
            }

            return new DetectionResult
            {
                IsOK = false,
                NgReason = Domain.Enums.NgReason.Other,
                NgDetail = $"无法解析的结果串: {Truncate(raw)}",
                DetectionTime = System.DateTime.Now
            };
        }

        /// <summary>供单元测试/离线回放直接访问指定版本解析器。</summary>
        public static IEnumerable<IResultParser> AllParsers => new[] { V1, V2 };

        private static string Truncate(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "(空)";
            s = s.Trim();
            return s.Length <= 200 ? s : s.Substring(0, 200) + "...";
        }
    }
}
