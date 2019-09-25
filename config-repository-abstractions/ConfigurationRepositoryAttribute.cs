using System;
namespace CloudNative.Configuration
{
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class ConfigurationRepositoryAttribute : Attribute
    {
        public enum ScopeTypes
        {
            /// <summary>
            /// Configuration items should be available in all datacentres globally
            /// </summary>
            Global,

            /// <summary>
            /// Configureation items should be avaialble in only the local datacentre to which it is written
            /// </summary>
            Local
        }

        /// <summary>
        /// Scope of configuration items
        /// </summary>
        public ScopeTypes Scope { get; set; }
    }
}
