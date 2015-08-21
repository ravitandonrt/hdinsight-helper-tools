# HDInsightClusterLogsDownloader
A command line tool to download the hadoop and other components service logs of your Microsoft Azure HDInsight clusters.
You need to first use the [HDInsightManagementCLI](../HDInsightManagementCLI) to get the UserClusterTablePrefix for your cluster.

Add your storage account credentials into the App.config and provide the table name (e.g. ustormiaasprod219aug2015at204409001hadoopservicelog) or the table name prefix (e.g. ustormiaasprod219aug2015at20)

Once the tools runs it will create an excel workbook with all the logs and also pivot tables that show your TraceLevel splits.

You may extend or modify the tools to make your query granular or larger. 
Specifying wide or large filter ranges will make the query take a long time to download.
Currently aborts at 100000 rows or 15 minutes - whichever is earlier to avoid out of memory issues.

Logs from you **hadoopservicelog** table (below is a snapshot from a Linux Storm cluster):
![Image of HadoopServiceLog workbook](HadoopServiceLog.png)

The excel workbook also contains the ability to create Pivot table to show components failing the most:
![Image of PivotTableComponentErrorWarn workbook](PivotTableComponentErrorWarn.png)

## References
* https://social.msdn.microsoft.com/Forums/azure/en-US/8a1b48a3-2617-4a2c-980f-4022005a9afa/question-about-logging-in-storm-with-hdinsight?forum=hdinsight
* http://blogs.msdn.com/b/brian_swan/archive/2014/01/06/accessing-hadoop-logs-in-hdinsight.aspx
* https://github.com/hdinsight/hdinsight-storm-examples/issues/7

## TODO (How you can help)
* Switch to Dynamic table entities to support other tables
* Augment parsing & checking
* Query building helper
