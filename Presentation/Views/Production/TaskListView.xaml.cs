using TripleDetection.Presentation;
using System.Windows;
using System.Windows.Controls;
using TripleDetection.Presentation.ViewModels;
using TripleDetection.Presentation.ViewModels.Production;
using TaskEntity = TripleDetection.Domain.Entities.ProdTask;

namespace TripleDetection.Presentation.Views.Production
{
    public partial class TaskListView : UserControl
    {
        private TaskListViewModel _viewModel;

        public TaskListView()
        {
            InitializeComponent();
            _viewModel = ViewResolver.Resolve<TaskListViewModel>();
            DataContext = _viewModel;
            Loaded += (s, e) => _viewModel.Search();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Search();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Reset();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenEditWindow();
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is TaskEntity task)
            {
                _viewModel.OpenEditWindow(task);
            }
        }

        private void BtnApprove_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is TaskEntity task)
            {
                var result = MessageBox.Show($"确认审核任务 [{task.Name}] 吗？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _viewModel.ApproveTask(task.Id);
                }
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is TaskEntity task)
            {
                var result = MessageBox.Show($"确认删除任务 [{task.Name}] 吗？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    _viewModel.DeleteTask(task.Id);
                }
            }
        }

        private void BtnFirstPage_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.FirstPage();
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.PreviousPage();
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NextPage();
        }

        private void BtnLastPage_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.LastPage();
        }
    }
}