using System;
using System.Linq;
using TripleDetection.Application.VmServices;
using TripleDetection.Domain.Enums;
using TripleDetection.Presentation.Models;
using Xunit;

namespace TripleDetection.Tests
{
    /// <summary>v1 CSV 协议解析器测试</summary>
    public class V1CsvResultParserTests
    {
        private readonly V1CsvResultParser _parser = new V1CsvResultParser();

        [Fact]
        public void Parse_OK_结果()
        {
            bool ok = _parser.TryParse("1, 20260901, 2026-09-01, 2027-09-01", out var r);
            Assert.True(ok);
            Assert.True(r.IsOK);
            Assert.Equal("20260901", r.BatchNumber);
            Assert.Equal("2026-09-01", r.ProductionDate);
            Assert.Equal("2027-09-01", r.ExpirationDate);
            Assert.Equal(NgReason.None, r.NgReason);
            Assert.Equal(string.Empty, r.NgDetail);
            Assert.Null(r.Confidence);
        }

        [Fact]
        public void Parse_NG_结果_缺陷码为Other_原始串入NgDetail()
        {
            string raw = "0, 20260901, 2026-09-01, 2027-09-01";
            bool ok = _parser.TryParse(raw, out var r);
            Assert.True(ok);
            Assert.False(r.IsOK);
            Assert.Equal(NgReason.Other, r.NgReason);
            Assert.Equal(raw, r.NgDetail);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Parse_空串_返回false(string raw)
        {
            Assert.False(_parser.TryParse(raw, out var r));
            Assert.Null(r);
        }

        [Theory]
        [InlineData("1, 20260901, 2026-09-01")]
        [InlineData("garbage")]
        [InlineData("1,2,3")]
        public void Parse_字段不足4个_返回false(string raw)
        {
            Assert.False(_parser.TryParse(raw, out _));
        }

        [Fact]
        public void Parse_去除字段首尾空白()
        {
            bool ok = _parser.TryParse(" 1 ,  BN001 ,  2026-09-01 ,  2027-09-01 ", out var r);
            Assert.True(ok);
            Assert.True(r.IsOK);
            Assert.Equal("BN001", r.BatchNumber);
        }

        [Fact]
        public void Version_为1()
        {
            Assert.Equal(1, _parser.Version);
        }
    }

    /// <summary>v2 JSON 协议解析器测试</summary>
    public class V2JsonResultParserTests
    {
        private readonly V2JsonResultParser _parser = new V2JsonResultParser();

        [Fact]
        public void Parse_OK_全字段匹配()
        {
            string raw = @"{""v"":2,""ok"":true,""bn"":{""expected"":""20260901"",""actual"":""20260901"",""match"":true},""mfg"":{""expected"":""2026-09-01"",""actual"":""2026-09-01"",""match"":true},""exp"":{""expected"":""2027-09-01"",""actual"":""2027-09-01"",""match"":true},""confidence"":0.97}";
            bool ok = _parser.TryParse(raw, out var r);
            Assert.True(ok);
            Assert.True(r.IsOK);
            Assert.Equal(NgReason.None, r.NgReason);
            Assert.Equal("20260901", r.BatchNumber);
            Assert.Equal(0.97, r.Confidence.Value, 3);
        }

        [Fact]
        public void Parse_NG_批号不匹配()
        {
            string raw = @"{""v"":2,""ok"":false,""bn"":{""expected"":""20260901"",""actual"":""20260902"",""match"":false},""mfg"":{""expected"":""2026-09-01"",""actual"":""2026-09-01"",""match"":true},""exp"":{""expected"":""2027-09-01"",""actual"":""2027-09-01"",""match"":true},""confidence"":0.91}";
            bool ok = _parser.TryParse(raw, out var r);
            Assert.True(ok);
            Assert.False(r.IsOK);
            Assert.Equal(NgReason.BnMismatch, r.NgReason);
            Assert.Contains("批号不匹配", r.NgDetail);
            Assert.Equal("20260902", r.BatchNumber);
        }

