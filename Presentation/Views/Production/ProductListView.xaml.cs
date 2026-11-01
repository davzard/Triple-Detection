using TripleDetection.Presentation;
using System.Windows;
using System.Windows.Controls;
using TripleDetection.Presentation.ViewModels;
using TripleDetection.Presentation.ViewModels.Production;

namespace TripleDetection.Presentation.Views.Production
{
    public partial class ProductListView : UserControl
    {
        private ProductListViewModel ViewModel => (ProductListViewModel)DataContext;

        public ProductListView()
        {
            InitializeComponent();
            DataContext = ViewResolver.Resolve<ProductListViewModel>();
            Loaded += (s, e) => ViewModel.Search();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Search();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Reset();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenEditWindow();
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var product = (Domain.Entities.Product)button.DataContext;
            ViewModel.OpenEditWindow(product);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var product = (Domain.Entities.Product)button.DataContext;
            var result = MessageBox.Show($"确定要删除产品【{product.Name}】吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                ViewModel.DeleteProduct(product.Id);
            }
        }

        private void BtnFirstPage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.FirstPage();
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.PreviousPage();
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.NextPage();
        }

        private void BtnLastPage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.LastPage();
        }
    }
}