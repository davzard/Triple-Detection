using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.ViewModels.Quality
{
    /// <summary>
    /// 班次报表视图模型（M7，设计文档 §3.7）：
    /// 班次选择 → 起止/操作员/总数/OK/NG/良率/NG 明细 Top/停机时长，支持导出 CSV。
    /// </summary>
    public class ShiftReportViewModel : BindableBase
    {
        private readonly SpStatisticsService _sp;
        private ShiftInfo _selectedShift;
        private ShiftReport _report;

        public ShiftReportViewModel(SpStatisticsService sp)
        {
            _sp = sp;
            Shifts = new ObservableCollection<ShiftInfo>(_sp.GetRecentShifts(20));
            if (Shifts.Count > 0) SelectedShift = Shifts[0];
            ExportCommand = new DelegateCommand(ExportCsv, () => Report != null);
        }

        public ObservableCollection<ShiftInfo> Shifts { get; }

        public DelegateCommand ExportCommand { get; }

        public ShiftInfo SelectedShift
        {
            get => _selectedShift;
            set
            {
                if (SetProperty(ref _selectedShift, value) && value != null)
                {
                    Report = _sp.GetShiftReport(value.Id);
                    RaisePropertyChanged(nameof(ShiftDisplay));
                    ExportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ShiftReport Report
        {
            get => _report;
            private set => SetProperty(ref _report, value);
        }

        public string ShiftDisplay => _selectedShift == null ? "无班次"
            : $"班次#{_selectedShift.Id}  {_selectedShift.StartAt:yyyy-MM-dd HH:mm}" +
              ($" ~ {(_selectedShift.EndAt?.ToString("HH:mm") ?? "进行中")}" +
               $"  操作员: {_selectedShift.Operator}  任务: {_selectedShift.TaskName}");

        public void ExportCsv()
        {
            var r = Report;
            if (r == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "CSV文件|*.csv",
                FileName = $"班次报表_{r.Shift.StartAt:yyyyMMdd_HHmm}.csv"
            };
            if (dialog.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("班次报表");
            sb.AppendLine($"班次ID,{r.Shift.Id}");
            sb.AppendLine($"开始时间,{r.Shift.StartAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"结束时间,{(r.Shift.EndAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "进行中")}");
            sb.AppendLine($"操作员,{r.Shift.Operator}");
            sb.AppendLine($"任务,{r.Shift.TaskName}");
            sb.AppendLine($"总数,{r.Shift.Total}");
            sb.AppendLine($"OK,{r.Shift.Ok}");
            sb.AppendLine($"NG,{r.Shift.Ng}");
            sb.AppendLine($"良率%,{r.Shift.YieldPercent:F2}");
            sb.AppendLine($"停机时长ms,{r.StopDurationMs}");
            sb.AppendLine($"首检时间,{(r.FirstDetection?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")}");
            sb.AppendLine($"末检时间,{(r.LastDetection?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")}");
            if (r.Spc.ConfidenceMean.HasValue)
                sb.AppendLine($"OCR置信度均值,{r.Spc.ConfidenceMean:F4}");
            if (r.Spc.ConfidenceP5.HasValue)
                sb.AppendLine($"OCR置信度P5,{r.Spc.ConfidenceP5:F4}");
            sb.AppendLine($"检测耗时P50ms,{r.Spc.CycleP50Ms}");
            sb.AppendLine($"检测耗时P99ms,{r.Spc.CycleP99Ms}");
            sb.AppendLine();
            sb.AppendLine("NG明细Top,数量,累计占比%");
            foreach (var item in r.NgTop)
                sb.AppendLine($"{item.ReasonText},{item.Count},{item.CumulativePercent:F1}");

            try
            {
                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出失败: {ex.Message}", "错误",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