        [Fact]
        public void Parse_NG_有效期漏印()
        {
            string raw = @"{""v"":2,""ok"":false,""bn"":{""expected"":""20260901"",""actual"":""20260901"",""match"":true},""mfg"":{""expected"":""2026-09-01"",""actual"":""2026-09-01"",""match"":true},""exp"":{""expected"":""2027-09-01"",""actual"":"""",""match"":false}}";
            bool ok = _parser.TryParse(raw, out var r);
            Assert.True(ok);
            Assert.False(r.IsOK);
            Assert.Equal(NgReason.ExpMissing, r.NgReason);
            Assert.Contains("有效期漏印", r.NgDetail);
        }

        [Fact]
        public void Parse_NG_三字段全缺_判定OcrFailed()
        {
            string raw = @"{""v"":2,""ok"":false,""msg"":""OCR识别失败""}";
            bool ok = _parser.TryParse(raw, out var r);
            Assert.True(ok);
            Assert.False(r.IsOK);
            Assert.Equal(NgReason.OcrFailed, r.NgReason);
            Assert.Contains("OCR识别失败", r.NgDetail);
        }

        [Fact]
        public void Parse_多缺陷_优先级Mismatch高于Missing()
        {
            string raw = @"{""v"":2,""ok"":false,""bn"":{""expected"":""A"",""actual"":""B"",""match"":false},""mfg"":{""expected"":""2026-09-01"",""actual"":"""",""match"":false},""exp"":{""expected"":""2027-09-01"",""actual"":""2027-09-01"",""match"":true}}";
            bool ok = _parser.TryParse(raw, out var r);
            Assert.True(ok);
            Assert.Equal(NgReason.BnMismatch, r.NgReason);
            Assert.Contains("批号不匹配", r.NgDetail);
            Assert.Contains("生产日期漏印", r.NgDetail);
        }

        [Fact]
        public void Parse_非法JSON_返回false()
        {
            Assert.False(_parser.TryParse("{broken json", out var r));
            Assert.Null(r);
        }

        [Fact]
        public void Parse_缺少ok字段_返回false()
        {
            Assert.False(_parser.TryParse(@"{""v"":2,""bn"":{}}", out _));
        }

        [Fact]
        public void Parse_非JSON串_返回false()
        {
            Assert.False(_parser.TryParse("1, 20260901, 2026-09-01, 2027-09-01", out _));
        }

        [Fact]
        public void Parse_空串_返回false()
        {
            Assert.False(_parser.TryParse("   ", out _));
        }

        [Fact]
        public void Version_为2()
        {
            Assert.Equal(2, _parser.Version);
        }
    }

    /// <summary>解析器注册表路由与兜底测试</summary>
    public class ResultParserRegistryTests
    {
        [Fact]
        public void Parse_CSV串_路由到v1()
        {
            var r = ResultParserRegistry.Parse("1, 20260901, 2026-09-01, 2027-09-01");
            Assert.True(r.IsOK);
            Assert.Equal("20260901", r.BatchNumber);
        }

        [Fact]
        public void Parse_JSON串_路由到v2()
        {
            var r = ResultParserRegistry.Parse(@"{""v"":2,""ok"":true,""bn"":{""expected"":""A"",""actual"":""A"",""match"":true}}");
            Assert.True(r.IsOK);
            Assert.Equal(NgReason.None, r.NgReason);
        }

        [Fact]
        public void Parse_JSON前带空白_仍路由到v2()
        {
            var r = ResultParserRegistry.Parse("  {\"v\":2,\"ok\":true}");
            Assert.True(r.IsOK);
        }

        [Fact]
        public void Parse_无法解析_返回NG兜底()
        {
            var r = ResultParserRegistry.Parse("1, 20260901");
            Assert.False(r.IsOK);
            Assert.Equal(NgReason.Other, r.NgReason);
            Assert.StartsWith("无法解析的结果串", r.NgDetail);
        }

        [Fact]
        public void Parse_空串_返回NG兜底()
        {
            var r = ResultParserRegistry.Parse(null);
            Assert.False(r.IsOK);
            Assert.Equal(NgReason.Other, r.NgReason);
            Assert.Contains("(空)", r.NgDetail);
        }

        [Fact]
        public void AllParsers_包含v1与v2()
        {
            var versions = ResultParserRegistry.AllParsers.Select(p => p.Version).ToArray();
            Assert.Contains(1, versions);
            Assert.Contains(2, versions);
        }
    }
}
