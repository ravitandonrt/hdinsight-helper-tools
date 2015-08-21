# hdinsight-helper-tools
Scripts and Tools to provide help, pointers and examples for developing with Microsoft HDInsight.

## Build status

[![Build status](https://ci.appveyor.com/api/projects/status/u59hingp65bdsayq?svg=true)](https://ci.appveyor.com/project/rtandonmsft/hdinsight-helper-tools)

## Tools

### HDInsightManagementCLI
A command line tool to manage (create, list and delete) your Microsoft Azure HDInsight clusters. 
It is developed using the latest preview SDK that supports deploying both Windows and Linux clusters using Azure Resource Manager.

Read more at [HDInsightClusterLogsDownloader](src/HDInsightManagementCLI)

**[Microsoft.Azure.Management.HDInsight NuGet](https://www.nuget.org/packages/Microsoft.Azure.Management.HDInsight) -** [![NuGet version](https://badge.fury.io/nu/Microsoft.Azure.Management.HDInsight.svg)](http://badge.fury.io/nu/Microsoft.Azure.Management.HDInsight)

### HDInsightClusterLogsDownloader
A command line tool to download the hadoop and other components service logs of your Microsoft Azure HDInsight clusters.
These logs are in a table in your storage account that you provided while creating the cluster.

Read more at [HDInsightClusterLogsDownloader](src/HDInsightClusterLogsDownloader)

## Support
The tools are provided as-is. You can choose to use it, extend it, copy portions of it.
Please log issues in this repository that you may hit, they will be addressed time-permitting.

*More tools coming soon...*
