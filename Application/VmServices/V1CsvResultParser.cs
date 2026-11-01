using System;
using TripleDetection.Domain.Enums;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// v1 协议（现有格式，保留兼容）：逗号分隔 4 字段 "isOK,批号,生产日期,有效期"，
    /// 例 "1, 20260901, 2026-09-01, 2027-09-01"。
    /// v1 不携带缺陷分类：NG 统一记 Other，原始串存入 NgDetail 供追溯。
    /// </summary>
    public class V1CsvResultParser : IResultParser
    {
        public int Version => 1;

        public bool TryParse(string? raw, out DetectionResult? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var parts = raw.Trim().Split(',');
            if (parts.Length < 4)
            {
                return false;
            }

            bool isOK = parts[0].Trim() == "1";
            result = new DetectionResult
            {
                IsOK = isOK,
                BatchNumber = parts[1].Trim(),
                ProductionDate = parts[2].Trim(),
                ExpirationDate = parts[3].Trim(),
                DetectionTime = DateTime.Now,
                NgReason = isOK ? NgReason.None : NgReason.Other,
                NgDetail = isOK ? string.Empty : raw.Trim()
            };
            return true;
        }
    }
}
