using System;
using Newtonsoft.Json.Linq;
using TripleDetection.Domain.Enums;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.VmServices
{
    /// <summary>
    /// v2 协议（JSON，带版本与缺陷分类），格式见
    /// docs/specs/2026-11-01-production-readiness-design.md §3.3.1。
    /// 多缺陷并存时 NgReason 取优先级最高者（Mismatch &gt; Missing &gt; OcrFailed），全部明细存 NgDetail。
    /// </summary>
    public class V2JsonResultParser : IResultParser
    {
        public int Version => 2;

        public bool TryParse(string? raw, out DetectionResult? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart()[0] != '{')
            {
                return false;
            }

            JObject doc;
            try
            {
                doc = JObject.Parse(raw);
            }
            catch (Exception)
            {
                return false;
            }

            var okToken = doc["ok"];
            if (okToken == null || okToken.Type != JTokenType.Boolean)
            {
                return false;
            }
            bool isOK = okToken.Value<bool>();

            var fieldIssues = new System.Collections.Generic.List<NgReason>();
            var details = new System.Collections.Generic.List<string>();

            var bn = ReadField(doc["bn"], "bn", fieldIssues, details);
            var mfg = ReadField(doc["mfg"], "mfg", fieldIssues, details);
            var exp = ReadField(doc["exp"], "exp", fieldIssues, details);

            // 整图 OCR 失败（ok=false 且三个字段均无 actual）
            if (!isOK && bn == null && mfg == null && exp == null)
            {
                fieldIssues.Add(NgReason.OcrFailed);
            }

            string? msg = (string?)doc["msg"];
            if (!string.IsNullOrEmpty(msg))
            {
                details.Add(msg);
            }

            var confidenceToken = doc["confidence"];
            double? confidence = confidenceToken != null && confidenceToken.Type == JTokenType.Float
                ? confidenceToken.Value<double>()
                : (double?)null;

            result = new DetectionResult
            {
                IsOK = isOK,
                BatchNumber = bn?.actual ?? string.Empty,
                ProductionDate = mfg?.actual ?? string.Empty,
                ExpirationDate = exp?.actual ?? string.Empty,
                DetectionTime = DateTime.Now,
                NgReason = isOK ? NgReason.None : PickPrimaryReason(fieldIssues),
                NgDetail = string.Join("; ", details),
                Confidence = confidence
            };
            return true;
        }

        private sealed class FieldInfo
        {
            public string? actual;
        }

        /// <summary>读取单个三期字段；不匹配/漏印时追加对应缺陷码。字段缺失返回 null。</summary>
        private static FieldInfo? ReadField(
            JToken? token,
            string name,
            System.Collections.Generic.List<NgReason> issues,
            System.Collections.Generic.List<string> details)
        {
            if (token == null || token.Type != JTokenType.Object)
            {
                return null;
            }

            string? actual = (string?)token["actual"];
            var matchToken = token["match"];
            var match = matchToken != null && matchToken.Type == JTokenType.Boolean
                ? matchToken.Value<bool>()
                : false;

            string mismatchCode, missingCode;
            switch (name)
            {
                case "bn":
                    mismatchCode = "批号不匹配"; missingCode = "批号漏印";
                    break;
                case "mfg":
                    mismatchCode = "生产日期不匹配"; missingCode = "生产日期漏印";
                    break;
                default:
                    mismatchCode = "有效期不匹配"; missingCode = "有效期漏印";
                    break;
            }

            if (string.IsNullOrEmpty(actual))
            {
                issues.Add(name == "bn" ? NgReason.BnMissing
                        : name == "mfg" ? NgReason.MfgMissing
                        : NgReason.ExpMissing);
                details.Add(missingCode);
                return new FieldInfo { actual = string.Empty };
            }

            if (!match)
            {
                issues.Add(name == "bn" ? NgReason.BnMismatch
                        : name == "mfg" ? NgReason.MfgMismatch
                        : NgReason.ExpMismatch);
                details.Add(mismatchCode);
            }

            return new FieldInfo { actual = actual };
        }

        /// <summary>多缺陷取优先级最高者：Mismatch(1-3) &gt; Missing(10-12) &gt; OcrFailed(20)</summary>
        private static NgReason PickPrimaryReason(System.Collections.Generic.IEnumerable<NgReason> issues)
        {
            NgReason best = NgReason.Other;
            int bestPriority = int.MaxValue;
            foreach (var issue in issues)
            {
                int priority = issue switch
                {
                    NgReason.BnMismatch or NgReason.MfgMismatch or NgReason.ExpMismatch => 1,
                    NgReason.BnMissing or NgReason.MfgMissing or NgReason.ExpMissing => 2,
                    NgReason.OcrFailed => 3,
                    _ => 4
                };
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    best = issue;
                }
            }
            return best;
        }
    }
}
