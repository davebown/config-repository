using System;
using Newtonsoft.Json;

namespace CloudNative.Configuration.Models
{
    public abstract class ConfigurationItemBase<TKey>
    {
        /// <summary>
        /// Folder path (e.g. key prefix) of the configuration item.
        /// </summary>
        [JsonIgnore]
        public virtual string FolderPath { get; set; }

        /// <summary>
        /// Id of the configuration item.
        /// </summary>
        public virtual TKey Id { get; set; }

        /// <summary>
        /// Version number (modification number) of the configuration item.
        /// </summary>
        public long Version { get; set; }

        /// <summary>
        /// Time that the configuration item was created.
        /// </summary>
        public DateTimeOffset CreatedOn { get; set; }

        /// <summary>
        /// Time that the configuration item was last modified.
        /// </summary>
        public DateTimeOffset? ModifiedOn { get; set; }
    }
}
