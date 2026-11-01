using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TripleDetection.Application.Services;
using TripleDetection.Application.VmServices;
using TripleDetection.Presentation.Models;
using MessageBox = System.Windows.MessageBox;
using TaskEntity = TripleDetection.Domain.Entities.ProdTask;
#if VM_SDK
using VM.Core;
using GlobalVariableModuleCs;
#endif

namespace TripleDetection.Presentation.Views.Detection
{
    public partial class DetectionView : UserControl
    {
        private readonly LoggingService _logService;
        private readonly IDetectionEngine _engine;
        private readonly ITaskService _taskService;
        private readonly IProductService _productService;

        private string _selectedSolPath;
        private bool _isSolutionLoad = false;
        private bool _isContinuRun = false;
        private List<TaskEntity> _taskList = new List<TaskEntity>();
        private TaskEntity _selectedTask;
        private int _okCount = 0;
        private int _ngCount = 0;
#if VM_SDK
        private VmProcedure _procedure;
        private VMControls.Winform.Release.VmRenderControl _vmRender;
#endif

        public DetectionView(IDetectionEngine engine, ITaskService taskService, IProductService productService, LoggingService logService)
        {
            InitializeComponent();
            _engine = engine;
            _taskService = taskService;
            _productService = productService;
            _logService = logService;

            _engine.DetectionResult += Engine_OnDetectionResult;

            LoadTasks();
            SubscribeToLogs();

            _logService.Log("检测页面已加载");
#if !VM_SDK
            _logService.Log("[Mock] 当前构建未包含 VisionMaster SDK，检测由模拟引擎执行");
#endif
        }

        private void Engine_OnDetectionResult(object sender, DetectionResult result)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateDetectionResult(result);
            });
        }

        private void LoadTasks()
        {
            var tasks = _taskService.GetAll().Where(t => t.Status == TaskStatus.Approved).ToList();
            _taskList = tasks;
            cmbTaskSelect.Items.Clear();
            foreach (var task in tasks)
            {
                cmbTaskSelect.Items.Add(task.Name);
            }
            if (tasks.Count > 0)
            {
                _logService.Log($"已加载 {tasks.Count} 个任务");
            }
        }

        private void CmbTask_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTaskSelect.SelectedItem == null) return;

            var selectedIndex = cmbTaskSelect.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _taskList.Count) return;
            _selectedTask = _taskList[selectedIndex];

            _engine.SetCurrentTaskContext(_selectedTask.Id, _selectedTask.ProductId, _selectedTask.BatchNumber,
                _selectedTask.ProductionDate, _selectedTask.ExpirationDate);

            var product = _productService.GetById(_selectedTask.ProductId);

            var productName = product?.Name ?? "--";
            var solFilePath = product?.SolFilePath ?? "--";

            txtProduct.Text = $"产品: {productName}";
            txtBatch.Text = $"批次: {_selectedTask.BatchNumber ?? "--"}";
            txtProductionDate.Text = $"生产日期: {_selectedTask.ProductionDate:yyyy-MM-dd}";
            txtExpirationDate.Text = _selectedTask.ExpirationDate.HasValue
                ? $"有效期至: {_selectedTask.ExpirationDate.Value:yyyy-MM-dd}"
                : "有效期至: --";
            txtSolFilePath.Text = $"方案路径: {solFilePath}";

            _logService.Log($"已选择任务: {_selectedTask.Name}");
        }

        private void UpdateDetectionResult(DetectionResult result)
        {
            var status = result.IsOK ? "OK" : "NG";
            txtCurrentResult.Text = status;
            txtCurrentResult.Foreground = result.IsOK
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 192, 0))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0));

            txtPassRate.Text = (_okCount + _ngCount) > 0
                ? ((double)_okCount / (_okCount + _ngCount)).ToString("P2")
                : "0%";

            if (result.IsOK) _okCount++;
            else _ngCount++;
            txtOkCount.Text = $"OK: {_okCount}";
            txtNgCount.Text = $"NG: {_ngCount}";

            var logEntry = $"[{DateTime.Now:HH:mm:ss}] {_selectedTask?.Name}: {status} | 批号={result.BatchNumber} | 生产日期={result.ProductionDate} | 有效期至={result.ExpirationDate} | 耗时={result.ElapsedMs}ms";
            lstDetectionLogs.Items.Insert(0, logEntry);
            if (lstDetectionLogs.Items.Count > 100)
                lstDetectionLogs.Items.RemoveAt(lstDetectionLogs.Items.Count - 1);

