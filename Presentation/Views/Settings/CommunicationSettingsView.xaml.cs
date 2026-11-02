using System.Windows;
using System.Windows.Controls;
using TripleDetection.Application.Security;
using TripleDetection.Application.SettingsServices;
using TripleDetection.Presentation;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Presentation.Views.Settings
{
    public partial class CommunicationSettingsView : UserControl
    {
        private readonly CommunicationSettingsService _service;

        public CommunicationSettingsView()
        {
            InitializeComponent();
            // M6 RBAC：参数保存需工程师及以上权限
            _service = new CommunicationSettingsService(ViewResolver.TryResolve<IAuthorizationService>());
            LoadSettings();
        }

        public CommunicationSettingsView(CommunicationSettingsService service) : this()
        {
            _service = service;
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = _service.Load();
            txtCameraIp.Text = settings.CameraIp;
            txtCameraPort.Text = settings.CameraPort.ToString();
            txtPlcIp.Text = settings.PlcIp;
            txtPlcPort.Text = settings.PlcPort.ToString();

            // Set PLC type
            foreach (ComboBoxItem item in cboPlcType.Items)
            {
                if (item.Tag?.ToString() == settings.PlcType)
                {
                    cboPlcType.SelectedItem = item;
                    break;
                }
            }
            if (cboPlcType.SelectedItem == null)
            {
                cboPlcType.SelectedIndex = 0;
            }

            // Set baud rate
            foreach (ComboBoxItem item in cboBaudRate.Items)
            {
                if (item.Tag?.ToString() == settings.BaudRate.ToString())
                {
                    cboBaudRate.SelectedItem = item;
                    break;
                }
            }
            if (cboBaudRate.SelectedItem == null)
            {
                cboBaudRate.SelectedIndex = 4; // Default to 115200
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new CommunicationSettings
                {
                    CameraIp = txtCameraIp.Text.Trim(),
                    CameraPort = int.TryParse(txtCameraPort.Text, out int camPort) ? camPort : 5000,
                    PlcIp = txtPlcIp.Text.Trim(),
                    PlcPort = int.TryParse(txtPlcPort.Text, out int plcPort) ? plcPort : 5001,
                    PlcType = (cboPlcType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Mitsubishi",
                    BaudRate = int.TryParse((cboBaudRate.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int baud) ? baud : 115200
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