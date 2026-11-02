using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Prism.Commands;
using Prism.Mvvm;
using SkiaSharp;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.ViewModels.Quality
{
    /// <summary>
    /// SPC 视图模型（M7，设计文档 §3.7）：
    /// 良率趋势折线（小时粒度 + 3σ 控制限）+ NG Pareto 柱状 + OCR 置信度/检测耗时摘要。
    /// </summary>
    public class SpcViewModel : BindableBase
    {
        private readonly SpStatisticsService _sp;
        private int _rangeDays = 1;
        private string _summaryText = "暂无数据";
        private string _controlText = "";

        public SpcViewModel(SpStatisticsService sp)
        {
            _sp = sp;
            RefreshCommand = new DelegateCommand(Refresh);
            Refresh();
        }

        public DelegateCommand RefreshCommand { get; }

        public int RangeDays
        {
            get => _rangeDays;
            set
            {
                if (SetProperty(ref _rangeDays, value)) Refresh();
            }
        }

        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        public string ControlText
        {
            get => _controlText;
            set => SetProperty(ref _controlText, value);
        }

        // ---- 良率趋势图 ----
        public ObservableCollection<ISeries> YieldSeries { get; } = new ObservableCollection<ISeries>();
        public Axis[] YieldXAxes { get; private set; } = Array.Empty<Axis>();
        public Axis[] YieldYAxes { get; private set; } =
        {
            new Axis { MinLimit = 0, MaxLimit = 100, Name = "良率 %" }
        };

        // ---- NG Pareto 图 ----
        public ObservableCollection<ISeries> ParetoSeries { get; } = new ObservableCollection<ISeries>();
        public Axis[] ParetoXAxes { get; private set; } = Array.Empty<Axis>();
        public Axis[] ParetoYAxes { get; private set; } =
        {
            new Axis { MinLimit = 0, Name = "数量" }
        };

        public void Refresh()
        {
            var to = DateTime.Now;
            var from = to.Date.AddDays(-(_rangeDays - 1));

            LoadYieldTrend(from, to);
            LoadNgPareto(from, to);
            LoadSummary(from, to);
        }

        private void LoadYieldTrend(DateTime from, DateTime to)
        {
            var points = _sp.GetYieldTrend(from, to).ToList();
            var labels = points.Select(p => p.Time.ToString("MM-dd HH:00")).ToArray();
            var yields = points.Select(p => p.YieldPercent).ToArray();

            // 3σ 控制限（基于小时良率点，样本标准差）
            string control = "";
            double[] uclValues = null, lclValues = null;
            if (yields.Length >= 2)
            {
                var mean = yields.Average();
                var sigma = Math.Sqrt(yields.Sum(y => (y - mean) * (y - mean)) / (yields.Length - 1));
                var ucl = Math.Min(100, mean + 3 * sigma);
                var lcl = Math.Max(0, mean - 3 * sigma);
                uclValues = Enumerable.Repeat(ucl, yields.Length).ToArray();
                lclValues = Enumerable.Repeat(lcl, yields.Length).ToArray();
                control = $"控制限: UCL={ucl:F1}%  中心线={mean:F1}%  LCL={lcl:F1}%  (n={yields.Length}h)";
            }

            YieldSeries.Clear();
            if (yields.Length > 0)
            {
                YieldSeries.Add(new LineSeries<double>
                {
                    Values = yields,
                    Name = "良率 %",
                    GeometrySize = 6,
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2)
                });
                if (uclValues != null)
                {
                    var dash = new DashEffect(new[] { 4f, 3f }, 0f);
                    YieldSeries.Add(new LineSeries<double>
                    {
                        Values = uclValues,
                        Name = "UCL",
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(SKColors.OrangeRed, 1) { PathEffect = dash }
                    });
                    YieldSeries.Add(new LineSeries<double>
                    {
                        Values = lclValues,
                        Name = "LCL",
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(SKColors.OrangeRed, 1) { PathEffect = dash }
                    });
                }
            }

            YieldXAxes = new[]
            {
                new Axis { Labels = labels, Labeler = v => labels[(int)v] ?? "" }
            };
            RaisePropertyChanged(nameof(YieldXAxes));
            ControlText = control;
        }

        private void LoadNgPareto(DateTime from, DateTime to)
        {
            var pareto = _sp.GetNgPareto(from, to).ToList();
            ParetoSeries.Clear();
            if (pareto.Count > 0)
            {
                ParetoSeries.Add(new ColumnSeries<double>
                {
                    Values = pareto.Select(p => (double)p.Count).ToArray(),
                    Name = "NG 数量",
                    Padding = 4,
                    Fill = new SolidColorPaint(SKColors.IndianRed)
                });
                ParetoXAxes = new[]
                {
                    new Axis { Labels = pareto.Select(p => p.ReasonText).ToArray() }
                };
            }
            else
            {
                ParetoXAxes = Array.Empty<Axis>();
            }
            RaisePropertyChanged(nameof(ParetoXAxes));
        }

        private void LoadSummary(DateTime from, DateTime to)
        {
            var s = _sp.GetSpcSummary(from, to);
            var parts = new List<string> { $"样本数: {s.SampleCount}" };
            if (s.ConfidenceMean.HasValue)
                parts.Add($"OCR置信度 均值={s.ConfidenceMean:F2} P5={s.ConfidenceP5:F2}");
            else
                parts.Add("OCR置信度: 无数据（v1 协议）");
            parts.Add($"检测耗时 P50={s.CycleP50Ms}ms P99={s.CycleP99Ms}ms");
            SummaryText = string.Join("    ", parts);
        }
    }
}
