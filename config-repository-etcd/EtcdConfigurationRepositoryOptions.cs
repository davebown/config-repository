using System;
namespace CloudNative.Configuration.Etcd
{
    public class EtcdConfigurationRepositoryOptions
    {
        /// <summary>
        /// Should use DNS lookup for _etcd-client SRV record, otherwise just use regular host lookup which will require 'DnsHostOverride' to be set
        /// </summary>
        public bool UseEtcdClientDiscovery { get; set; } = true;

        /// <summary>
        /// Discovery DNS domain to lookup a _etcd-client SRV record from
        /// </summary>
        public string EtcdClientDiscoveryDomain { get; set; } = "";

        /// <summary>
        /// Overrride DNS lookup to use specified hostname e.g. kubernetes headless service such as etcd.namespace.svc.cluster.local
        /// </summary>
        public string DnsHostOverride { get; set; }

        /// <summary>
        /// Override port used to communicate with etcd. By default the port specified in the SRV record or port 2379 is used if 'UseEtcdClientDiscovery' is false.
        /// </summary>
        public int? PortOverride { get; set; }

        /// <summary>
        /// Username used to authenticate to etcd cluster
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Password used to authenticate to etcd cluster
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Should connect to the cluster using TLS.
        /// </summary>
        public bool UseTLS { get; set; }
    }
}
