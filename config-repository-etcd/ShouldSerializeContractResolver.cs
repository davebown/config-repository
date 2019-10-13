using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CloudNative.Configuration.Etcd
{
    public class ShouldSerializeContractResolver : DefaultContractResolver
    {
        /// <summary>
        /// Serialize all properties except id and version
        /// </summary>
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName == "id" || property.PropertyName == "version")
            {
                property.ShouldSerialize = instance =>
                {
                    return false;
                };
            }
            return property;
        }
    }
}
