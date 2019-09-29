using CloudNative.Configuration.Models;

namespace CloudNative.Tests
{
    public class ConfigItem : ConfigurationItemBase<string>
    {
        public string Name { get; set; }
    }

    public class ChildConfigItem : ConfigItem
    {
        public string Description { get; set; }
    }
}
