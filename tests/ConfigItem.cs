using System;
using CloudNative.Configuration.Models;
using Newtonsoft.Json;

namespace CloudNative.Tests
{
    public class ConfigItem : ConfigurationItemBase<string>
    {
        public string Name { get; set; }

        [JsonIgnore]
        public override long Version { get => base.Version; set => base.Version = value; }
    }
}
