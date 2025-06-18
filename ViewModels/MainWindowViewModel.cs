using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using LotusECMLogger.Models;
using LotusECMLogger.Services;

namespace LotusECMLogger.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly IJ2534Service _j2534Service;
        private readonly IConfigurationService _configService;
        private readonly ILoggingService _loggingService;

        public LiveDataViewModel LiveDataViewModel { get; }
        public ECUCodingViewModel ECUCodingViewModel { get; }

        private string _selectedConfiguration = string.Empty;
        public string SelectedConfiguration
        {
            get => _selectedConfiguration;
            set => SetProperty(ref _selectedConfiguration, value);
        }

        private bool _isLogging;
        public bool IsLogging
        {
            get => _isLogging;
            private set
            {
                if (SetProperty(ref _isLogging, value))
                {
                    StartLoggingCommand.RaiseCanExecuteChanged();
                    StopLoggingCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private double _refreshRate;
        public double RefreshRate
        {
            get => _refreshRate;
            private set => SetProperty(ref _refreshRate, value);
        }

        private string _currentLogFile = string.Empty;
        public string CurrentLogFile
        {
            get => _currentLogFile;
            private set => SetProperty(ref _currentLogFile, value);
        }

        public ObservableCollection<string> AvailableConfigurations { get; }
        
        public RelayCommand StartLoggingCommand { get; }
        public RelayCommand StopLoggingCommand { get; }
        public RelayCommand RefreshConfigurationsCommand { get; }

        public MainWindowViewModel(
            IJ2534Service j2534Service,
            IConfigurationService configService,
            ILoggingService loggingService)
        {
            _j2534Service = j2534Service ?? throw new ArgumentNullException(nameof(j2534Service));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            AvailableConfigurations = new ObservableCollection<string>();

            // Create child ViewModels
            LiveDataViewModel = new LiveDataViewModel(j2534Service);
            ECUCodingViewModel = new ECUCodingViewModel(j2534Service);

            // Initialize commands
            StartLoggingCommand = new RelayCommand(StartLogging, CanStartLogging);
            StopLoggingCommand = new RelayCommand(StopLogging, CanStopLogging);
            RefreshConfigurationsCommand = new RelayCommand(RefreshConfigurations);

            // Subscribe to service events
            _j2534Service.ErrorOccurred += HandleError;

            // Load initial data
            _ = LoadConfigurationsAsync();
        }

        private async Task LoadConfigurationsAsync()
        {
            try
            {
                SetBusy("Loading configurations...");
                var configs = await _configService.GetAvailableConfigurations();
                
                AvailableConfigurations.Clear();
                foreach (var config in configs)
                {
                    AvailableConfigurations.Add(config);
                }

                if (AvailableConfigurations.Count > 0)
                {
                    SelectedConfiguration = AvailableConfigurations[0];
                }
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void StartLogging()
        {
            if (string.IsNullOrEmpty(SelectedConfiguration))
            {
                StatusMessage = "Please select a configuration first";
                return;
            }

            try
            {
                SetBusy("Starting logging...");
                var logFile = _loggingService.GetSuggestedLogFilePath();
                await _j2534Service.StartLogging(logFile, SelectedConfiguration);
                
                IsLogging = true;
                CurrentLogFile = logFile;
                StatusMessage = "Logging started";
            }
            catch (Exception ex)
            {
                HandleError(ex);
                await StopLogging();
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void StopLogging()
        {
            try
            {
                SetBusy("Stopping logging...");
                await _j2534Service.StopLogging();
                
                IsLogging = false;
                CurrentLogFile = string.Empty;
                StatusMessage = "Logging stopped";
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
            finally
            {
                ClearBusy();
            }
        }

        private void RefreshConfigurations()
        {
            _ = LoadConfigurationsAsync();
        }

        private bool CanStartLogging()
        {
            return !IsLogging && !string.IsNullOrEmpty(SelectedConfiguration);
        }

        private bool CanStopLogging()
        {
            return IsLogging;
        }

        private void HandleError(Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            IsLogging = false;
        }
    }

    // Simple ICommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
} 