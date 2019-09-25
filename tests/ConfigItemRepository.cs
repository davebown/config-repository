using System;
using CloudNative.Configuration;
using CloudNative.Configuration.Etcd;
using Microsoft.Extensions.Logging;

namespace CloudNative.Tests
{
    /// <summary>
    /// Repository of Config Items
    /// </summary>
    [ConfigurationRepository(Scope = ConfigurationRepositoryAttribute.ScopeTypes.Global)]
    public class ConfigItemRepository : EtcdConfigurationRepository<ConfigItem, string>
    {
        public ConfigItemRepository(ILogger<EtcdConfigurationRepository<ConfigItem, string>> logger, EtcdConfigurationRepositoryOptions options)
            : base(logger, options)
        {
        }
    }
}
