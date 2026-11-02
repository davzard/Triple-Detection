using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TripleDetection.Presentation.Models;
using TripleDetection.Application.Security;
using TripleDetection.Application.SettingsServices;
using TripleDetection.Presentation;

namespace TripleDetection.Presentation.Views.Settings
{
    public partial class VmSettingsView : UserControl
    {
        private readonly VmSettingsService _service;

        public VmSettingsView()
        {
            InitializeComponent();
            // M6 RBAC：参数保存需工程师及以上权限
            _service = new VmSettingsService(ViewResolver.TryResolve<IAuthorizationService>());
            LoadSettings();
        }

        public VmSettingsView(VmSettingsService service) : this()
        {
            _service = service;
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = _service.Load();
            txtVmPath.Text = settings.VmInstallPath;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择 VisionMaster 安装路径",
                ShowNewFolderButton = false
            };

            if (!string.IsNullOrEmpty(txtVmPath.Text))
            {
                dialog.SelectedPath = txtVmPath.Text;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtVmPath.Text = dialog.SelectedPath;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new VmSettings
                {
                    VmInstallPath = txtVmPath.Text.Trim()
                };

                _service.Save(settings);
                MessageBox.Show("保存成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}