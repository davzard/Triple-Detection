using System.Windows;
using System.Windows.Controls;
using TripleDetection.Application.Security;
using TripleDetection.Application.SettingsServices;
using TripleDetection.Presentation;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Presentation.Views.Settings
{
    public partial class SystemSettingsView : UserControl
    {
        private readonly SystemSettingsService _service;

        public SystemSettingsView()
        {
            InitializeComponent();
            // M6 RBAC：参数保存需工程师及以上权限
            _service = new SystemSettingsService(ViewResolver.TryResolve<IAuthorizationService>());
            LoadSettings();
        }

        public SystemSettingsView(SystemSettingsService service) : this()
        {
            _service = service;
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = _service.Load();
            txtLogRetentionDays.Text = settings.LogRetentionDays.ToString();
            txtLogExportPath.Text = settings.LogExportPath;
            chkAutoCleanLog.IsChecked = settings.AutoCleanLog;
            txtFactoryCode.Text = settings.FactoryCode;
            txtProductionLine.Text = settings.ProductionLine;
            txtDbBackupRoot.Text = settings.DbBackupRoot;
            txtImageRetentionCount.Text = settings.ImageRetentionCount.ToString();
            chkAutoCleanImages.IsChecked = settings.AutoCleanImages;

            // Set log save method
            foreach (ComboBoxItem item in cboLogSaveMethod.Items)
            {
                if (item.Tag?.ToString() == settings.LogSaveMethod)
                {
                    cboLogSaveMethod.SelectedItem = item;
                    break;
                }
            }
            if (cboLogSaveMethod.SelectedItem == null)
            {
                cboLogSaveMethod.SelectedIndex = 0;
            }
        }

        private void BrowseLogExport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择日志导出目录",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrEmpty(txtLogExportPath.Text))
            {
                dialog.SelectedPath = txtLogExportPath.Text;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtLogExportPath.Text = dialog.SelectedPath;
            }
        }

        private void BrowseDbBackup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择数据库备份目录",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrEmpty(txtDbBackupRoot.Text))
            {
                dialog.SelectedPath = txtDbBackupRoot.Text;
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtDbBackupRoot.Text = dialog.SelectedPath;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new SystemSettings
                {
                    LogSaveMethod = (cboLogSaveMethod.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ByDate",
                    LogRetentionDays = int.TryParse(txtLogRetentionDays.Text, out int logDays) ? logDays : 30,
                    LogExportPath = txtLogExportPath.Text.Trim(),
                    AutoCleanLog = chkAutoCleanLog.IsChecked ?? true,
                    FactoryCode = txtFactoryCode.Text.Trim(),
                    ProductionLine = txtProductionLine.Text.Trim(),
                    DbBackupRoot = txtDbBackupRoot.Text.Trim(),
                    ImageRetentionCount = int.TryParse(txtImageRetentionCount.Text, out int imgCount) ? imgCount : 1000,
                    AutoCleanImages = chkAutoCleanImages.IsChecked ?? true
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