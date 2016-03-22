using log4net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HDInsightManagementCLI
{
    public class AzureStorageConfig
    {
        public string Name { get; set; }
        public string Key { get; set; }
        public string Container { get; set; }
    }

    public class SqlAzureConfig
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Type { get; set; }
    }

    public class Config
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(HDInsightManagementCLI));

        public enum ConfigName
        {
            AzureManagementUri,
            AzureResourceManagementUri,
            AzureResourceProviderNamespace,
            AzureActiveDirectoryUri,
            AzureActiveDirectoryRedirectUri,
            AzureActiveDirectoryTenantId,
            AzureActiveDirectoryClientId,
            SubscriptionId,
            ResourceGroupName,
            ClusterLocation,
            ClusterDnsName,
            ClusterSize,
            ClusterUsername,
            ClusterPassword,
            SshUsername,
            SshPassword,
            SshPublicKeyFilePath,
            HDInsightVersion,
            DefaultStorageAccountName,
            DefaultStorageAccountKey,
            DefaultStorageAccountContainer,
            AdditionalStorageAccountNames,
            AdditionalStorageAccountKeys,
            SqlAzureAsMetastore,
            SqlHiveMetastoreServer,
            SqlHiveMetastoreDatabase,
            SqlHiveMetastoreUser,
            SqlHiveMetastorePassword,
            SqlOozieMetastoreDatabase,
            SqlOozieMetastoreServer,
            SqlOozieMetastoreUser,
            SqlOozieMetastorePassword,
            OperationPollIntervalInSeconds,
            TimeoutPeriodInMinutes,
            DeleteCutoffPeriodInHours,
            CleanupOnError,
            SilentMode,
            AutoEnableRdp,
            RdpUsername,
            RdpPassword,
            RdpExpirationInDays,
            ClusterType,
            OperatingSystemType,
            HeadNodeSize,
            WorkerNodeSize,
            ZookeeperSize,
            VirtualNetworkId,
            SubnetName,
        }

        private readonly Dictionary<ConfigName, string> _overrides =
            new Dictionary<ConfigName, string>();

        public Config(string[] args)
        {
            var argsList = new List<string>(args);
            foreach (ConfigName config in System.Enum.GetValues(typeof(ConfigName)))
            {
                if (argsList.Count > 0)
                {
                    string result;

                    if (ApplicationUtilities.TryGetArgumentValue(argsList, string.Format("/{0}:", config), out result))
                    {
                        _overrides.Add(config, result);
                    }
                }
                else
                {
                    break;
                }
            }

            //Ignore the first valid command
            if (argsList.Count - 1 != _overrides.Count)
            {
                Logger.InfoFormat("WARNING! Override parse count mismatch. Overrides Passed: {0}, Overrides Parsed: {1}", argsList.Count - 1, _overrides.Count);
            }
        }

        public string AzureManagementUri
        {
            get { return GetConfigurationValue(ConfigName.AzureManagementUri); }
        }

        public string AzureResourceManagementUri
        {
            get { return GetConfigurationValue(ConfigName.AzureResourceManagementUri); }
        }

        public string AzureResourceProviderNamespace
        {
            get { return GetConfigurationValue(ConfigName.AzureResourceProviderNamespace); }
        }
        
        public string AzureActiveDirectoryUri
        {
            get { return GetConfigurationValue(ConfigName.AzureActiveDirectoryUri); }
        }

        public string AzureActiveDirectoryRedirectUri
        {
            get { return GetConfigurationValue(ConfigName.AzureActiveDirectoryRedirectUri); }
        }

        public string AzureActiveDirectoryTenantId
        {
            get { return GetConfigurationValue(ConfigName.AzureActiveDirectoryTenantId); }
        }

        public string AzureActiveDirectoryClientId
        {
            get { return GetConfigurationValue(ConfigName.AzureActiveDirectoryClientId); }
        }

        public string ClusterType
        {
            get
            {
                return GetConfigurationValue(ConfigName.ClusterType);
            }
        }

        public string OSType
        {
            get
            {
                return GetConfigurationValue(ConfigName.OperatingSystemType);
            }
        }

        private string _subscriptionId = null;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public string SubscriptionId
        {
            get
            {
                if (_subscriptionId == null)
                {
                    _subscriptionId = GetConfigurationValue(ConfigName.SubscriptionId);
                    Guid guid;
                    var result = Guid.TryParse(_subscriptionId, out guid);
                    if (!result)
                    {
                        throw new ApplicationException(
                            String.Format(
                                "SubscriptionId: '{0}' is not a valid value, please use a valid subscription id or a well formed guid",
                                _subscriptionId));
                    }
                }
                return _subscriptionId;
            }
        }

        public string ClusterLocation
        {
            get { return GetConfigurationValue(ConfigName.ClusterLocation); }
        }

        private string _clusterDnsName = null;
        public string ClusterDnsName
        {
            get
            {
                if (_clusterDnsName == null)
                {
                    _clusterDnsName = GetConfigurationValue(ConfigName.ClusterDnsName);
                }

                return _clusterDnsName;
            }
            set
            {
                _clusterDnsName = value;
            }
        }

        private string _resourceGroupName = null;
        public string ResourceGroupName
        {
            get
            {
                if (_resourceGroupName == null)
                {
                    var result = TryGetConfigurationValue(ConfigName.ResourceGroupName, out _resourceGroupName);

                    if(!result || String.IsNullOrWhiteSpace(_resourceGroupName))
                    {
                        _resourceGroupName = HDInsightManagementCLIHelpers.GetResourceGroupName(SubscriptionId, ClusterLocation);
                    }
                }

                return _resourceGroupName;
            }
            set
            {
                _resourceGroupName = value;
            }
        }

        public string ClusterUsername
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.ClusterUsername, out returnValue);
                if (!result)
                {
                    returnValue = "admin";
                }
                return returnValue;
            }
        }

        private string _clusterPassword = null;
        public string ClusterPassword
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_clusterPassword))
                {
                    bool result = TryGetConfigurationValue(ConfigName.ClusterPassword, out _clusterPassword);
                    if (!result)
                    {
                        Logger.InfoFormat("Generating random cluster password of length 24 using System.Web.Security.Membership.GeneratePassword");
                        do
                        {
                            _clusterPassword = System.Web.Security.Membership.GeneratePassword(24, 2);
                        }
                        while (!Regex.IsMatch(_clusterPassword, HDInsightManagementCLIHelpers.HDInsightPasswordValidationRegex));
                        Logger.InfoFormat("PLEASE NOTE: New cluster password: {0}", _clusterPassword);
                        Logger.InfoFormat("If the cluster was created previously you should use the previously generated password instead.");
                        SaveConfigurationValue(ConfigName.ClusterPassword, _clusterPassword);
                    }
                }
                return _clusterPassword;
            }
            set
            {
                _clusterPassword = value;
            }
        }

        public string SshUsername
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.SshUsername, out returnValue);
                if (!result)
                {
                    returnValue = "ssh" + ClusterUsername;
                }
                return returnValue;
            }
        }

        public string SshPassword
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.SshPassword, out returnValue);
                return returnValue;
            }
        }

        public string SshPublicKeyFilePath
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.SshPublicKeyFilePath, out returnValue);
                if(!result)
                {
                    var keyPair = HDInsightManagementCLIHelpers.GenerateSshKeyPair(ClusterDnsName, ClusterPassword);
                    Logger.InfoFormat("PLEASE NOTE: A new SSH key pair was generated. Public Key Path: {0}, Private Key Path: {1}, Passphrase: {2}", keyPair.Key, keyPair.Value, ClusterPassword);
                    Logger.InfoFormat("This new key path will saved in your application configuration, the passphrase is same as your cluster password.");
                    returnValue = keyPair.Key;
                    SaveConfigurationValue(ConfigName.SshPublicKeyFilePath, returnValue);
                }
                return returnValue;
            }
        }

        public int ClusterSize
        {
            get
            {
                int defaultValue = 3;
                int returnValue = defaultValue;
                string outValue = null;
                bool result = TryGetConfigurationValue(ConfigName.ClusterSize, out outValue);
                if (result)
                {
                    bool flag = int.TryParse(outValue, out returnValue);
                    if (!flag)
                    {
                        returnValue = defaultValue;
                    }
                }
                return returnValue;
            }
        }

        public string HDInsightVersion
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.HDInsightVersion, out returnValue);
                if (!result)
                {
                    returnValue = "default";
                }
                return returnValue;
            }
        }

        private AzureStorageConfig _defaultStorageAccount;
        public AzureStorageConfig DefaultStorageAccount
        {
            get
            {
                if(_defaultStorageAccount == null)
                {
                    _defaultStorageAccount = new AzureStorageConfig()
                    {
                        Name = GetConfigurationValue(ConfigName.DefaultStorageAccountName),
                        Key = GetConfigurationValue(ConfigName.DefaultStorageAccountKey)
                    };

                    string container = null;
                    var result = TryGetConfigurationValue(ConfigName.DefaultStorageAccountContainer, out container);
                    if(result && !String.IsNullOrEmpty(container))
                    {
                        _defaultStorageAccount.Container = container;
                    }
                }
                return _defaultStorageAccount;
            }
            set
            {
                _defaultStorageAccount = value;
            }
        }

        private List<AzureStorageConfig> _additionalStorageAccounts = null;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public List<AzureStorageConfig> AdditionalStorageAccounts
        {
            get
            {
                if (_additionalStorageAccounts == null)
                {
                    _additionalStorageAccounts = new List<AzureStorageConfig>();

                    var additionalStorageAccountNames = GetConfigurationValue(ConfigName.AdditionalStorageAccountNames).Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    var additionalStorageAccountKeys = GetConfigurationValue(ConfigName.AdditionalStorageAccountKeys).Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

                    if (additionalStorageAccountNames.Count() != additionalStorageAccountKeys.Count())
                    {
                        throw new ApplicationException(
                            String.Format("AdditionalStorageAccountNames.Count: {0} is not equal to AdditionalStorageAccountKeys.Count: {1}",
                                          additionalStorageAccountNames.Count(), additionalStorageAccountKeys.Count()));
                    }

                    for (int i = 0; i < additionalStorageAccountNames.Count(); i++)
                    {
                        var additionalStorageAccount = new AzureStorageConfig()
                        {
                            Name = additionalStorageAccountNames[i],
                            Key = additionalStorageAccountKeys[i]
                        };

                        _additionalStorageAccounts.Add(additionalStorageAccount);
                    }
                }
                return _additionalStorageAccounts;
            }
            set
            {
                _additionalStorageAccounts = value;
            }
        }

        private List<SqlAzureConfig> _sqlAzureMetastores = null;
        public List<SqlAzureConfig> SqlAzureMetastores
        {
            get
            {
                if (_sqlAzureMetastores == null && SqlAzureAsMetastore)
                {
                    _sqlAzureMetastores = new List<SqlAzureConfig>();

                    var sqlHive = new SqlAzureConfig()
                    {
                        Server = GetConfigurationValue(ConfigName.SqlHiveMetastoreServer),
                        Database = GetConfigurationValue(ConfigName.SqlHiveMetastoreDatabase),
                        User = GetConfigurationValue(ConfigName.SqlHiveMetastoreUser),
                        Password = GetConfigurationValue(ConfigName.SqlHiveMetastorePassword),
                        Type = "HiveMetastore"
                    };

                    _sqlAzureMetastores.Add(sqlHive);

                    var sqlOozie = new SqlAzureConfig()
                    {
                        Server = GetConfigurationValue(ConfigName.SqlOozieMetastoreServer),
                        Database = GetConfigurationValue(ConfigName.SqlOozieMetastoreDatabase),
                        User = GetConfigurationValue(ConfigName.SqlOozieMetastoreUser),
                        Password = GetConfigurationValue(ConfigName.SqlOozieMetastorePassword),
                        Type = "OozieMetastore"
                    };

                    _sqlAzureMetastores.Add(sqlOozie);
                }
                return _sqlAzureMetastores;
            }
            set
            {
                _sqlAzureMetastores = value;
            }
        }

        public string HeadNodeSize
        {
            get { return GetConfigurationValue(ConfigName.HeadNodeSize); }
        }

        public string WorkerNodeSize
        {
            get { return GetConfigurationValue(ConfigName.WorkerNodeSize); }
        }

        public string ZookeeperSize
        {
            get { return GetConfigurationValue(ConfigName.ZookeeperSize); }
        }

        public string VirtualNetworkId
        {
            get { return GetConfigurationValue(ConfigName.VirtualNetworkId); }
        }

        public string SubnetName
        {
            get { return GetConfigurationValue(ConfigName.SubnetName); }
        }

        public int OperationPollIntervalInSeconds
        {
            get
            {
                int defaultValue = 30;
                int returnValue = defaultValue;
                string outValue = null;
                bool result = TryGetConfigurationValue(ConfigName.OperationPollIntervalInSeconds, out outValue);
                if (result)
                {
                    bool flag = int.TryParse(outValue, out returnValue);
                    if (!flag)
                    {
                        returnValue = defaultValue;
                    }
                }
                return returnValue;
            }
        }

        public int TimeoutPeriodInMinutes
        {
            get
            {
                int defaultValue = 60;
                int returnValue = defaultValue;
                string outValue = null;
                bool result = TryGetConfigurationValue(ConfigName.TimeoutPeriodInMinutes, out outValue);
                if (result)
                {
                    bool flag = int.TryParse(outValue, out returnValue);
                    if (!flag)
                        returnValue = defaultValue;
                }
                return returnValue;
            }
        }

        int _deleteCutoffPeriodInHours = 24;
        public int DeleteCutoffPeriodInHours
        {
            get
            {
                string outValue;
                bool result = TryGetConfigurationValue(ConfigName.DeleteCutoffPeriodInHours, out outValue);
                if (result)
                {
                    int returnValue;
                    bool flag = int.TryParse(outValue, out returnValue);
                    if (flag)
                        _deleteCutoffPeriodInHours = returnValue;
                }
                return _deleteCutoffPeriodInHours;
            }
            set
            {
                _deleteCutoffPeriodInHours = value;
            }
        }

        public bool SqlAzureAsMetastore
        {
            get
            {
                string returnValue;
                bool result = TryGetConfigurationValue(ConfigName.SqlAzureAsMetastore, out returnValue);
                if (!result)
                {
                    returnValue = "false";
                }
                return IsValueYesOrTrue(returnValue);
            }
        }

        public bool CleanupOnError
        {
            get
            {
                string returnValue;
                bool result = TryGetConfigurationValue(ConfigName.CleanupOnError, out returnValue);
                if (!result)
                {
                    returnValue = "false";
                }
                return IsValueYesOrTrue(returnValue);
            }
            set
            {
                SetConfigurationValue(ConfigName.CleanupOnError, value.ToString());
            }
        }

        public bool SilentMode
        {
            get
            {
                string returnValue;
                bool result = TryGetConfigurationValue(ConfigName.SilentMode, out returnValue);
                if (!result)
                    returnValue = "false";
                return IsValueYesOrTrue(returnValue);
            }
        }

        public string RdpUsername
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.RdpUsername, out returnValue);
                if (!result)
                {
                    returnValue = "rdp" + ClusterUsername;
                }
                return returnValue;
            }
        }

        public string RdpPassword
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.RdpPassword, out returnValue);
                if (!result)
                {
                    returnValue = ClusterPassword;
                    SaveConfigurationValue(ConfigName.RdpPassword, returnValue);
                }
                return returnValue;
            }
        }

        public string RdpExpirationInDays
        {
            get { return GetConfigurationValue(ConfigName.RdpExpirationInDays); }
        }

        public bool AutoEnableRdp
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.AutoEnableRdp, out returnValue);
                if (!result)
                {
                    returnValue = "false";
                }
                return IsValueYesOrTrue(returnValue);
            }
        }

        private bool IsValueYesOrTrue(string value)
        {
            if (String.Compare(value, "y", StringComparison.OrdinalIgnoreCase) == 0 || String.Compare(value, "yes", StringComparison.OrdinalIgnoreCase) == 0 ||
                String.Compare(value, "true", StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
            return false;
        }

        internal string GetConfigurationValue(ConfigName configName)
        {
            string returnValue = null;
            if (_overrides.ContainsKey(configName))
            {
                returnValue = _overrides[configName];
            }
            else
            {
                returnValue = ConfigurationManager.AppSettings[configName.ToString()];
            }

            if (String.IsNullOrEmpty(returnValue))
            {
                throw new ArgumentNullException(configName.ToString());
            }

            return returnValue;
        }

        internal bool TryGetConfigurationValue(ConfigName configName, out string configValue)
        {
            configValue = _overrides.ContainsKey(configName) ? _overrides[configName] : ConfigurationManager.AppSettings[configName.ToString()];

            return !String.IsNullOrEmpty(configValue);
        }

        private void SetConfigurationValue(ConfigName configName, string value)
        {
            if (_overrides.ContainsKey(configName))
            {
                _overrides[configName] = value;
            }
            else
            {
                _overrides.Add(configName, value);
            }
        }

        private void SaveConfigurationValue(ConfigName configName, string value)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            KeyValueConfigurationCollection settings = config.AppSettings.Settings;

            settings[configName.ToString()].Value = value;

            Logger.InfoFormat("Saving application configuration - Key: {0}, Value: {1}", configName.ToString(), value);
            config.Save(ConfigurationSaveMode.Modified);
            
            ConfigurationManager.RefreshSection(config.AppSettings.SectionInformation.Name);
        }

        public void PrintRunConfiguration()
        {
            if (this.SilentMode)
            {
                return;
            }

            var sb = new StringBuilder();

            sb.AppendLine(Environment.NewLine + "====================Current Run Configuration====================");
            foreach (ConfigName configPropertyName in Enum.GetValues(typeof(ConfigName)))
            {
                string configValue;
                this.TryGetConfigurationValue(configPropertyName, out configValue);
                if (!String.IsNullOrEmpty(configValue))
                {
                    sb.AppendLine(String.Format("{0} : {1}", configPropertyName, configValue));
                }
            }
            Logger.Info(sb.ToString());
        }
    }
}
