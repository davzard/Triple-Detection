using System;
using System.Collections.ObjectModel;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
// Prism 9 中 Prism.Regions 已重命名为 Prism.Navigation.Regions
using Prism.Navigation.Regions;
using TripleDetection.Application.Services;

namespace TripleDetection.Presentation.ViewModels.Detection
{
    public class MainViewModel : BindableBase
    {
        private string _resultText = "--";
        private string _resultBackground = "#808080";
        private string _details = "Detection details will appear here";
        private bool _isImageViewActive = true;
        private string _selectedProcedure = "";
        private object _currentView;
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;

        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> ResultHistory { get; } = new ObservableCollection<string>();

        public string ResultText
        {
            get => _resultText;
            set => SetProperty(ref _resultText, value);
        }

        public string ResultBackground
        {
            get => _resultBackground;
            set => SetProperty(ref _resultBackground, value);
        }

        public string Details
        {
            get => _details;
            set => SetProperty(ref _details, value);
        }

        public bool IsImageViewActive
        {
            get => _isImageViewActive;
            set => SetProperty(ref _isImageViewActive, value);
        }

        public string SelectedProcedure
        {
            get => _selectedProcedure;
            set => SetProperty(ref _selectedProcedure, value);
        }

        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public DelegateCommand NavigateToProductCommand { get; }

        public MainViewModel(IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;
            NavigateToProductCommand = new DelegateCommand(NavigateToProduct);

            // Subscribe to log events via EventAggregator
            if (_eventAggregator != null)
            {
                _eventAggregator.GetEvent<LogAddedEvent>().Subscribe(OnLogAdded);
            }
        }

        private void OnLogAdded(LogEntry entry)
        {
            AddLog(entry.Message);
        }

        private void NavigateToProduct()
        {
            CurrentView = new Views.Production.ProductListView();
        }

        public void AddLog(string message)
        {
            var logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (LogMessages.Count > 1000)
                LogMessages.RemoveAt(0);
            LogMessages.Add(logEntry);
        }

        public void AddResult(string result)
        {
            if (ResultHistory.Count > 500)
                ResultHistory.RemoveAt(0);
            ResultHistory.Add(result);
        }
    }
}