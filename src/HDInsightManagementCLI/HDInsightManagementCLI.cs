using Hyak.Common;
using log4net;
using Microsoft.Azure;
using Microsoft.Azure.Management.HDInsight;
using Microsoft.Azure.Management.HDInsight.Models;
using Microsoft.Azure.Management.Resources;
using Microsoft.Azure.Management.Resources.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HDInsightManagementCLI
{
    public static class HDInsightManagementCLI
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(HDInsightManagementCLI));

        static Stopwatch totalStopWatch;

        static TimeSpan pollInterval;
        static TimeSpan timeout;

        static Config config = null;

        static TokenCloudCredentials tokenCloudCredentials;
        static HDInsightManagementClient hdInsightManagementClient;

        public static int Main(string[] args)
        {
            var command = string.Empty;
            try
            {
                if (args.Length == 0)
                {
                    Console.Error.WriteLine("No input args, you should provide at least one arg");
                    ShowHelp();
                    return 9999;
                }

                command = args[0];
                if ((command == "help") || (command == "/?"))
                {
                    ShowHelp();
                    return -2;
                }

                config = new Config(args);

                config.PrintRunConfiguration();

                if (!config.SilentMode)
                {
                    Logger.InfoFormat("\n========== Microsoft HDInsight Management Command Line Tool ==========\n");
                    Logger.InfoFormat("Command: {0}", command);

                    Logger.InfoFormat("TimeoutPeriodInMinutes: {0}, OperationPollIntervalInSeconds: {1}. Overridable through command line or config file\n",
                        config.TimeoutPeriodInMinutes, config.OperationPollIntervalInSeconds);
                }

                Logger.InfoFormat("Getting Azure ActiveDirectory Token from {0}", config.AzureActiveDirectoryUri);

                AuthenticationResult token = null;
                AuthenticationContext ac = new AuthenticationContext(config.AzureActiveDirectoryUri + config.AzureActiveDirectoryTenantId, true);

                var promptBehavior = PromptBehavior.Auto;

                while (token == null)
                {
                    try
                    {
                        Logger.DebugFormat("Acquring token from {0} with behavior: {1}", config.AzureActiveDirectoryUri + config.AzureActiveDirectoryTenantId, promptBehavior);
                        token = ac.AcquireToken(config.AzureManagementUri, config.AzureActiveDirectoryClientId,
                            new Uri(config.AzureActiveDirectoryRedirectUri), promptBehavior);
                    }
                    catch(Exception ex)
                    {
                        Logger.ErrorFormat("An error occurred while acquiring token from Azure Active Directory, will retry. Exception:\r\n{0}", ex.ToString());
                        promptBehavior = PromptBehavior.RefreshSession;
                    }
                }

                Logger.InfoFormat("Acquired Azure ActiveDirectory Token for User: {0} with Expiry: {1}", token.UserInfo.GivenName, token.ExpiresOn);
                tokenCloudCredentials = new TokenCloudCredentials(config.SubscriptionId, token.AccessToken);

                Logger.InfoFormat("Connecting to AzureResourceManagementUri endpoint at {0}", config.AzureResourceManagementUri);


                HttpClient azureResourceProviderHandlerHttpClient = null;
                if (!AzureResourceProviderHandler.HDInsightResourceProviderNamespace.Equals(config.AzureResourceProviderNamespace, StringComparison.OrdinalIgnoreCase))
                {
                    azureResourceProviderHandlerHttpClient = new HttpClient(new AzureResourceProviderHandler(config.AzureResourceProviderNamespace));
                    hdInsightManagementClient = new HDInsightManagementClient(tokenCloudCredentials,
                        new Uri(config.AzureResourceManagementUri), azureResourceProviderHandlerHttpClient);
                }
                else
                {
                    hdInsightManagementClient = new HDInsightManagementClient(tokenCloudCredentials, new Uri(config.AzureResourceManagementUri));
                }

                pollInterval = TimeSpan.FromSeconds(config.OperationPollIntervalInSeconds);
                timeout = TimeSpan.FromMinutes(config.TimeoutPeriodInMinutes);

                totalStopWatch = new Stopwatch();
                totalStopWatch.Start();

                switch (command)
                {
                    case "l":
                    case "list":
                        {
                            Logger.InfoFormat("SubscriptionId: {0} - Getting cluster information", config.SubscriptionId);
                            var clusters = hdInsightManagementClient.Clusters.List();
                            int i = 1;
                            foreach (var cluster in clusters)
                            {
                                Logger.InfoFormat("Cluster {0}: {1}", i++, cluster.Name);
                                Logger.InfoFormat(ClusterToString(cluster));
                            }
                            break;
                        }
                    case "ls":
                    case "listspecific":
                        {
                            Logger.InfoFormat("Cluster: {0} - Getting details", config.ClusterDnsName);
                            var cluster = hdInsightManagementClient.Clusters.Get(config.ResourceGroupName, config.ClusterDnsName).Cluster;
                            Logger.InfoFormat(ClusterToString(cluster));

                            Logger.InfoFormat("Cluster Configurations:\r\n{0}", GetClusterConfigurations());
                            break;
                        }
                    case "lsrc":
                    case "listspecificresumecreate":
                    case "lsrd":
                    case "listspecificresumedelete":
                        {
                            if (command.Contains("lsrc") || command.Contains("create"))
                            {
                                MonitorCreate(config.ResourceGroupName, config.ClusterDnsName);
                            }
                            else if (command.Contains("lsrd") || command.Contains("delete"))
                            {
                                MonitorDelete(config.ResourceGroupName, config.ClusterDnsName);
                            }
                            break;
                        }
                    case "c":
                    case "create":
                        {
                            foreach (var asv in config.AdditionalStorageAccounts)
                            {
                                if (!asv.Name.EndsWith(".net", StringComparison.OrdinalIgnoreCase))
                                    Logger.InfoFormat("WARNING - ASV AccountName: {0} does not seem to have a valid FQDN. " +
                                        "Please ensure that you use the full blob endpoint url else your cluster creation will fail.",
                                        asv.Name);
                            }

                            CreateResourceGroup(config.AzureResourceManagementUri, 
                                config.AzureResourceProviderNamespace, 
                                config.ResourceGroupName, config.ClusterLocation);

                            Create(config.SubscriptionId, config.ResourceGroupName, config.ClusterDnsName, config.ClusterLocation, 
                                config.DefaultStorageAccount, config.ClusterSize, 
                                config.ClusterUsername, config.ClusterPassword, config.HDInsightVersion,
                                config.ClusterType, config.OSType,
                                config.AdditionalStorageAccounts, config.SqlAzureMetastores,
                                config.VirtualNetworkId, config.SubnetName,
                                config.HeadNodeSize, config.WorkerNodeSize, config.ZookeeperSize);

                            break;
                        }
                    case "rs":
                    case "resize":
                        {
                            Resize(config.ResourceGroupName, config.ClusterDnsName, config.ClusterSize);
                            break;
                        }
                    case "rdpon":
                        {
                            EnableRdp(config.ClusterDnsName, config.ClusterLocation,
                                config.RdpUsername, config.RdpPassword,
                                DateTime.Now.AddDays(int.Parse(config.RdpExpirationInDays)));
                            break;
                        }
                    case "rdpoff":
                        {
                            DisableRdp(config.ClusterDnsName, config.ClusterLocation);
                            break;
                        }
                    case "d":
                    case "delete":
                        {
                            Delete(config.ResourceGroupName, config.ClusterDnsName);
                            break;
                        }
                    case "derr":
                    case "deleteerror":
                        {
                            Logger.InfoFormat("SubId: {0} - Deleting all clusters in error or unknown state", config.SubscriptionId);
                            var clustersResponse = hdInsightManagementClient.Clusters.List();
                            var errorClustersList = new List<Cluster>();
                            foreach (var cluster in clustersResponse.Clusters)
                            {
                                Logger.InfoFormat("Found: {0}, State: {1}, CreatedDate: {2}",
                                    cluster.Name, cluster.Properties.ClusterState, cluster.Properties.CreatedDate);
                                if (cluster.Properties.ProvisioningState == HDInsightClusterProvisioningState.Failed ||
                                    cluster.Properties.ProvisioningState == HDInsightClusterProvisioningState.Canceled ||
                                    String.Compare(cluster.Properties.ClusterState, "Error", StringComparison.OrdinalIgnoreCase) == 0 ||
                                    String.Compare(cluster.Properties.ClusterState, "Unknown", StringComparison.OrdinalIgnoreCase) == 0)
                                {
                                    errorClustersList.Add(cluster);
                                }
                            }

                            Logger.InfoFormat("Clusters found: {0}, Clusters in Error/Unknown state: {1}", clustersResponse.Clusters.Count, errorClustersList.Count);

                            var deleteCount = 0;
                            foreach (var errorCluster in errorClustersList)
                            {
                                Delete(config.ResourceGroupName, errorCluster.Name);
                                deleteCount++;
                            }

                            Logger.InfoFormat("Clusters deleted: {0}", deleteCount);

                            break;
                        }
                    case "dstale":
                    case "deletestale":
                    case "dall":
                    case "deleteall":
                    case "dallprefix":
                    case "deleteallprefix":
                        {
                            if (!ConfirmOperation(String.Format("SubId: {0} - Are you sure you want delete all the clusters (Clusters that were created more than {1} hours ago)? This operation cannot be undone.",
                                config.SubscriptionId, config.DeleteCutoffPeriodInHours)))
                            {
                                break;
                            }

                            var clustersResponse = hdInsightManagementClient.Clusters.List();
                            var deleteClustersList = new List<Cluster>();

                            var currTime = DateTime.UtcNow;
                            var cutoffTime = DateTime.UtcNow.AddHours(-config.DeleteCutoffPeriodInHours);
                            Logger.InfoFormat(
                                    Environment.NewLine + String.Format("Current UTC time: {0}, Cut-off time: {1}", currTime.ToString(), cutoffTime.ToString()) +
                                    Environment.NewLine + String.Format("Total Clusters: {0}", clustersResponse.Clusters.Count) +
                                    Environment.NewLine + "Searching for Clusters...");

                            foreach (var cluster in clustersResponse)
                            {
                                if (cluster.Properties.CreatedDate < cutoffTime)
                                {
                                    Logger.InfoFormat("Name: {0}, State: {1}, CreatedDate: {2}",
                                        cluster.Name, cluster.Properties.ProvisioningState, cluster.Properties.CreatedDate);
                                    deleteClustersList.Add(cluster);
                                }
                            }

                            Logger.InfoFormat("Clusters to be deleted: {0}", deleteClustersList.Count);

                            var deleteCount = 0;
                            if (deleteClustersList.Count == 0 || !ConfirmOperation())
                            {
                                break;
                            }
                            else
                            {
                                foreach (var deleteCluster in deleteClustersList)
                                {
                                    try
                                    {
                                        Delete(config.ResourceGroupName, deleteCluster.Name);
                                        deleteCount++;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.InfoFormat("SubId: {0} - Cluster: {1} - Error encountered during Delete:\r\n{2}",
                                            config.SubscriptionId, deleteCluster.Name, ex.ToString());
                                    }
                                }
                            }

                            Logger.InfoFormat("Clusters deleted: {0}", deleteCount);
                            break;
                        }
                    case "gsl":
                    case "getsupportedlocations":
                        {
                            GetSupportedLocations(config.ClusterLocation);
                            break;
                        }
                    case "gc":
                    case "getcapabilties":
                        {
                            GetCapabilities(config.SubscriptionId);
                            break;
                        }
                    default:
                        {
                            Logger.InfoFormat(string.Format("Command '{0}' is incorrect.", command));
                            ShowHelp();
                            throw new Exception();
                        }
                }
                Logger.InfoFormat("Total time taken for this command ({0}): {1:0.00} secs", command, totalStopWatch.Elapsed.TotalSeconds);
                totalStopWatch.Stop();
            }
            catch (Exception ex)
            {
                Logger.Error(String.Format("Command '{0}' failed. Error Information:", command ?? "null") , ex);
                return -1;
            }
            finally
            {
                if (Debugger.IsAttached && !config.SilentMode)
                {
                    Logger.Info("Press any key to exit...");
                    Console.ReadKey();
                }
            }

            return 0;
        }

        private static string GetClusterConfigurations(List<string> clusterConfigurationNames = null)
        {
            var sb = new StringBuilder();
            if (clusterConfigurationNames == null || clusterConfigurationNames.Count == 0)
            {
                clusterConfigurationNames = new List<string>() { "core-site" };
                //"hdfs-site", "hive-site", "mapred-site", "oozie-site", "yarn-site", "hbase-site", "storm-site"
            }

            foreach (var clusterConfigurationName in clusterConfigurationNames)
            {
                sb.AppendFormat(GetClusterConfiguration(clusterConfigurationName));
            }
            return sb.ToString();
        }

        private static string GetClusterConfiguration(string clusterConfigurationName)
        {
            var sb = new StringBuilder();
            var clusterConfigurations = hdInsightManagementClient.Clusters.GetClusterConfigurations(
                config.ResourceGroupName, config.ClusterDnsName, clusterConfigurationName);

            if (clusterConfigurations.Configuration.Count > 0)
            {
                sb.AppendFormat("\t{0}:\r\n\t\t{1}\r\n",
                    clusterConfigurationName,
                    String.Join(Environment.NewLine + "\t\t", clusterConfigurations.Configuration.Select(
                    c => c.Key + ": " + c.Value)));
            }
            return sb.ToString();
        }

        private static string ClusterToString(Cluster cluster)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("\r\n\tResourceGroup: {0}\r\n", HDInsightManagementCLIHelpers.GetResourceGroupNameFromClusterId(cluster.Id));
            sb.AppendFormat("\t{0}\r\n", cluster.ToDisplayString().Replace("\n", "\n\t"));
            sb.AppendFormat("\tUserClusterTablePrefix: {0}\r\n", HDInsightManagementCLIHelpers.GetUserClusterTablePrefix(cluster));
            return sb.ToString();
        }

        private static void Resize(string resourceGroup, string ClusterDnsName, int newSize)
        {
            Logger.InfoFormat("Cluster: {0} - Getting cluster information", ClusterDnsName);
            var clusterGetResponse = hdInsightManagementClient.Clusters.Get(resourceGroup, ClusterDnsName);
            Logger.InfoFormat(clusterGetResponse.ToDisplayString());
            Logger.InfoFormat("Resizing - Cluster: {0}, Location: {1}, NewSize: {2}", ClusterDnsName, resourceGroup, newSize);
            var resizeResponse = hdInsightManagementClient.Clusters.Resize(resourceGroup, ClusterDnsName, newSize);
            Logger.InfoFormat("Resizing complete - Getting Updated Cluster Information:");
            clusterGetResponse = hdInsightManagementClient.Clusters.Get(resourceGroup, ClusterDnsName);
            Logger.InfoFormat(clusterGetResponse.ToDisplayString());
        }

        private static void EnableRdp(string resourceGroup, string ClusterDnsName, string rdpUserName, string rdpPassword, DateTime rdpExpiry)
        {
            Logger.InfoFormat("Enabling Rdp - ResourceGroup: {0}, Cluster: {1}", resourceGroup, ClusterDnsName);
            hdInsightManagementClient.Clusters.EnableRdp(resourceGroup, ClusterDnsName, rdpUserName, rdpPassword, rdpExpiry);
            HDInsightManagementCLIHelpers.CreateRdpFile(ClusterDnsName, rdpUserName, rdpPassword);
        }

        private static void DisableRdp(string resourceGroup, string ClusterDnsName)
        {
            Logger.InfoFormat("Disabling Rdp - ResourceGroup: {0}, Cluster: {1}", resourceGroup, ClusterDnsName);
            var response = hdInsightManagementClient.Clusters.DisableRdp(resourceGroup, ClusterDnsName);
        }

        static bool ConfirmOperation(string message = null)
        {
            if (config.SilentMode)
                return true;

            if (message == null)
                message = String.Format("Do you wish to continue the current operaton?");
            Console.Write(message + " (yes/no): ");
            var input = Console.ReadLine();
            if (string.Compare(input, "yes", StringComparison.OrdinalIgnoreCase) == 0 || string.Compare(input, "y", StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
            else
            {
                Logger.InfoFormat("Operation aborted");
                return false;
            }
        }

        static void GetSupportedLocations(string location)
        {
            Logger.InfoFormat("SubscriptionId: {0} - Getting Supported Locations", hdInsightManagementClient.Credentials.SubscriptionId);

            var capabilities = hdInsightManagementClient.Clusters.GetCapabilities(location);

            Logger.InfoFormat("Regions:\r\n{0}",
                String.Join(Environment.NewLine, capabilities.Regions.Select(c => c.Key + ":" + Environment.NewLine + "\t" +
                    String.Join(Environment.NewLine + "\t", c.Value.AvailableRegions))));
        }

        static void GetCapabilities(string location)
        {
            Logger.InfoFormat("SubscriptionId: {0} - Getting Subscription Properties", hdInsightManagementClient.Credentials.SubscriptionId);

            var capabilities = hdInsightManagementClient.Clusters.GetCapabilities(location);

            Logger.InfoFormat("Features:\r\n\t{0}",
                String.Join(Environment.NewLine + "\t", capabilities.Features));

            Logger.InfoFormat("QuotaCapability:\r\n\t{0}",
                String.Join(Environment.NewLine + "\t", capabilities.QuotaCapability.RegionalQuotas.Select(q => q.ToDisplayString(false))));

            Logger.InfoFormat("Regions:\r\n\t{0}",
                String.Join(Environment.NewLine + "\t", capabilities.Regions.Select(c => c.Key + ":" + Environment.NewLine + "\t\t" +
                    String.Join(Environment.NewLine + "\t\t", c.Value.AvailableRegions))));

            Logger.InfoFormat("Versions:\r\n\t{0}",
                String.Join(Environment.NewLine + "\t", capabilities.Versions.Select(c => c.Key + ":" + Environment.NewLine + "\t\t" +
                    String.Join(Environment.NewLine + "\t\t",
                        String.Join(Environment.NewLine + "\t\t", c.Value.AvailableVersions.Select(v => v.DisplayName))))));

            Logger.InfoFormat("VmSizes:\r\n\t{0}",
                String.Join(Environment.NewLine + "\t", capabilities.VmSizes.Select(c => c.Key + ":" + Environment.NewLine + "\t\t" +
                    String.Join(Environment.NewLine + "\t\t", c.Value.AvailableVmSizes))));
        }

        static void CreateResourceGroup(string azureResourceManagementUri, string azureResourceProviderNamespace, string resourceGroupName, string location)
        {
            Logger.InfoFormat("Creating Resource Group and registering provider - " + 
                "ARM Uri: {0}, ResourceProvider: {1}, ResourceGroupName: {2}, Location: {3}",
                azureResourceManagementUri, azureResourceProviderNamespace, resourceGroupName, location);

            var resourceClient = new ResourceManagementClient(tokenCloudCredentials, new Uri(azureResourceManagementUri));
            resourceClient.ResourceGroups.CreateOrUpdate(
                resourceGroupName,
                new ResourceGroup
                {
                    Location = location
                });

            resourceClient.Providers.Register(azureResourceProviderNamespace);
        }

        static void Create(string subscriptionId, string resourceGroupName, string clusterDnsName, string clusterLocation,
            AzureStorageConfig defaultStorageAccount, int clusterSize, string clusterUsername, string clusterPassword,
            string hdInsightVersion,
            string clusterType, string osType,
            List<AzureStorageConfig> additionalStorageAccounts,
            List<SqlAzureConfig> sqlAzureMetaStores,
            string virtualNetworkId = null, string subnetName = null,
            string headNodeSize = null, string workerNodeSize = null, string zookeeperSize = null)
        {
            Logger.InfoFormat("ResourceGroup: {0}, Cluster: {1} - Submitting a new cluster deployment request", resourceGroupName, clusterDnsName);

            var clusterCreateParameters = new ClusterCreateParameters()
                {
                    ClusterSizeInNodes = clusterSize,
                    ClusterType = clusterType,
                    DefaultStorageAccountName = defaultStorageAccount.Name,
                    DefaultStorageAccountKey = defaultStorageAccount.Key,
                    DefaultStorageContainer = defaultStorageAccount.Container,
                    Location = clusterLocation,
                    Password = clusterPassword,
                    UserName = clusterUsername,
                    Version = hdInsightVersion,
                    OSType = (OSType)Enum.Parse(typeof(OSType), osType.ToString()),
                    HeadNodeSize = headNodeSize,
                    WorkerNodeSize = workerNodeSize,
                    ZookeeperNodeSize = zookeeperSize
                };

            if (clusterCreateParameters.OSType == OSType.Linux)
            {
                clusterCreateParameters.SshUserName = config.SshUsername;
                if(!String.IsNullOrWhiteSpace(config.SshPassword))
                {
                    clusterCreateParameters.SshPassword = config.SshPassword;
                }
                else if(File.Exists(config.SshPublicKeyFilePath))
                {
                    var publicKey = File.ReadAllText(config.SshPublicKeyFilePath);
                    Logger.Debug("SSH RSA Public Key: " + Environment.NewLine + publicKey + Environment.NewLine);
                    clusterCreateParameters.SshPublicKey = publicKey;
                }
                else if (String.IsNullOrWhiteSpace(config.SshPassword))
                {
                    clusterCreateParameters.SshPassword = config.ClusterPassword;
                }
            }
            else
            {
                if (config.AutoEnableRdp)
                {
                    clusterCreateParameters.RdpUsername = config.RdpUsername;
                    clusterCreateParameters.RdpPassword = config.RdpPassword;
                    clusterCreateParameters.RdpAccessExpiry = DateTime.Now.AddDays(int.Parse(config.RdpExpirationInDays));
                }
            }

            foreach (var additionalStorageAccount in additionalStorageAccounts)
            {
                clusterCreateParameters.AdditionalStorageAccounts.Add(additionalStorageAccount.Name, additionalStorageAccount.Key);
            }


            if (!String.IsNullOrEmpty(virtualNetworkId) && !String.IsNullOrEmpty(subnetName))
            {
                clusterCreateParameters.VirtualNetworkId = virtualNetworkId;
                clusterCreateParameters.SubnetName = subnetName;
            }

            if (sqlAzureMetaStores != null && sqlAzureMetaStores.Count > 0)
            {
                var hiveMetastore = sqlAzureMetaStores.FirstOrDefault(s => s.Type.Equals("HiveMetastore"));
                if (hiveMetastore != null)
                {
                    clusterCreateParameters.HiveMetastore =
                        new Metastore(hiveMetastore.Server, hiveMetastore.Database, hiveMetastore.User, hiveMetastore.Password);
                }

                var oozieMetastore = sqlAzureMetaStores.FirstOrDefault(s => s.Type.Equals("OozieMetastore"));
                if (oozieMetastore != null)
                {
                    clusterCreateParameters.OozieMetastore =
                        new Metastore(oozieMetastore.Server, oozieMetastore.Database, oozieMetastore.User, oozieMetastore.Password);
                }
            }

            var localStopWatch = Stopwatch.StartNew();

            var createTask = hdInsightManagementClient.Clusters.CreateAsync(resourceGroupName, clusterDnsName, clusterCreateParameters);
            Logger.InfoFormat("Cluster: {0} - Create cluster request submitted with task id: {1}, task status: {2}",
                clusterDnsName, createTask.Id, createTask.Status);

            Thread.Sleep(pollInterval);

            var error = MonitorCreate(resourceGroupName, clusterDnsName, createTask);

            if (error)
            {
                if (config.CleanupOnError)
                {
                    Logger.InfoFormat("{0} - {1}. Submitting a delete request for the failed cluster creation.", Config.ConfigName.CleanupOnError.ToString(), config.CleanupOnError.ToString());
                    Delete(resourceGroupName, clusterDnsName);
                }
                else
                {
                    throw new ApplicationException(String.Format("Cluster: {0} - Creation unsuccessful", clusterDnsName));
                }
            }
            else
            {
                if (config.AutoEnableRdp && clusterCreateParameters.OSType == OSType.Windows)
                {
                    HDInsightManagementCLIHelpers.CreateRdpFile(clusterDnsName, config.RdpUsername, config.RdpPassword);
                }
            }
        }

        static bool MonitorCreate(string resourceGroupName, string ClusterDnsName, Task<ClusterGetResponse> createTask = null)
        {
            bool error = false;
            Cluster cluster = null;
            var localStopWatch = Stopwatch.StartNew();
            do
            {
                if (createTask != null)
                {
                    Logger.InfoFormat("Cluster: {0} - Create Task Id: {1}, Task Status: {2}, Time: {3:0.00} secs",
                        ClusterDnsName, createTask.Id, createTask.Status, localStopWatch.Elapsed.TotalSeconds);

                    if (createTask.IsFaulted || createTask.IsCanceled)
                    {
                        error = true;
                        break;
                    }
                }

                try
                {
                    cluster = hdInsightManagementClient.Clusters.Get(resourceGroupName, ClusterDnsName).Cluster;
                }
                catch(CloudException ex)
                {
                    if(!ex.Error.Code.Equals("ResourceNotFound", StringComparison.OrdinalIgnoreCase))
                    {
                        throw;
                    }
                }

                if (cluster != null)
                {
                    Logger.InfoFormat("Cluster: {0} - State: {1}, Time: {2:0.00} secs, CRUD Time: {3:0.00} secs",
                    cluster.Name, cluster.Properties.ClusterState,
                    localStopWatch.Elapsed.TotalSeconds,
                    (DateTime.UtcNow - cluster.Properties.CreatedDate).TotalSeconds);

                    if (cluster.Properties.ProvisioningState == HDInsightClusterProvisioningState.Failed ||
                        cluster.Properties.ProvisioningState == HDInsightClusterProvisioningState.Canceled ||
                        String.Compare(cluster.Properties.ClusterState, "Error", StringComparison.OrdinalIgnoreCase) == 0 ||
                        String.Compare(cluster.Properties.ClusterState, "Unknown", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        error = true;
                        break;
                    }

                    if (cluster.Properties.ProvisioningState == HDInsightClusterProvisioningState.Succeeded ||
                        String.Compare(cluster.Properties.ClusterState, "Running", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        error = false;
                        break;
                    }
                }
                else
                {
                    Logger.InfoFormat("Cluster: {0} - State: {1}, Time: {2:0.00} secs",
                    ClusterDnsName, "ResourceNotFound", localStopWatch.Elapsed.TotalSeconds);
                }

                if (timeout.TotalMinutes > 0)
                {
                    var elapsedTime = localStopWatch.Elapsed;
                    if (elapsedTime > timeout)
                    {
                        error = true;
                        Logger.InfoFormat("Cluster: {0} - Operation timed out. Timeout: {1:0.00} mins, Elapsed Time: {2:0.00} mins. Cluster will be deleted if /CleanupOnError was set",
                            ClusterDnsName, timeout.TotalMinutes, elapsedTime.TotalMinutes);
                        break;
                    }
                }
                else
                {
                    error = false;
                    break;
                }

                Thread.Sleep(pollInterval);
            }
            while (!error);

            if (createTask != null)
            {
                Logger.InfoFormat("Task Result:\r\n{0}", createTask.Result.ToDisplayString());
            }

            if (cluster != null)
            {
                Logger.InfoFormat("Cluster details:" + Environment.NewLine + ClusterToString(cluster));
            }

            if(!error)
            {
                Logger.InfoFormat("Cluster: {0} - Created successfully and is ready to use", cluster.Name);
                Logger.InfoFormat("Cluster ConnectivityEndpoints:\r\n{0}",
                    String.Join(Environment.NewLine, cluster.Properties.ConnectivityEndpoints.Select(c => c.ToDisplayString(false))));
            }
            return error;
        }

        static void Delete(string resourceGroupName, string clusterDnsName)
        {
            var localStopWatch = Stopwatch.StartNew();
            var deleteTask = hdInsightManagementClient.Clusters.DeleteAsync(resourceGroupName, clusterDnsName);
            Logger.InfoFormat("Cluster: {0} - Cluster delete request submitted with task id: {1}, status: {2}",
                clusterDnsName, deleteTask.Id, deleteTask.Status);

            bool error = MonitorDelete(resourceGroupName, clusterDnsName);

            if (error)
            {
                throw new ApplicationException(String.Format("Cluster: {0} - Delete unsucessful", clusterDnsName));
            }
        }

        static bool MonitorDelete(string resourceGroupName, string ClusterDnsName)
        {
            Cluster cluster = null;
            var localStopWatch = new Stopwatch();
            localStopWatch.Start();
            bool error = false;
            try
            {
                do
                {
                    cluster = hdInsightManagementClient.Clusters.Get(resourceGroupName, ClusterDnsName).Cluster;

                    if (cluster != null)
                    {
                        Logger.InfoFormat("Cluster: {0} - State: {1}, Time: {2:0.00} secs",
                            cluster.Name, cluster.Properties.ClusterState, localStopWatch.Elapsed.TotalSeconds);

                        if (timeout.TotalMinutes > 0)
                        {
                            var elapsedTime = localStopWatch.Elapsed;
                            if (elapsedTime > timeout)
                            {
                                error = true;
                                Logger.InfoFormat("Cluster: {0} - Operation timed out. Timeout: {1:0.00} mins, Elapsed Time: {2:0.00} mins." + Environment.NewLine, ClusterDnsName, timeout.TotalMinutes, elapsedTime.TotalMinutes);
                                break;
                            }
                        }
                    }
                    else
                    {
                        error = false;
                        break;
                    }
                    Thread.Sleep(pollInterval);
                }
                while (!error);
            }
            catch (Exception ex)
            {
                Logger.InfoFormat(ex.ToString());
                if (ex is CloudException && ((CloudException)ex).Error.Code.Equals("ResourceNotFound", StringComparison.OrdinalIgnoreCase))
                {
                    Logger.InfoFormat("Cluster: {0} - Not found. Delete Successful.", ClusterDnsName);
                }
                else
                {
                    Logger.InfoFormat("Cluster: {0} - Delete Unsuccessful.", ClusterDnsName);
                }
            }

            if (cluster != null)
            {
                Logger.InfoFormat("Cluster details:" + Environment.NewLine + ClusterToString(cluster));
            }

            return error;
        }

        private static void ShowHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Environment.NewLine + "-".PadRight(50, '-'));
            sb.AppendLine("Microsoft HDInsight Management Tool Help");
            sb.AppendLine("-".PadRight(50, '-'));
            sb.AppendLine("You must provide one of the following as command line arg:");
            sb.AppendLine("c - creates a cluster");
            sb.AppendLine("d - deletes the cluster");
            sb.AppendLine("l - list all the clusters for the specified subscription");
            sb.AppendLine("ls - list a specific cluster's details for the specified subscription and ClusterDnsName");
            sb.AppendLine("lsrc - resume cluster creation monitoring (helpful to resume if you get a timeout or lost track of the create)");
            sb.AppendLine("lsrd - resume cluster deletion monitoring (helpful to resume if you get a timeout or lost track of the delete)");
            sb.AppendLine("gsl - get supported locations for a subcription");
            sb.AppendLine("gc - gets subcription capabilities like supported regions, versions, os types etc");
            sb.AppendLine(String.Format("dall - delete all the clusters based off on cutoff time. " + 
                "Cutoff time is overridable using {0}", Config.ConfigName.DeleteCutoffPeriodInHours.ToString()));
            sb.AppendLine(String.Format("rdpon, rdponrdfe - enable rdp for a cluster. " + 
                "RdpUsername, RdpPassword, RdpExpirationInDays are specified using {0}, {1}, {2}", 
                Config.ConfigName.RdpUsername, Config.ConfigName.RdpPassword, Config.ConfigName.RdpExpirationInDays));
            sb.AppendLine("Configuration Overrides - Command Line arguments for configuration take precedence over App.config");
            sb.AppendLine("Specifying a configuration override in commandline: /{ConfigurationName}:{ConfigurationValue}");
            sb.AppendLine("Optional/Additional parameters that override the configuration values can be specified in the App.config");
            sb.AppendLine("Overridable Configuration Names:");
            sb.AppendLine("\t" + string.Join(Environment.NewLine + "\t", Enum.GetNames(typeof(Config.ConfigName))));
            sb.AppendLine("-".PadRight(50, '-'));
            sb.AppendLine("Examples:");
            sb.AppendLine("HDInsightManagementCLI.exe c - Creates a cluster using the name specified in app.config or as command line overrides");
            sb.AppendLine("HDInsightManagementCLI.exe c /CleanupOnError:yes - Creates a cluster and cleans up if an error was encountered");
            sb.AppendLine("HDInsightManagementCLI.exe d - Deletes the cluster.");
            sb.AppendLine("HDInsightManagementCLI.exe l /SubscriptionId:<your-sub-id> - Gets the clusters for the specified subscription id");
            sb.AppendLine("HDInsightManagementCLI.exe gsl /SubscriptionId:<your-sub-id> - Gets the list of supported locations for the specified subscription id");
            sb.AppendLine("HDInsightManagementCLI.exe gc /SubscriptionId:<your-sub-id> - Gets the subscription capabilities");
            sb.AppendLine("HDInsightManagementCLI.exe rdpon - Enables RDP for cluster (windows only)");
            Logger.Info(sb.ToString());
        }
    }
}