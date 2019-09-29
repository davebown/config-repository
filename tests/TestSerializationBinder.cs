using System;
using Newtonsoft.Json.Serialization;

namespace CloudNative.Tests
{
    public class TestSerializationBinder : DefaultSerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            switch (typeName)
            {
                case "CloudNative.Tests.ChildConfigItem": return typeof(ChildConfigItem);
                default: return base.BindToType(assemblyName, typeName);
            }
        }
    }
}
