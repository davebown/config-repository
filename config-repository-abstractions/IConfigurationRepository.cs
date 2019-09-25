using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CloudNative.Configuration
{
    //Configuration repository interface
    public interface IConfigurationRepository<TModel, TKey> : IDisposable where TModel : Models.ConfigurationItemBase<TKey>, new()
    {
        /// <summary>
        /// Wait till the repository is ready to serve and modify data
        /// </summary>
        Task WaitTillReady();

        /// <summary>
		/// Get all configuration records in the repository
		/// </summary>
		/// <returns>Enumeration of configuration records</returns>
        Task<IEnumerable<TModel>> GetAll();

        /// <summary>
		/// Get a configuration record from the respository by id
		/// </summary>
		/// <param name="id">Id of the configuration record</param>
        Task<TModel> Get(TKey id);

        /// <summary>
		/// Add/update a configuration reocrd in the repository
		/// </summary>
		/// <param name="configurationItem">The configuration record to add or update</param>
        /// <param name="force">Force a set even if configuraton item already exists with a different version</param>
        Task Set(TModel configurationItem, bool force = false);

		/// <summary>
		/// Remove a configuration record from the repository by id
		/// </summary>
		/// <param name="id">Id of the configuration record</param>
		Task Remove(TKey id);

        /// <summary>
		/// Event handler that is called whenever a configuration record is added, updated or deleted.
		/// </summary>
        event EventHandler<ConfigurationRepositoryEventArgs<TModel, TKey>> OnChange;

        /// <summary>
        /// Enumerable of the ids of any configuration records that have failed to load
        /// </summary>
        IEnumerable<TKey> FailedToLoad { get; }
    }

    //Event arguments sent to OnChange event handler 
    public class ConfigurationRepositoryEventArgs<TModel, TKey> : EventArgs where TModel : Models.ConfigurationItemBase<TKey>, new()
    {
        /// <summary>
        /// Collection of updated configuration records
        /// </summary>
        public IEnumerable<TModel> Updated { get; set; }

        /// <summary>
        /// Collection of removed configuration records
        /// </summary>
        public IEnumerable<TModel> Removed { get; set; }
    }
}
