using System.Windows.Controls;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.Views.Quality
{
    /// <summary>
    /// SPC 分析视图（M7）。
    /// </summary>
    public partial class SpcView : UserControl
    {
        private ViewModels.Quality.SpcViewModel _vm;

        public SpcView()
        {
            InitializeComponent();
            _vm = new ViewModels.Quality.SpcViewModel(
                ViewResolver.Resolve<SpStatisticsService>());
            DataContext = _vm;
        }

        private void CmbRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            switch (((ComboBoxItem)((ComboBox)sender).SelectedItem)?.Content as string)
            {
                case "最近 7 天": _vm.RangeDays = 7; break;
                case "最近 30 天": _vm.RangeDays = 30; break;
                default: _vm.RangeDays = 1; break;
            }
        }
    }
}
