using TripleDetection.Presentation;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TripleDetection.Presentation.ViewModels;

namespace TripleDetection.Presentation.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private bool _isPasswordVisible;

        public LoginWindow()
        {
            InitializeComponent();

            _viewModel = ViewResolver.Resolve<LoginViewModel>();
            DataContext = _viewModel;

            _viewModel.LoginSucceeded += OnLoginSucceeded;
            _viewModel.OnLoginFailed += OnLoginFailed;

            LoadLogo();
            LoadSystemName();

            UsernameTextBox.Focus();
        }

        private void LoadLogo()
        {
            var logoPath = _viewModel.LogoPath;
            if (!string.IsNullOrEmpty(logoPath))
            {
                var fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logoPath);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        LogoImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                            new Uri(fullPath, UriKind.Absolute));
                        return;
                    }
                    catch
                    {
                        // Fall through to placeholder
                    }
                }
            }
            ShowLogoPlaceholder();
        }

        private void ShowLogoPlaceholder()
        {
            LogoImage.Visibility = Visibility.Collapsed;
        }

        private void LoadSystemName()
        {
            var systemName = System.Configuration.ConfigurationManager.AppSettings["SystemName"];
            if (!string.IsNullOrEmpty(systemName))
                SystemNameText.Text = systemName;
        }

        private void OnLoginSucceeded(Domain.Entities.User user)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void OnLoginFailed()
        {
            var storyboard = (Storyboard)Resources["ShakeAnimation"];
            storyboard?.Begin();

            UsernameBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E53E3E"));
            PasswordBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E53E3E"));
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = PasswordBox.Password;
        }

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                PasswordVisibleTextBox.Text = PasswordBox.Password;
                PasswordVisibleTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;
                PasswordVisibleTextBox.Focus();
            }
            else
            {
                PasswordBox.Password = PasswordVisibleTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordVisibleTextBox.Visibility = Visibility.Collapsed;
                PasswordBox.Focus();
            }
        }

        private void UsernameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SubmitLogin();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SubmitLogin();
        }

        private void PasswordVisibleTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                SubmitLogin();
        }

        private void SubmitLogin()
        {
            _viewModel.Username = UsernameTextBox.Text;
            _viewModel.Password = _isPasswordVisible ? PasswordVisibleTextBox.Text : PasswordBox.Password;

            if (_viewModel.LoginCommand.CanExecute(null))
                _viewModel.LoginCommand.Execute(null);
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            SubmitLogin();
        }
    }
}