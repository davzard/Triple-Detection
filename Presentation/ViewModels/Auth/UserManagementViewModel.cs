using System;
using System.Collections.ObjectModel;
using System.Windows;
using Prism.Mvvm;
using TripleDetection.Domain.Entities;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.ViewModels.Auth
{
    public class UserManagementViewModel : BindableBase
    {
        private readonly IUserService _userService;
        private string _queryUsername = "";
        private string _queryRole = "";
        private string _queryStatusText = "";
        private int _pageIndex = 0;
        private int _pageSize = 20;
        private int _totalCount = 0;
        private int _totalPages = 0;
        private User _selectedUser;

        public ObservableCollection<User> Users { get; } = new ObservableCollection<User>();

        public string QueryUsername
        {
            get => _queryUsername;
            set => SetProperty(ref _queryUsername, value);
        }

        public string QueryRole
        {
            get => _queryRole;
            set => SetProperty(ref _queryRole, value);
        }

        public string QueryStatusText
        {
            get => _queryStatusText;
            set => SetProperty(ref _queryStatusText, value);
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

        public User SelectedUser
        {
            get => _selectedUser;
            set => SetProperty(ref _selectedUser, value);
        }

        public UserManagementViewModel(IUserService userService)
        {
            _userService = userService;
        }

        public void Search()
        {
            var query = new UserQuery
            {
                Username = QueryUsername,
                Role = QueryRole,
                StatusText = QueryStatusText,
                PageIndex = PageIndex,
                PageSize = PageSize,
                SortBy = "CreateAt",
                SortDescending = true
            };

            var result = _userService.Query(query);
            Users.Clear();
            foreach (var item in result.Items)
            {
                Users.Add(item);
            }
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            PageIndex = result.PageIndex;
        }

        public void Reset()
        {
            QueryUsername = "";
            QueryRole = "";
            QueryStatusText = "";
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

        public void DeleteUser(string username)
        {
            try
            {
                _userService.Delete(username, "admin", SessionManager.CurrentUserId);
                Search();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void EnableUser(string username)
        {
            try
            {
                _userService.Enable(username, "admin", SessionManager.CurrentUserId);
                Search();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启用失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void DisableUser(string username)
        {
            try
            {
                _userService.Disable(username, "admin", SessionManager.CurrentUserId);
                Search();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"禁用失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LockUser(string username)
        {
            try
            {
                _userService.Lock(username, "admin", SessionManager.CurrentUserId);
                Search();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"锁定失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void UnlockUser(string username)
        {
            try
            {
                _userService.Unlock(username, "admin", SessionManager.CurrentUserId);
                Search();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解锁失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenEditWindow(User user = null)
        {
            var editVm = new UserEditViewModel(user, _userService);
            var editWindow = new Views.Auth.UserEditWindow { DataContext = editVm };
            editWindow.Owner = System.Windows.Application.Current.MainWindow;
            if (editWindow.ShowDialog() == true)
            {
                Search();
            }
        }
    }
}