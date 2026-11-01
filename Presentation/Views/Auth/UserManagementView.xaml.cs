using TripleDetection.Presentation;
using System.Windows;
using System.Windows.Controls;
using TripleDetection.Presentation.ViewModels;
using TripleDetection.Presentation.ViewModels.Auth;

namespace TripleDetection.Presentation.Views.Auth
{
    public partial class UserManagementView : UserControl
    {
        private UserManagementViewModel ViewModel => (UserManagementViewModel)DataContext;

        public UserManagementView()
        {
            InitializeComponent();
            DataContext = ViewResolver.Resolve<UserManagementViewModel>();
        }

        private void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.QueryUsername = txtSearchUsername.Text;
            ViewModel.QueryRole = cboSearchRole.SelectedIndex > 0 ? cboSearchRole.SelectedItem.ToString() : "";
            ViewModel.QueryStatusText = cboSearchStatus.SelectedIndex > 0 ? cboSearchStatus.SelectedItem.ToString() : "";
            ViewModel.Search();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            txtSearchUsername.Text = "";
            cboSearchRole.SelectedIndex = 0;
            cboSearchStatus.SelectedIndex = 0;
            ViewModel.Reset();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.OpenEditWindow(null);
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var user = btn.DataContext as Domain.Entities.User;
            if (user == null) return;

            ViewModel.OpenEditWindow(user);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var user = btn.DataContext as Domain.Entities.User;
            if (user == null) return;

            var result = MessageBox.Show(
                $"确定要删除用户 '{user.Username}' 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ViewModel.DeleteUser(user.Username);
            }
        }

        private void BtnEnable_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var user = btn.DataContext as Domain.Entities.User;
            if (user == null) return;

            ViewModel.EnableUser(user.Username);
        }

        private void BtnDisable_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var user = btn.DataContext as Domain.Entities.User;
            if (user == null) return;

            ViewModel.DisableUser(user.Username);
        }

        private void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var user = btn.DataContext as Domain.Entities.User;
            if (user == null) return;

            ViewModel.LockUser(user.Username);
        }

        private void BtnUnlock_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var user = btn.DataContext as Domain.Entities.User;
            if (user == null) return;

            ViewModel.UnlockUser(user.Username);
        }

        private void BtnFirstPage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.FirstPage();
        }

        private void BtnPreviousPage_Click(object sender, RoutedEventArgs e)
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