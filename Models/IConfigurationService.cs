using System.Collections.Generic;
using System.Threading.Tasks;

namespace LotusECMLogger.Models
{
    /// <summary>
    /// Interface for managing OBD configurations
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// Get a list of available configuration names
        /// </summary>
        /// <returns>List of configuration names</returns>
        Task<IEnumerable<string>> GetAvailableConfigurations();

        /// <summary>
        /// Load a configuration by name
        /// </summary>
        /// <param name="name">Name of the configuration to load</param>
        /// <returns>The loaded configuration</returns>
        Task<OBDConfiguration> LoadConfiguration(string name);

        /// <summary>
        /// Save a configuration
        /// </summary>
        /// <param name="name">Name to save the configuration as</param>
        /// <param name="configuration">Configuration to save</param>
        /// <returns>Task representing the async operation</returns>
        Task SaveConfiguration(string name, OBDConfiguration configuration);

        /// <summary>
        /// Delete a configuration
        /// </summary>
        /// <param name="name">Name of the configuration to delete</param>
        /// <returns>Task representing the async operation</returns>
        Task DeleteConfiguration(string name);

        /// <summary>
        /// Get the default configuration names
        /// </summary>
        /// <returns>List of default configuration names that cannot be deleted</returns>
        Task<IEnumerable<string>> GetDefaultConfigurations();

        /// <summary>
        /// Check if a configuration is a default configuration
        /// </summary>
        /// <param name="name">Name of the configuration to check</param>
        /// <returns>True if the configuration is a default configuration</returns>
        Task<bool> IsDefaultConfiguration(string name);
    }
} 