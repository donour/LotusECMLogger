using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LotusECMLogger.Models;

namespace LotusECMLogger.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly string _configDirectory;
        private readonly HashSet<string> _defaultConfigs;
        private readonly Dictionary<string, OBDConfiguration> _configCache;

        public ConfigurationService()
        {
            _configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            _defaultConfigs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "lotus-complete",
                "lotus-default",
                "lotus-diagnostic",
                "lotus-fast"
            };
            _configCache = new Dictionary<string, OBDConfiguration>(StringComparer.OrdinalIgnoreCase);

            // Ensure config directory exists
            Directory.CreateDirectory(_configDirectory);
        }

        public async Task<IEnumerable<string>> GetAvailableConfigurations()
        {
            var configs = new List<string>();
            foreach (var file in Directory.GetFiles(_configDirectory, "*.json"))
            {
                configs.Add(Path.GetFileNameWithoutExtension(file));
            }
            return await Task.FromResult(configs);
        }

        public async Task<OBDConfiguration> LoadConfiguration(string name)
        {
            // Check cache first
            if (_configCache.TryGetValue(name, out var cachedConfig))
            {
                return cachedConfig;
            }

            var filePath = GetConfigPath(name);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration '{name}' not found", filePath);
            }

            try
            {
                var config = await OBDConfiguration.LoadFromConfig(name);
                _configCache[name] = config;
                return config;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load configuration '{name}'", ex);
            }
        }

        public async Task SaveConfiguration(string name, OBDConfiguration configuration)
        {
            if (await IsDefaultConfiguration(name))
            {
                throw new InvalidOperationException($"Cannot modify default configuration '{name}'");
            }

            var filePath = GetConfigPath(name);
            var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(filePath, json);
            _configCache[name] = configuration;
        }

        public async Task DeleteConfiguration(string name)
        {
            if (await IsDefaultConfiguration(name))
            {
                throw new InvalidOperationException($"Cannot delete default configuration '{name}'");
            }

            var filePath = GetConfigPath(name);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _configCache.Remove(name);
            }

            await Task.CompletedTask;
        }

        public async Task<IEnumerable<string>> GetDefaultConfigurations()
        {
            return await Task.FromResult(_defaultConfigs.ToList());
        }

        public async Task<bool> IsDefaultConfiguration(string name)
        {
            return await Task.FromResult(_defaultConfigs.Contains(name));
        }

        private string GetConfigPath(string name)
        {
            // Sanitize filename
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_configDirectory, $"{safeName}.json");
        }

        /// <summary>
        /// Create a copy of a configuration with a new name
        /// </summary>
        public async Task<OBDConfiguration> CloneConfiguration(string sourceName, string newName)
        {
            var sourceConfig = await LoadConfiguration(sourceName);
            var clonedConfig = sourceConfig.Clone();
            await SaveConfiguration(newName, clonedConfig);
            return clonedConfig;
        }

        /// <summary>
        /// Clear the configuration cache
        /// </summary>
        public void ClearCache()
        {
            _configCache.Clear();
        }
    }
} 