using System;
using System.Collections.ObjectModel;
using System.Windows;
using Prism.Mvvm;
using TripleDetection.Domain.Entities;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.ViewModels.Production
{
    public class TaskListViewModel : BindableBase
    {
        private readonly ITaskService _taskService;
        private readonly IProductService _productService;
        private string _queryName = "";
        private int? _queryProductId;
        private int? _queryStatus;
        private DateTime? _queryProductionDateFrom;
        private DateTime? _queryProductionDateTo;
        private string _queryBatchNumber = "";
        private int _pageIndex = 0;
        private int _pageSize = 20;
        private int _totalCount = 0;
        private int _totalPages = 0;
        private ProdTask _selectedTask;

        public ObservableCollection<ProdTask> Tasks { get; } = new ObservableCollection<ProdTask>();
        public ObservableCollection<Product> Products { get; } = new ObservableCollection<Product>();

        public string QueryName
        {
            get => _queryName;
            set => SetProperty(ref _queryName, value);
        }

        public int? QueryProductId
        {
            get => _queryProductId;
            set => SetProperty(ref _queryProductId, value);
        }

        public int? QueryStatus
        {
            get => _queryStatus;
            set => SetProperty(ref _queryStatus, value);
        }

        public DateTime? QueryProductionDateFrom
        {
            get => _queryProductionDateFrom;
            set => SetProperty(ref _queryProductionDateFrom, value);
        }

        public DateTime? QueryProductionDateTo
        {
            get => _queryProductionDateTo;
            set => SetProperty(ref _queryProductionDateTo, value);
        }

        public string QueryBatchNumber
        {
            get => _queryBatchNumber;
            set => SetProperty(ref _queryBatchNumber, value);
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

        public ProdTask SelectedTask
        {
            get => _selectedTask;
            set => SetProperty(ref _selectedTask, value);
        }

        public TaskListViewModel(ITaskService taskService, IProductService productService)
        {
            _taskService = taskService;
            _productService = productService;
            LoadProducts();
        }

        private void LoadProducts()
        {
            Products.Clear();
            foreach (var product in _productService.GetAll())
            {
                Products.Add(product);
            }
        }

        public void Search()
        {
            var query = new TaskQuery
            {
                Name = QueryName,
                ProductId = QueryProductId,
                Status = QueryStatus,
                ProductionDateFrom = QueryProductionDateFrom,
                ProductionDateTo = QueryProductionDateTo,
                BatchNumber = QueryBatchNumber,
                PageIndex = PageIndex,
                PageSize = PageSize,
                SortBy = "CreateAt",
                SortDescending = true
            };

            var result = _taskService.Query(query);
            Tasks.Clear();
            foreach (var item in result.Items)
            {
                Tasks.Add(item);
            }
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            PageIndex = result.PageIndex;
        }

        public void Reset()
        {
            QueryName = "";
            QueryProductId = null;
            QueryStatus = null;
            QueryProductionDateFrom = null;
            QueryProductionDateTo = null;
            QueryBatchNumber = "";
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

        public void DeleteTask(int id)
        {
            _taskService.Delete(id, "admin", SessionManager.CurrentUserId);
            Search();
        }

        public void ApproveTask(int id)
        {
            _taskService.Approve(id, "admin", SessionManager.CurrentUserId);
            Search();
        }

        public void OpenEditWindow(ProdTask task = null)
        {
            var editVm = new TaskEditViewModel(task, _taskService, _productService);
            var editWindow = new Views.Production.TaskEditWindow { DataContext = editVm };
            editWindow.Owner = System.Windows.Application.Current.MainWindow;
            if (editWindow.ShowDialog() == true)
            {
                Search();
            }
        }
    }
}