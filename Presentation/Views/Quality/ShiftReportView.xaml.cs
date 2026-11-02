using System.Windows.Controls;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.Views.Quality
{
    /// <summary>
    /// 班次报表视图（M7）。
    /// </summary>
    public partial class ShiftReportView : UserControl
    {
        public ShiftReportView()
        {
            InitializeComponent();
            DataContext = new ViewModels.Quality.ShiftReportViewModel(
                ViewResolver.Resolve<SpStatisticsService>());
        }
    }
}