#if !VM_SDK
            // 模拟模式：显示本次保存的检测图像
            if (!string.IsNullOrEmpty(result.ImagePath) && File.Exists(result.ImagePath))
            {
                ShowMockImage(result.ImagePath);
            }
#endif
        }

#if !VM_SDK
        private void ShowMockImage(string imagePath)
        {
            try
            {
                var img = new Image
                {
                    Stretch = Stretch.Uniform,
                    Source = new BitmapImage(new Uri(imagePath))
                };
                VmRenderHost.Child = img;
                txtVmRenderPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                _logService.Log($"显示模拟图像失败: {ex.Message}");
            }
        }
#endif

        private void SubscribeToLogs()
        {
            _logService.OnLogAdded += (s, entry) =>
            {
                Dispatcher.Invoke(() =>
                {
                    lstLogs.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {entry.Message}");
                    if (lstLogs.Items.Count > 100)
                        lstLogs.Items.RemoveAt(lstLogs.Items.Count - 1);
                });
            };
        }

#if VM_SDK
        private void InitVmRender()
        {
            if (_vmRender == null)
            {
                _vmRender = new VMControls.Winform.Release.VmRenderControl();
                var host = new System.Windows.Forms.Integration.WindowsFormsHost { Child = _vmRender };
                VmRenderHost.Child = host;
            }
        }

        private VmProcedure GetProcedure()
        {
            return (_engine as VmIntegrationService)?.GetProcedure();
        }
