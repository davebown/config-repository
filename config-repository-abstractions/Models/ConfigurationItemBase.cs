using System;
namespace CloudNative.Configuration.Models
{
    public abstract class ConfigurationItemBase<TKey>
    {
        public virtual TKey Id { get; set; }

        public virtual DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;
       
        public virtual DateTimeOffset? ModifiedOn { get; set; } 

        public virtual long Version { get; set; }
    }
}
