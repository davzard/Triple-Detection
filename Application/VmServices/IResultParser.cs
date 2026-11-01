using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// 检测结果协议解析器。按协议版本实现，由 <see cref="ResultParserRegistry"/> 路由。
    /// 解析失败（格式非法）时 TryParse 返回 false，由调用方决定兜底策略（默认按 NG 处理）。
    /// </summary>
    public interface IResultParser
    {
        /// <summary>协议版本号（v1 = CSV，v2 = JSON）</summary>
        int Version { get; }

        /// <summary>尝试解析原始结果串。成功时输出完整结果（含 NgReason/NgDetail/Confidence）。</summary>
        bool TryParse(string? raw, out DetectionResult? result);
    }
}