#endif

        private void BtnSelectSol_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "VM Sol File|*.sol*";
            if (dialog.ShowDialog() == true)
            {
                _selectedSolPath = dialog.FileName;
                _isSolutionLoad = false;
                _logService.Log($"已选择方案: {_selectedSolPath}");
                MessageBox.Show("方案路径: " + _selectedSolPath + "\n下一步点击加载按钮!", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnLoadSol_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedSolPath))
            {
                if (_selectedTask != null)
                {
                    var product = _productService.GetById(_selectedTask.ProductId);
                    if (product != null && !string.IsNullOrEmpty(product.SolFilePath))
                    {
                        _selectedSolPath = product.SolFilePath;
                        _logService.Log($"已从任务获取方案路径: {_selectedSolPath}");
                    }
                }

#if VM_SDK
                if (string.IsNullOrEmpty(_selectedSolPath))
                {
                    MessageBox.Show("请先选择方案文件!", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
#else
                if (string.IsNullOrEmpty(_selectedSolPath))
                {
                    _selectedSolPath = "mock://主检测流程.sol";
                    _logService.Log("[Mock] 未指定方案文件，使用内置模拟方案");
                }
#endif
            }

            try
            {
                if (_isSolutionLoad)
                {
                    _isSolutionLoad = false;
                }

                _engine.LoadSolution(_selectedSolPath);
                _isSolutionLoad = true;

                _logService.Log("加载方案成功!");
                MessageBox.Show("加载方案成功!", "信息", MessageBoxButton.OK, MessageBoxImage.Information);

                cmbProcedure.Items.Clear();
                foreach (var name in _engine.GetAllProcedureNames())
                {
                    cmbProcedure.Items.Add(name);
                }

                if (cmbProcedure.Items.Count > 0)
                {
                    cmbProcedure.SelectedIndex = 0;
#if VM_SDK
                    _procedure = GetProcedure();
                    if (_procedure == null)
                    {
                        _logService.Log("Procedure 为空，请检查方案!");
                        return;
                    }
                    InitVmRender();
                    _vmRender.ModuleSource = _procedure;
#else
                    _logService.Log("[Mock] 模拟流程已就绪，可执行单次/连续运行");
#endif
                }
                else
                {
                    _logService.Log("流程数量为0，请检查方案!");
                }
            }
            catch (Exception ex)
            {
#if VM_SDK
                dynamic vmEx = ex;
                if (vmEx.errorCode != null)
                {
                    _logService.Log($"加载方案失败, 错误码: 0x{vmEx.errorCode:X}");
                    MessageBox.Show($"加载方案失败: 0x{vmEx.errorCode:X}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
#endif
                {
                    _logService.Log($"加载方案失败: {ex.Message}");
                    MessageBox.Show($"加载方案失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnSaveSol_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTask == null)
            {
                MessageBox.Show("请先选择任务!", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_isSolutionLoad)
            {
                MessageBox.Show("请先加载方案!", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var batchNumber = _selectedTask.BatchNumber ?? "";
                var mfgDate = _selectedTask.ProductionDate.ToString("yyyyMMdd");
                var expDate = _selectedTask.ExpirationDate.HasValue
                    ? _selectedTask.ExpirationDate.Value.ToString("yyyyMMdd")
                    : "";

                _engine.SetGlobalVariableString("BN", batchNumber);
                _engine.SetGlobalVariableString("Mfg", mfgDate);
                _engine.SetGlobalVariableString("EXP", expDate);

                _logService.Log($"三期信息已设置ToVM: BN={batchNumber}, Mfg={mfgDate}, EXP={expDate}");
                MessageBox.Show($"三期信息已设置:\nBN={batchNumber}\nMfg={mfgDate}\nEXP={expDate}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logService.Log($"设置三期信息失败: {ex.Message}");
                MessageBox.Show($"设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void BtnLoadFromVm_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSolutionLoad || !_engine.HasProcedure)
            {
                MessageBox.Show("请先加载方案!", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string bn = _engine.GetGlobalVariableString("BN") ?? "";
                string mfg = _engine.GetGlobalVariableString("Mfg") ?? "";
                string exp = _engine.GetGlobalVariableString("EXP") ?? "";

                _logService.Log($"三期信息已获取FromVM: BN={bn}, Mfg={mfg}, EXP={exp}");
                MessageBox.Show($"三期信息:\nBN={bn}\nMfg={mfg}\nEXP={exp}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logService.Log($"获取三期信息失败: {ex.Message}");
                MessageBox.Show($"获取失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CmbProcedure_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbProcedure.SelectedItem == null) return;

            try
            {
                _engine.SetProcedure(cmbProcedure.SelectedItem.ToString());
#if VM_SDK
                _procedure = GetProcedure();
                if (_vmRender != null)
                    _vmRender.ModuleSource = _procedure;
#endif

                _logService.Log($"已选择 [{cmbProcedure.SelectedItem}]");
            }
            catch (Exception ex)
            {
#if VM_SDK
                dynamic vmEx = ex;
                if (vmEx.errorCode != null)
                    _logService.Log($"选择流程失败, 错误码: 0x{vmEx.errorCode:X}");
                else
#endif
                    _logService.Log($"选择流程失败: {ex.Message}");
            }
        }

        private void BtnTaskRun_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSolutionLoad || !_engine.HasProcedure)
            {
                _logService.Log("流程不存在!");
                return;
            }

            try
            {
                _engine.RunOnce();
                _logService.Log("单次运行已触发，等待结果回调...");
            }
            catch (Exception ex)
            {
#if VM_SDK
                dynamic vmEx = ex;
                if (vmEx.errorCode != null)
                    _logService.Log($"单次运行失败, 错误码: 0x{vmEx.errorCode:X}");
                else
#endif
                    _logService.Log($"单次运行失败: {ex.Message}");
            }
        }

        private void BtnTaskPause_Click(object sender, RoutedEventArgs e)
        {
            if (!_isSolutionLoad || !_engine.HasProcedure)
            {
                _logService.Log("流程不存在!");
                return;
            }

            try
            {
                bool newState = !_engine.IsContinuousRun;
                _engine.SetContinuousRun(newState);
                _isContinuRun = newState;
                _logService.Log($"连续运行: {newState}");
                btnContiRun.Content = _isContinuRun ? "停止连续" : "连续运行";
            }
            catch (Exception ex)
            {
#if VM_SDK
                dynamic vmEx = ex;
                if (vmEx.errorCode != null)
                    _logService.Log($"连续运行切换失败, 错误码: 0x{vmEx.errorCode:X}");
                else
#endif
                    _logService.Log($"连续运行切换失败: {ex.Message}");
            }
        }
    }
}
