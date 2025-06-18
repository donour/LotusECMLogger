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
    public class ECUCodingViewModel : ViewModelBase
    {
        private readonly IJ2534Service _j2534Service;
        private readonly ObservableCollection<ECUCodingOption> _codingOptions;
        private readonly ICollectionView _filteredOptions;

        private string _searchFilter = string.Empty;
        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    _filteredOptions.Refresh();
                }
            }
        }

        private bool _groupByCategory = true;
        public bool GroupByCategory
        {
            get => _groupByCategory;
            set
            {
                if (SetProperty(ref _groupByCategory, value))
                {
                    UpdateGrouping();
                }
            }
        }

        private string _selectedCategory = "All";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    _filteredOptions.Refresh();
                }
            }
        }

        public ObservableCollection<string> Categories { get; }
        public ICollectionView FilteredOptions => _filteredOptions;

        public RelayCommand ClearFilterCommand { get; }
        public RelayCommand ToggleGroupingCommand { get; }
        public RelayCommand<string> SelectCategoryCommand { get; }

        public ECUCodingViewModel(IJ2534Service j2534Service)
        {
            _j2534Service = j2534Service ?? throw new ArgumentNullException(nameof(j2534Service));
            _codingOptions = new ObservableCollection<ECUCodingOption>();
            Categories = new ObservableCollection<string> { "All" };

            // Create filtered view
            _filteredOptions = CollectionViewSource.GetDefaultView(_codingOptions);
            _filteredOptions.Filter = FilterOption;

            // Set up initial grouping
            UpdateGrouping();

            // Initialize commands
            ClearFilterCommand = new RelayCommand(ClearFilter);
            ToggleGroupingCommand = new RelayCommand(() => GroupByCategory = !GroupByCategory);
            SelectCategoryCommand = new RelayCommand<string>(category => SelectedCategory = category);

            // Subscribe to service events
            _j2534Service.CodingDataUpdated += HandleCodingDataUpdated;
        }

        private bool FilterOption(object obj)
        {
            if (obj is not ECUCodingOption option) return false;

            // Apply category filter
            if (SelectedCategory != "All" && option.Category != SelectedCategory)
                return false;

            // Apply search filter
            if (string.IsNullOrWhiteSpace(SearchFilter)) return true;

            return option.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                   option.Value.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                   option.Category.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateGrouping()
        {
            _filteredOptions.GroupDescriptions.Clear();
            if (GroupByCategory)
            {
                _filteredOptions.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            }
        }

        private void ClearFilter()
        {
            SearchFilter = string.Empty;
            SelectedCategory = "All";
        }

        private void HandleCodingDataUpdated(IEnumerable<ECUCodingOption> options)
        {
            _codingOptions.Clear();
            foreach (var option in options)
            {
                _codingOptions.Add(option);
            }

            // Update categories
            var categories = options.Select(o => o.Category).Distinct().OrderBy(c => c);
            Categories.Clear();
            Categories.Add("All");
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
        }

        public void Clear()
        {
            _codingOptions.Clear();
            Categories.Clear();
            Categories.Add("All");
            SelectedCategory = "All";
            SearchFilter = string.Empty;
        }

        /// <summary>
        /// Get a summary of the current ECU coding configuration
        /// </summary>
        public string GetConfigurationSummary()
        {
            if (!_codingOptions.Any())
                return "No coding data available";

            var summary = new System.Text.StringBuilder();
            summary.AppendLine("ECU Configuration Summary:");
            
            var byCategory = _codingOptions
                .GroupBy(o => o.Category)
                .OrderBy(g => g.Key);

            foreach (var category in byCategory)
            {
                summary.AppendLine($"\n{category.Key}:");
                foreach (var option in category.OrderBy(o => o.Name))
                {
                    summary.AppendLine($"  {option.Name}: {option.Value}");
                }
            }

            return summary.ToString();
        }

        /// <summary>
        /// Export the current configuration to a file
        /// </summary>
        public async Task ExportConfiguration(string filePath)
        {
            if (!_codingOptions.Any())
                throw new InvalidOperationException("No coding data available to export");

            var summary = GetConfigurationSummary();
            await File.WriteAllTextAsync(filePath, summary);
        }
    }
} 