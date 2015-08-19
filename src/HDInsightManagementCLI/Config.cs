using log4net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;

namespace HDInsightManagementCLI
{
    public enum ClusterType
    {
        Hadoop,
        HBase,
        Storm,
        Spark
    }

    public enum OperatingSystemType
    {
        Linux,
        Windows
    }

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
            AzureActiveDirectoryUri,
            AzureActiveDirectoryRedirectUri,
            AzureActiveDirectoryTenantId,
            AzureActiveDirectoryClientId,
            AzureManagementCertificateName,
            AzureManagementCertificatePassword,
            SubscriptionId,
            ResourceGroupName,
            ClusterLocation,
            ClusterDnsName,
            ClusterDnsNameSuffix,
            ClusterUserName,
            ClusterPassword,
            ClusterSize,
            HDInsightVersion,
            WasbNames,
            WasbKeys,
            WasbContainers,
            HiveQuery,
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
            BuildLabMode,
            SubscriptionIdsSafeList,
            DnsNamesSafeList,
            EnvironmentName,
            RdpUsername,
            RdpPassword,
            RdpExpirationInDays,
            AutoEnableRdp,
            ClusterType,
            OperatingSystemType
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

        public ClusterType ClusterType
        {
            get
            {
                ClusterType retval;
                string clusterTypeStr;
                if (!TryGetConfigurationValue(ConfigName.ClusterType, out clusterTypeStr))
                {
                    clusterTypeStr = "Hadoop";
                }

                if (!Enum.TryParse(clusterTypeStr, true, out retval))
                {
                    retval = ClusterType.Hadoop;
                }

                return retval;
            }
        }

        public OperatingSystemType OSType
        {
            get
            {
                OperatingSystemType retval;
                string operatingSystemType;
                if (!TryGetConfigurationValue(ConfigName.OperatingSystemType, out operatingSystemType))
                {
                    operatingSystemType = "Windows";
                }

                if (!Enum.TryParse(operatingSystemType, true, out retval))
                {
                    retval = OperatingSystemType.Windows;
                }

                return retval;
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

        public string ClusterUserName
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.ClusterUserName, out returnValue);
                if (!result)
                {
                    returnValue = "hdinsightuser";
                }
                return returnValue;
            }
        }

        public string ClusterPassword
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.ClusterPassword, out returnValue);
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

        public AzureStorageConfig DefaultWasbAccount
        {
            get
            {
                return WasbAccounts.First();
            }
        }

        private List<AzureStorageConfig> _wasbAccounts = null;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public List<AzureStorageConfig> WasbAccounts
        {
            get
            {
                if (_wasbAccounts == null)
                {
                    _wasbAccounts = new List<AzureStorageConfig>();

                    var wasbNames = GetConfigurationValue(ConfigName.WasbNames).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    var wasbKeys = GetConfigurationValue(ConfigName.WasbKeys).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                    if (wasbNames.Count() != wasbKeys.Count())
                    {
                        throw new ApplicationException(
                            String.Format("WasbNames.Count: {0} is not equal to WasbKeys.Count: {1}",
                                          wasbNames.Count(), wasbKeys.Count()));
                    }

                    var storageContainerNamesValue = GetConfigurationValue(ConfigName.WasbContainers);
                    var storageContainerNames = storageContainerNamesValue.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                    bool containerAutoName = String.Compare(storageContainerNamesValue, "auto", StringComparison.OrdinalIgnoreCase) == 0;

                    if (!containerAutoName)
                    {
                        if (wasbNames.Count() != storageContainerNames.Count())
                        {
                            throw new ApplicationException(
                                String.Format(
                                    "WasbNames.Count: {0} is not equal to WasbContainers.Count: {1}",
                                    wasbNames.Count(), storageContainerNames.Count()));
                        }
                    }

                    for (int i = 0; i < wasbNames.Count(); i++)
                    {
                        var wasbAccount = new AzureStorageConfig()
                        {
                            Name = wasbNames[i],
                            Key = wasbKeys[i]
                        };

                        wasbAccount.Container = containerAutoName ? ClusterDnsName.ToLowerInvariant() : storageContainerNames[i].ToLowerInvariant();

                        _wasbAccounts.Add(wasbAccount);
                    }
                }
                return _wasbAccounts;
            }
            set
            {
                _wasbAccounts = value;
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
                int defaultValue = 30;
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
            get { return GetConfigurationValue(ConfigName.RdpUsername); }
        }

        public string RdpPassword
        {
            get
            {
                string returnValue = null;
                bool result = TryGetConfigurationValue(ConfigName.ClusterPassword, out returnValue);
                if (result)
                {
                    return returnValue;
                }
                else
                {
                    return ClusterPassword;
                }
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

        public void PrintRunConfiguration()
        {
            if (this.SilentMode)
            {
                return;
            }

            var sb = new StringBuilder();

            sb.AppendLine(Environment.NewLine + "==================== Current Run Configuration ====================");
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
