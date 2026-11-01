using System;
using System.Collections.ObjectModel;
using System.Windows;
using Prism.Mvvm;
using TripleDetection.Domain.Entities;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.ViewModels.Production
{
    public class ProductListViewModel : BindableBase
    {
        private readonly IProductService _productService;
        private string _queryCode = "";
        private string _queryName = "";
        private int? _queryStatus;
        private DateTime? _queryCreateAtFrom;
        private DateTime? _queryCreateAtTo;
        private int _pageIndex = 0;
        private int _pageSize = 20;
        private int _totalCount = 0;
        private int _totalPages = 0;
        private Product _selectedProduct;

        public ObservableCollection<Product> Products { get; } = new ObservableCollection<Product>();

        public string QueryCode
        {
            get => _queryCode;
            set => SetProperty(ref _queryCode, value);
        }

        public string QueryName
        {
            get => _queryName;
            set => SetProperty(ref _queryName, value);
        }

        public int? QueryStatus
        {
            get => _queryStatus;
            set => SetProperty(ref _queryStatus, value);
        }

        public DateTime? QueryCreateAtFrom
        {
            get => _queryCreateAtFrom;
            set => SetProperty(ref _queryCreateAtFrom, value);
        }

        public DateTime? QueryCreateAtTo
        {
            get => _queryCreateAtTo;
            set => SetProperty(ref _queryCreateAtTo, value);
        }

        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (SetProperty(ref _pageIndex, value))
                    RaisePropertyChanged(nameof(CurrentPageDisplay));
            }
        }

        public int PageSize
        {
            get => _pageSize;
            set => SetProperty(ref _pageSize, value);
        }

        public int TotalCount
        {
            get => _totalCount;
            set
            {
                if (SetProperty(ref _totalCount, value))
                    RaisePropertyChanged(nameof(TotalPagesDisplay));
            }
        }

        public int TotalPages
        {
            get => _totalPages;
            set
            {
                if (SetProperty(ref _totalPages, value))
                {
                    RaisePropertyChanged(nameof(TotalPagesDisplay));
                    RaisePropertyChanged(nameof(HasNextPage));
                    RaisePropertyChanged(nameof(HasPreviousPage));
                }
            }
        }

        public string TotalPagesDisplay => $"共 {TotalCount} 条";
        public string CurrentPageDisplay => $"{PageIndex + 1} / {TotalPages} 页";
        public bool HasNextPage => PageIndex < TotalPages - 1;
        public bool HasPreviousPage => PageIndex > 0;

        public Product SelectedProduct
        {
            get => _selectedProduct;
            set => SetProperty(ref _selectedProduct, value);
        }

        public ProductListViewModel(IProductService productService)
        {
            _productService = productService;
        }

        public void Search()
        {
            var query = new ProductQuery
            {
                Code = QueryCode,
                Name = QueryName,
                Status = QueryStatus,
                CreateAtFrom = QueryCreateAtFrom,
                CreateAtTo = QueryCreateAtTo,
                PageIndex = PageIndex,
                PageSize = PageSize,
                SortBy = "CreateAt",
                SortDescending = true
            };

            var result = _productService.Query(query);
            Products.Clear();
            foreach (var item in result.Items)
            {
                Products.Add(item);
            }
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            PageIndex = result.PageIndex;
        }

        public void Reset()
        {
            QueryCode = "";
            QueryName = "";
            QueryStatus = null;
            QueryCreateAtFrom = null;
            QueryCreateAtTo = null;
            PageIndex = 0;
            Search();
        }

        public void FirstPage()
        {
            PageIndex = 0;
            Search();
        }

        public void PreviousPage()
        {
            if (PageIndex > 0)
            {
                PageIndex--;
                Search();
            }
        }

        public void NextPage()
        {
            if (PageIndex < TotalPages - 1)
            {
                PageIndex++;
                Search();
            }
        }

        public void LastPage()
        {
            PageIndex = TotalPages > 0 ? TotalPages - 1 : 0;
            Search();
        }

        public void DeleteProduct(int id)
        {
            _productService.Delete(id, "admin", SessionManager.CurrentUserId);
            Search();
        }

        public void OpenEditWindow(Product product = null)
        {
            var editVm = new ProductEditViewModel(product, _productService);
            var editWindow = new Views.Production.ProductEditWindow { DataContext = editVm };
            editWindow.Owner = System.Windows.Application.Current.MainWindow;
            if (editWindow.ShowDialog() == true)
            {
                Search();
            }
        }
    }
}