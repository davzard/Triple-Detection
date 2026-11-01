using System;
using System.Windows.Input;
using Prism.Mvvm;
using Prism.Commands;
using TripleDetection.Application.Services;
using TripleDetection.Domain.Entities;
using TripleDetection.Domain;

namespace TripleDetection.Presentation.ViewModels
{
    public class LoginViewModel : BindableBase
    {
        private readonly IUserService _userService;
        private string _username = string.Empty;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isLoading;
        private bool _usernameHasError;
        private bool _passwordHasError;
        private string _logoPath;

        public LoginViewModel(IUserService userService)
        {
            _userService = userService;
            _logoPath = System.Configuration.ConfigurationManager.AppSettings["LoginLogoPath"]
                ?? System.Configuration.ConfigurationManager.AppSettings["SystemLogoPath"];

            LoginCommand = new DelegateCommand(ExecuteLogin, CanExecuteLogin)
                .ObservesProperty(() => IsLoading)
                .ObservesProperty(() => Username)
                .ObservesProperty(() => Password);
        }

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    UsernameHasError = false;
                    ErrorMessage = string.Empty;
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                {
                    PasswordHasError = false;
                    ErrorMessage = string.Empty;
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool UsernameHasError
        {
            get => _usernameHasError;
            set => SetProperty(ref _usernameHasError, value);
        }

        public bool PasswordHasError
        {
            get => _passwordHasError;
            set => SetProperty(ref _passwordHasError, value);
        }

        public string LogoPath
        {
            get => _logoPath;
            set => SetProperty(ref _logoPath, value);
        }

        public ICommand LoginCommand { get; }

        public event Action<User> LoginSucceeded;
        public event Action OnLoginFailed;

        private bool CanExecuteLogin()
        {
            return !IsLoading && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }

        private void ExecuteLogin()
        {
            UsernameHasError = string.IsNullOrWhiteSpace(Username);
            PasswordHasError = string.IsNullOrWhiteSpace(Password);
            if (UsernameHasError || PasswordHasError)
            {
                ErrorMessage = "请输入用户名和密码";
                OnLoginFailed?.Invoke();
                return;
            }

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var user = _userService.Authenticate(Username, Password);

                if (user == null)
                {
                    ErrorMessage = "用户名或密码错误";
                    OnLoginFailed?.Invoke();
                    return;
                }

                if (!user.IsEnabled)
                {
                    ErrorMessage = "账号已被禁用";
                    OnLoginFailed?.Invoke();
                    return;
                }

                if (user.IsLocked)
                {
                    ErrorMessage = "账号已被锁定";
                    OnLoginFailed?.Invoke();
                    return;
                }

                SessionManager.SetCurrentUser(user);
                LoginSucceeded?.Invoke(user);
            }
            catch (Exception ex)
            {
                ErrorMessage = "数据库连接失败，请稍后重试";
                var msg = ex.ToString();
                System.Diagnostics.Debug.WriteLine($"Login error: {msg}");
                try
                {
                    var logDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                    System.IO.Directory.CreateDirectory(logDir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(logDir, "login_error.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + msg + "\n\n");
                }
                catch { /* 日志写入失败不影响主流程 */ }
                OnLoginFailed?.Invoke();
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}