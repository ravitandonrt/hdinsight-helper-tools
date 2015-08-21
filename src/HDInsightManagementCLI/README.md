# HDInsightManagementCLI
This is an **unofficial** tool created for managing (create, list and delete) your Microsoft Azure HDInsight clusters.
It is developed using the latest preview SDK that supports deploying both Windows and Linux clusters using Azure Resource Manager (ARM).

**[Microsoft.Azure.Management.HDInsight NuGet](https://www.nuget.org/packages/Microsoft.Azure.Management.HDInsight) -** [![NuGet version](https://badge.fury.io/nu/Microsoft.Azure.Management.HDInsight.svg)](http://badge.fury.io/nu/Microsoft.Azure.Management.HDInsight)

## Key points
* HDInsight is moving to Azure Resource Manager.
  * The **Windows** clusters are still PaaS and are deployed RDFE way
  * The **Linux** clusters are in IaaS model and are deployed via Azure Resource Manager.
* You will not be able to deploy **Storm** and **HBase** cluster types for **Linux** OS flavor from the old azure portal (RDFE based).
  * You will need to use the new preview portal to create any new cluster types in Linux.
  * Windows based clusters can be created using the new portal, old portal, new SDK and old SDK.
* Certificate based credentials will no longer work if you are using the new SDK (ARM), you need to use Active Directory Token based credentials.
  ```csharp
    Logger.InfoFormat("Getting Azure ActiveDirectory Token from {0}", config.AzureActiveDirectoryUri);
    AuthenticationContext ac = new AuthenticationContext(config.AzureActiveDirectoryUri + config.AzureActiveDirectoryTenantId, true);
    var token = ac.AcquireToken(config.AzureManagementUri, config.AzureActiveDirectoryClientId,
        new Uri(config.AzureActiveDirectoryRedirectUri), PromptBehavior.Auto);

    Logger.InfoFormat("Acquired Azure ActiveDirectory Token for User: {0} with Expiry: {1}", token.UserInfo.GivenName, token.ExpiresOn);
    tokenCloudCredentials = new TokenCloudCredentials(config.SubscriptionId, token.AccessToken);

    Logger.InfoFormat("Connecting to AzureResourceManagementUri endpoint at {0}", config.AzureResourceManagementUri);
    hdInsightManagementClient = new HDInsightManagementClient(tokenCloudCredentials, new Uri(config.AzureResourceManagementUri));
  ```
  Configurations:
  ```
    AzureManagementUri : https://management.core.windows.net/
    AzureResourceManagementUri : https://management.azure.com/
    AzureActiveDirectoryUri : https://login.windows.net/
    AzureActiveDirectoryRedirectUri : urn:ietf:wg:oauth:2.0:oob
    AzureActiveDirectoryTenantId : YOUR-TENANT-ID
    AzureActiveDirectoryClientId : 1950a258-227b-4e31-a9cf-717495945fc2
  ```
* You need to call GetCapabilities to list any subscription properties. (You may pass any Azure location to get capabilities)

## Change Impact
The new [HDInsight ARM SDK](https://www.nuget.org/packages/Microsoft.Azure.Management.HDInsight) is completely different than the [RDFE HDInsight SDK](Microsoft.WindowsAzure.Management.HDInsight). 
The new SDK does not break existing scripts or clients as it is a separate NuGet package.
You will not automatically get this SDK if you update your current HDInsight SDKs. Instead, you will have to explicitly choose the NuGet packages for the new SDK.

There are two parts of this SDK: cluster CRUD (management) and job submission (data). The new changes do not impact job submission to templeton, hence the data SDK remains fairly same.

One can use the SDK by installing the appropriate NuGet package for the SDK:
* RDFE data NuGet package: Microsoft.Hadoop.Client
* ARM data NuGet package: Microsoft.Azure.Management.HDInsight.Job 
* RDFE management NuGet package: Microsoft.WindowsAzure.Management.HDInsight 
* ARM management NuGet package: Microsoft.Azure.Management.HDInsight 

With the move to ARM, HDInsight has begun to use Hyak. 
Hyak is a code generator written by the Azure SDK team that provides the ability to generate client SDK code for REST API endpoints in multiple programming languages.

The following operations are no longer be supported in the SDK:
* ListAvailableLocations 
* ListAvailableVersions 

This information is available via the GetCapabilities operation. 

## Usage
```
--------------------------------------------------
Microsoft HdInsight Management Tool Help
--------------------------------------------------
You must provide one of the following as command line arg:
c - creates a cluster
d - deletes the cluster
l - list all the clusters for the specified subscription
ls - list a specific cluster's details for the specified subscription and ClusterDnsName
lsrc - resume cluster creation monitoring (helpful to resume if you get a timeout or lost track of the create)
lsrd - resume cluster deletion monitoring (helpful to resume if you get a timeout or lost track of the delete)
gsl - get supported locations for a subcription
gc - gets subcription capabilities like supported regions, versions, os types etc
dall - delete all the clusters based off on cutoff time. Cutoff time is overridable using DeleteCutoffPeriodInHours
rdpon, rdponrdfe - enable rdp for a cluster. RdpUsername, RdpPassword, RdpExpirationInDays are specified using RdpUsername, RdpPassword, RdpExpirationInDays
Configuration Overrides - Command Line arguments for configuration take precedence over App.config
Specifying a configuration override in command line: /{ConfigurationName}:{ConfigurationValue}
Optional/Additional parameters that override the configuration values can be specified in the App.config
Overridable Configuration Names:
        AzureManagementUri
        AzureResourceManagementUri
        AzureActiveDirectoryUri
        AzureActiveDirectoryRedirectUri
        AzureActiveDirectoryTenantId
        AzureActiveDirectoryClientId
        AzureManagementCertificateName
        AzureManagementCertificatePassword
        SubscriptionId
        ResourceGroupName
        ClusterLocation
        ClusterDnsName
        ClusterSize
        ClusterUsername
        ClusterPassword
        SshUsername
        SshPassword
        SshPublicKeyFilePath
        HDInsightVersion
        WasbNames
        WasbKeys
        WasbContainers
        SqlAzureAsMetastore
        SqlHiveMetastoreServer
        SqlHiveMetastoreDatabase
        SqlHiveMetastoreUser
        SqlHiveMetastorePassword
        SqlOozieMetastoreDatabase
        SqlOozieMetastoreServer
        SqlOozieMetastoreUser
        SqlOozieMetastorePassword
        OperationPollIntervalInSeconds
        TimeoutPeriodInMinutes
        DeleteCutoffPeriodInHours
        CleanupOnError
        SilentMode
        BuildLabMode
        AutoEnableRdp
        RdpUsername
        RdpPassword
        RdpExpirationInDays
        ClusterType
        OperatingSystemType
--------------------------------------------------
Examples:
HDInsightManagementCLI.exe c - Creates a cluster using the name specified in app.config or as command line overrides
HDInsightManagementCLI.exe c /CleanupOnError:yes - Creates a cluster and cleans up if an error was encountered
HDInsightManagementCLI.exe d - Deletes the cluster.
HDInsightManagementCLI.exe l /SubscriptionId:<your-sub-id> - Gets the clusters for the specified subscription id
HDInsightManagementCLI.exe gsl /SubscriptionId:<your-sub-id> - Gets the list of supported locations for the specified subscription id
HDInsightManagementCLI.exe gc /SubscriptionId:<your-sub-id> - Gets the subscription capabilities
HDInsightManagementCLI.exe rdpon - Enables RDP for cluster (windows only)
```

## TODO (How you can help)
* Switch to a command line parser NuGet and customize that to use the App.config
* Add more commands support based on APIs available
* Add job submission support
* Update CLI when password retrieval arrives
* Upload the RDFE based CLI as well
