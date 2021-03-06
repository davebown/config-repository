﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DnsClient;
using dotnet_etcd;
using Etcdserverpb;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace CloudNative.Configuration.Etcd
{
    public abstract class EtcdConfigurationRepository<TModel, TKey> : IConfigurationRepository<TModel, TKey>, IDisposable where TModel : Models.ConfigurationItemBase<TKey>, new()
    {
        //Logger for repos
        readonly ILogger<EtcdConfigurationRepository<TModel, TKey>> _logger;

        //Initialisation task triggered by the constructor
        readonly Task _initTask;
        //Task completion source used to indicate when initial data is loaded
        readonly TaskCompletionSource<bool> _dataLoaded = new TaskCompletionSource<bool>();

        //Readiness task completes when data 
        readonly Task _readyTask;
        //Serializer settings to serialize json objects to etcd key/vaule store
        readonly JsonSerializerSettings _jsonSerializerSettings;
        //Set of keys that have failed to load
        readonly ConcurrentHashSet<string> _failedKeys = new ConcurrentHashSet<string>();

        //Client used for interaction with etcd cluster
        EtcdClient _etcdClient;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="options">Repository configuration options</param>
        public EtcdConfigurationRepository(ILogger<EtcdConfigurationRepository<TModel, TKey>> logger, EtcdConfigurationRepositoryOptions options)
        {
            _logger = logger;

            //Initialise serializer settings
            _jsonSerializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                //Use custom serializer to ensure 'id' and 'version' properties don't get written to etcd
                ContractResolver = new ShouldSerializeContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                },
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None,
                SerializationBinder = new DefaultSerializationBinder(),
            };

            //Create an initialisation task to create etcd client and initialise the watcher
            _initTask = CreateClient(options)
                .ContinueWith(async result =>
                {
                    if (result.IsFaulted)
                    {
                        throw result.Exception;
                    }
                    await Initialise(result.Result);
                    _etcdClient = result.Result;
                }).Unwrap();

            //Setup ready task to complete when initialisation is complete and initial data is loaded
            _readyTask = Task.WhenAll(_initTask, _dataLoaded.Task);
        }

        /// <summary>
        /// Wait till the repository is ready to serve and modify data
        /// </summary>
        public virtual Task WaitTillReady()
        {
            return _readyTask;
        }

        /// <summary>
        /// Get a configuration record from the respository by id
        /// </summary>
        /// <param name="id">Id of the configuration record</param>
        /// <remarks>For configuration records without an assigned folder</remarks>
        public virtual Task<TModel> Get(TKey id)
        {
            return Get(null, id);
        }

        /// <summary>
        /// Get a configuration record from the respository by folder and id
        /// </summary>
        /// <param name="folderPath">Folder of the configuration record</param>
        /// <param name="id">Id of the configuration record</param>
        public virtual async Task<TModel> Get(string folderPath, TKey id)
        {
            if (id.Equals(default(TKey)))
            {
                throw new ArgumentNullException(nameof(id));
            }
            //Ensure the repository is initialised before retreiving item
            await _readyTask.ConfigureAwait(false);
            if (ConfigurationItems.TryGetValue(CreateEtcdKey(folderPath, id), out TModel configurationItem))
            {
                return configurationItem;
            }
            return null;
        }

        /// <summary>
        /// Get all configuration records in the repository
        /// </summary>
        /// <returns>Enumeration of configuration records</returns>
        public virtual async Task<IEnumerable<TModel>> GetAll()
        {
            //Ensure the repository is initialised before retreiving item
            await _readyTask.ConfigureAwait(false);
            return ConfigurationItems.Values;
        }

        /// <summary>
        /// Add/update a configuration reocrd in the repository
        /// </summary>
        /// <param name="configurationItem">The configuration record to add or update</param>
        /// <param name="force">Force a set (overwrite) even if configuraton item already exists with a different version</param>
        public virtual async Task Set(TModel configurationItem, bool force = false)
        {
            if (configurationItem == null)
            {
                throw new ArgumentNullException(nameof(configurationItem));
            }

            //Set created timestamp for version 0
            if (configurationItem.Version == 0 || force)
            {
                configurationItem.CreatedOn = DateTimeOffset.UtcNow;
            }
            //Otherwise set modified timestamp
            else
            {
                configurationItem.ModifiedOn = DateTimeOffset.UtcNow;
            }

            //Create the etcd key for the configuration item
            var etcdKey = CreateEtcdKey(configurationItem);

            //Serialise configuration item as JSON to persist to etcd
            var json = JsonConvert.SerializeObject(configurationItem, typeof(TModel), _jsonSerializerSettings);

            //Ensure the repository is initialised before retreiving item
            await _readyTask.ConfigureAwait(false);

            //If we force an update then we just put the value without a check
            if (force)
            {
                //Put the key/value into etcd
                var setReponse = await _etcdClient.PutAsync(etcdKey, json).ConfigureAwait(false);
                //Update the configuration item version
                configurationItem.Version = setReponse.Header.Revision;
            }
            //If we do not force update we need to do a CAS (check and set) which will eliminate concurrency issues and accidental overwrites
            else
            {
                //Create a transaction that checks the ModRevision is the same as the configuration item before performing the put operation
                var transReq = new TxnRequest();
                if (configurationItem.Version == 0)
                {
                    transReq.Compare.Add(new Compare
                    {
                        Key = Google.Protobuf.ByteString.CopyFromUtf8(etcdKey),
                        Result = Compare.Types.CompareResult.Equal,
                        CreateRevision = configurationItem.Version,
                        Target = Compare.Types.CompareTarget.Create
                    });
                }
                else
                {
                    transReq.Compare.Add(new Compare
                    {
                        Key = Google.Protobuf.ByteString.CopyFromUtf8(etcdKey),
                        Result = Compare.Types.CompareResult.Equal,
                        ModRevision = configurationItem.Version,
                        Target = Compare.Types.CompareTarget.Mod
                    });
                }
                transReq.Success.Add(new RequestOp
                {
                    RequestPut = new PutRequest
                    {
                        Key = Google.Protobuf.ByteString.CopyFromUtf8(etcdKey),
                        Value = Google.Protobuf.ByteString.CopyFromUtf8(json)
                    }
                });
                //Execute transaction
                var transResponse = await _etcdClient.TransactionAsync(transReq);

                //Check the transaction succeeded
                if (transResponse.Succeeded)
                {
                    //Update the configuration item version
                    configurationItem.Version = transResponse.Responses[0].ResponsePut.Header.Revision;
                }
                else
                {
                    throw new ArgumentException("Unable to set configuration item, either the specified item does not exist or the version specified was incorrect.");
                }
            }
        }

        /// <summary>
        /// Remove a configuration record from the repository by id
        /// </summary>
        /// <param name="configurationItem">The configuration record to remove</param>
        public virtual Task Remove(TModel configurationItem)
        {
            if (configurationItem == null)
            {
                throw new ArgumentNullException(nameof(configurationItem));
            }
            return Remove(configurationItem.FolderPath, configurationItem.Id);
        }

        /// <summary>
        /// Remove a configuration record from the repository by id
        /// </summary>
        /// <param name="id">Id of the configuration record</param>
        /// <remarks>For configuration records without an assigned folder</remarks>
        public virtual Task Remove(TKey id)
        {
            return Remove(null, id);
        }

        /// <summary>
        /// Remove a configuration record from the repository by folder and id
        /// </summary>
        /// <param name="folderPath">Folder of configuration record</param>
        /// <param name="id">Id of the configuration record</param>
        public virtual async Task Remove(string folderPath, TKey id)
        {
            if (id.Equals(default(TKey)))
            {
                throw new ArgumentNullException(nameof(id));
            }
            //Ensure the repository is initialised before retreiving item
            await _readyTask.ConfigureAwait(false);
            //Delete the item from the repository
            await _etcdClient.DeleteAsync(CreateEtcdKey(folderPath, id)).ConfigureAwait(false);
        }

        /// <summary>
		/// Event handler that is called whenever a configuration record is added, updated or deleted.
		/// </summary>
        public event EventHandler<ConfigurationRepositoryEventArgs<TModel, TKey>> OnChange;

        /// <summary>
        /// Initialise etcd client
        /// </summary>
        /// <param name="options"></param>
        private async Task<EtcdClient> CreateClient(EtcdConfigurationRepositoryOptions options)
        {
            EtcdClient etcdClient = null;

            var lookup = new LookupClient();

            if (options.UseEtcdClientDiscovery)
            {
                var dnsHostName = string.IsNullOrEmpty(options.DnsHostOverride)
                    ? $"_etcd-client${(options.UseTLS ? "-ssl" : "")}.${options.EtcdClientDiscoveryDomain}"
                    : options.DnsHostOverride;

                _logger.LogInformation("Attempting service discovery using hostname {dnsHostName}", dnsHostName);

                var dnsResponse = await lookup.QueryAsync(dnsHostName, QueryType.SRV);

                if (dnsResponse.Answers.Count > 0)
                {
                    foreach (var srvRecord in dnsResponse.Answers.SrvRecords())
                    {
                        _logger.LogInformation($"Connecting to etcd host {srvRecord.Target} using port {options.PortOverride ?? srvRecord.Port}.");
                        var tmpEtcdClient = new EtcdClient(srvRecord.Target, options.PortOverride ?? srvRecord.Port, options.Username, options.Password);
                        try
                        {
                            await tmpEtcdClient.StatusASync(new Etcdserverpb.StatusRequest());
                            etcdClient = tmpEtcdClient;
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to connect to etcd cluster.");
                            tmpEtcdClient.Dispose();
                        }
                    }
                }

                if (etcdClient == null)
                {
                    if (!options.UseTLS) //Try again with TLS
                    {
                        _logger.LogInformation("Retrying discovery etcd cluster using TLS.");
                        options.UseTLS = true;
                        etcdClient = await CreateClient(options);
                    }
                    else
                    {
                        throw new InvalidOperationException("Unable to connect to a etcd cluster because service discovery operations failed.");
                    }
                }
            }
            else if (!string.IsNullOrEmpty(options.DnsHostOverride))
            {
                _logger.LogInformation($"Connecting to etcd host {options.DnsHostOverride} using port {options.PortOverride ?? 2379}.");
                etcdClient = new EtcdClient(options.DnsHostOverride, options.PortOverride ?? 2379, options.Username, options.Password);
            }
            else
            {
                throw new ArgumentException("Must specify 'DnsHostOverride option' if 'UseEtcdDiscovery' option is set to false.");
            }

            return etcdClient;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            _etcdClient?.Dispose();
        }

        /// <summary>
        /// Dictionary to hold the cached configuration items
        /// </summary> 
        protected ConcurrentDictionary<string, TModel> ConfigurationItems { get; } = new ConcurrentDictionary<string, TModel>();

        /// <summary>
        /// Base key of the repository
        /// </summary>
        protected virtual string BaseKey => $"/{this._scope.ToString().ToLower()}/{typeof(TModel).Name.ToLower()}";

        /// <summary>
        /// Expose JSON serializer settings to derived types
        /// </summary>
        protected JsonSerializerSettings JsonSerializerSettings => _jsonSerializerSettings;

        /// <summary>
        /// Enumerable of the ids of any configuration records that have failed to load
        /// </summary>
        public IEnumerable<TKey> FailedToLoad => _failedKeys.Select(key => ConvertEtcdKeyToId(key));

        /// <summary>
        /// Create etcd key for a specified configuration item
        /// </summary>
        /// <param name="configurationItem">Configuration item</param>
        /// <returns>EtcdKey</returns>
        protected string CreateEtcdKey(TModel configurationItem)
        {
            return CreateEtcdKey(configurationItem.FolderPath, configurationItem.Id);
        }

        /// <summary>
        /// Create etcd key for a specified configuration item id
        /// </summary>
        /// <param name="folderPath">Folder of configuration item</param>
        /// <param name="id">Id of a configuration item</param>
        /// <returns>EtcdKey</returns>
        protected string CreateEtcdKey(string folderPath, TKey id)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return $"{BaseKey}/{id}";
            }
            return $"{BaseKey}{(folderPath.StartsWith("/", StringComparison.InvariantCulture) ? folderPath : $"/{folderPath}")}/{id}";
        }

        /// <summary>
        /// Extract id from etcd key and convert to TKey type
        /// </summary>
        /// <param name="etcdKey">The key of the etcd key/value</param>
        /// <returns>Extract id</returns>
        protected TKey ConvertEtcdKeyToId(string etcdKey)
        {
            var idKeyPart = etcdKey.Substring(etcdKey.LastIndexOf("/", StringComparison.CurrentCulture) + 1);

            if (typeof(TKey) == typeof(Guid))
            {
                return (TKey)(object)Guid.Parse(idKeyPart);
            }

            return (TKey)Convert.ChangeType(idKeyPart, typeof(TKey));
        }

        /// <summary>
        /// Extract folder path from etcd key
        /// </summary>
        /// <param name="etcdKey">The key of the etcd key/value</param>
        /// <returns>Extracted folder string</returns>
        protected string ConvertEtcdKeyToFolderPath(string etcdKey)
        {
            var keyParts = etcdKey.Split('/');

            if (keyParts.Length <= 3)
            {
                return null;
            }

            var folderPath = "";

            for (var i = 3; i < keyParts.Length - 1; i++)
            {
                folderPath += $"/{keyParts[i]}";
            }

            return folderPath;
        }

        /// <summary>
        /// Scope of the configuration items
        /// </summary>
        ConfigurationRepositoryAttribute.ScopeTypes _scope;

        /// <summary>
        /// Initialise the 
        /// </summary>
        private async Task Initialise(EtcdClient etcdClient)
        {
            var attribute = this.GetType().GetTypeInfo().GetCustomAttribute<ConfigurationRepositoryAttribute>(true);

            if (attribute == null)
            {
                throw new InvalidOperationException("You must assign an attribute of type 'ConfigurationRepisotryAttribute' to the repository implementation.");
            }

            _scope = attribute.Scope;

            try
            {

                var existingEntries = await etcdClient.GetRangeAsync($"{BaseKey}/");

                long maxRevision = 0;

                foreach (var entry in existingEntries.Kvs)
                {
                    var etcdKey = entry.Key.ToStringUtf8();
                    try
                    {
                        //De-serialize configuration item from value in JSON format
                        var configurationItem = JsonConvert.DeserializeObject<TModel>(entry.Value.ToStringUtf8(), _jsonSerializerSettings);
                        //Get config item id from key of etcd item
                        configurationItem.Id = ConvertEtcdKeyToId(etcdKey);
                        //Get config item folder path (key prefix)
                        configurationItem.FolderPath = ConvertEtcdKeyToFolderPath(etcdKey);
                        //Assign version to Modification revision
                        configurationItem.Version = entry.ModRevision;
                        //Add configuration item to local cache
                        ConfigurationItems[etcdKey] = configurationItem;
                        //Capture the highest modification revision
                        if (entry.ModRevision > maxRevision)
                        {
                            maxRevision = entry.ModRevision;
                        }
                    }
                    catch (Exception ex)
                    {
                        _failedKeys.Add(etcdKey); //Add key to failed keys set
                        _logger.LogCritical(ex, "Error loading etcd keyvalue with key: {key}", etcdKey);
                    }
                }

                etcdClient.WatchRange(new Etcdserverpb.WatchRequest
                {
                    CreateRequest = new Etcdserverpb.WatchCreateRequest
                    {
                        Key = Google.Protobuf.ByteString.CopyFromUtf8($"{BaseKey}/"),
                        RangeEnd = Google.Protobuf.ByteString.CopyFromUtf8($"{BaseKey}0"),
                        StartRevision = maxRevision
                    }
                }, OnWatchResponse);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Error during initialisation of etcd repository.");
                throw;
            }
        }

        /// <summary>
        /// On watch response update local cache and fire OnChange event
        /// </summary>
        /// <param name="watchResponse"></param>
        private void OnWatchResponse(WatchResponse watchResponse)
        {
            var updatedItems = new List<TModel>();
            var removedItems = new List<TModel>();

            foreach (var watchEvent in watchResponse.Events)
            {
                //Get key of watch event
                var etcdKey = watchEvent.Kv.Key.ToStringUtf8();

                try
                {
                    if (watchEvent.Type == Mvccpb.Event.Types.EventType.Delete)
                    {
                        //Remove any existing configuration item if exists and add to removed items for OnChange event
                        if (ConfigurationItems.TryRemove(etcdKey, out TModel removed))
                        {
                            removedItems.Add(removed);
                        }
                        //If we the configuration item wasn't removed, then we must have missed an update, so just generate empty entity with the id and add to removed items for OnChange event
                        else
                        {
                            removedItems.Add(new TModel { Id = ConvertEtcdKeyToId(etcdKey) });
                        }
                    }
                    else
                    {
                        //De-serialize configuration item from value in JSON format
                        var configurationItem = JsonConvert.DeserializeObject<TModel>(watchEvent.Kv.Value.ToStringUtf8(), _jsonSerializerSettings);
                        //Assign id to configuration item
                        configurationItem.Id = ConvertEtcdKeyToId(etcdKey);
                        //Get config item folder path (key prefix)
                        configurationItem.FolderPath = ConvertEtcdKeyToFolderPath(etcdKey);
                        //Assign version to Modification revision
                        configurationItem.Version = watchEvent.Kv.ModRevision;
                        //Add to updated items list for OnChange event
                        updatedItems.Add(configurationItem);
                        //Add or update the configuration item in the local cache
                        ConfigurationItems.AddOrUpdate(etcdKey, configurationItem, (key, existing) => configurationItem);
                    }

                    //Remove from failed keys if exists
                    _failedKeys.Remove(etcdKey);
                }
                catch (Exception ex)
                {
                    _failedKeys.Add(etcdKey); //Add key to failed keys set
                    _logger.LogError(ex, "Error during de-serialization or processing watch {eventType} event with key: {key}", watchEvent.Type, etcdKey);
                }
            }

            //Safely invoke OnChange event delegates in a thread-safe way
            System.Threading.Volatile.Read(ref OnChange)?.Invoke(this, new ConfigurationRepositoryEventArgs<TModel, TKey>
            {
                Updated = updatedItems,
                Removed = removedItems
            });

            //Set data loaded task completion source
            _dataLoaded.TrySetResult(true);
        }
    }
}
