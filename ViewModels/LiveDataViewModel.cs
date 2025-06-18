using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using LotusECMLogger.Models;
using LotusECMLogger.Services;

namespace LotusECMLogger.ViewModels
{
    public class LiveDataViewModel : ViewModelBase
    {
        private readonly IJ2534Service _j2534Service;
        private readonly ObservableCollection<LiveReading> _readings;
        private readonly ICollectionView _filteredReadings;

        private string _searchFilter = string.Empty;
        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    _filteredReadings.Refresh();
                }
            }
        }

        private string _sortProperty = "ParameterName";
        public string SortProperty
        {
            get => _sortProperty;
            set
            {
                if (SetProperty(ref _sortProperty, value))
                {
                    UpdateSort();
                }
            }
        }

        private bool _sortDescending;
        public bool SortDescending
        {
            get => _sortDescending;
            set
            {
                if (SetProperty(ref _sortDescending, value))
                {
                    UpdateSort();
                }
            }
        }

        public ICollectionView FilteredReadings => _filteredReadings;

        public RelayCommand ClearFilterCommand { get; }
        public RelayCommand<string> SortByCommand { get; }

        public LiveDataViewModel(IJ2534Service j2534Service)
        {
            _j2534Service = j2534Service ?? throw new ArgumentNullException(nameof(j2534Service));
            _readings = new ObservableCollection<LiveReading>();
            
            // Create filtered view
            _filteredReadings = CollectionViewSource.GetDefaultView(_readings);
            _filteredReadings.Filter = FilterReading;
            
            // Set up default sort
            UpdateSort();

            // Initialize commands
            ClearFilterCommand = new RelayCommand(ClearFilter);
            SortByCommand = new RelayCommand<string>(property => SortProperty = property);

            // Subscribe to service events
            _j2534Service.DataReceived += HandleDataReceived;
        }

        private bool FilterReading(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchFilter)) return true;
            if (obj is not LiveReading reading) return false;

            return reading.ParameterName.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                   reading.FormattedValue.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSort()
        {
            _filteredReadings.SortDescriptions.Clear();
            _filteredReadings.SortDescriptions.Add(
                new SortDescription(SortProperty, 
                    SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
        }

        private void ClearFilter()
        {
            SearchFilter = string.Empty;
        }

        private void HandleDataReceived(IEnumerable<LiveReading> newReadings)
        {
            foreach (var reading in newReadings)
            {
                var existingReading = _readings.FirstOrDefault(r => 
                    r.ParameterName.Equals(reading.ParameterName, StringComparison.OrdinalIgnoreCase));

                if (existingReading != null)
                {
                    var index = _readings.IndexOf(existingReading);
                    _readings[index] = reading;
                }
                else
                {
                    _readings.Add(reading);
                }
            }
        }

        public void Clear()
        {
            _readings.Clear();
        }
    }

    // RelayCommand with parameter
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return parameter is T typedParameter && 
                   (_canExecute?.Invoke(typedParameter) ?? true);
        }

        public void Execute(object? parameter)
        {
            if (parameter is T typedParameter)
            {
                _execute(typedParameter);
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
} 