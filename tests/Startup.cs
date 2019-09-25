using System;
using CloudNative.Configuration;
using CloudNative.Configuration.Etcd;
using Microsoft.Extensions.DependencyInjection;

namespace CloudNative.Tests
{
    public class Startup
    {
        public Startup()
        {
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();

            services.AddSingleton(new EtcdConfigurationRepositoryOptions
            {
                DnsHostOverride = "localhost",
                Username = "root",
                Password = "Faith!",
                UseEtcdClientDiscovery = false,
                UseTLS = false
            });

            services.AddTransient<IConfigurationRepository<ConfigItem, string>, ConfigItemRepository>();
        }
    }
}
