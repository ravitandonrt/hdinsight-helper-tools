using log4net;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;

namespace HDInsightClusterLogsDownloader
{
    public enum OperatingSystemType
    {
        Linux,
        Windows
    }

    public class HDInsightClusterLogsDownloader
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(HDInsightClusterLogsDownloader));

        public const int MAX_LOGS = 100000;
        public const TimeSpan timeout = TimeSpan.FromMinutes(15);

        static void Main(string[] args)
        {
            string storageAccountName = ConfigurationManager.AppSettings.Get("StorageAccountName");
            string storageAccountKey = ConfigurationManager.AppSettings.Get("StorageAccountKey");
            string storageAccountEndpointSuffix = ConfigurationManager.AppSettings.Get("StorageAccountEndpointSuffix");
            string storageAccountTableNamePrefix = ConfigurationManager.AppSettings.Get("storageAccountTableNamePrefix");
            string storageAccountTableName = ConfigurationManager.AppSettings.Get("StorageAccountTableName");

            var clusterOperatingSystemType = (OperatingSystemType)Enum.Parse(typeof(OperatingSystemType), ConfigurationManager.AppSettings.Get("ClusterOperatingSystemType"));

            var credentials = new StorageCredentials(storageAccountName, storageAccountKey);
            var cloudStorageAccount = new CloudStorageAccount(credentials, storageAccountEndpointSuffix, true);
            var cloudTableClient = cloudStorageAccount.CreateCloudTableClient();

            CloudTable cloudTable = null;
            if (String.IsNullOrWhiteSpace(storageAccountTableName))
            {
                var cloudTables = cloudTableClient.ListTables(storageAccountTableNamePrefix).Where(t => t.Name.EndsWith("hadoopservicelog", StringComparison.OrdinalIgnoreCase)).ToList();
                Logger.InfoFormat("Found {0} 'hadoopservicelog' tables:\r\n{1}", cloudTables.Count, String.Join(Environment.NewLine, cloudTables.Select(t => t.Name)));
                if (cloudTables.Count == 1)
                {
                    storageAccountTableName = cloudTables.First().Name;
                }
                else
                {
                    if (cloudTables.Count == 0)
                    {
                        Logger.Error("No tables found with this prefix, try a shorter prefix. Prefix: " + storageAccountTableNamePrefix);
                    }
                    else
                    {
                        Logger.Error("More than one table found with same prefix, please pick one and use that name.");
                    }
                    Environment.Exit(1);
                }
            }

            cloudTable = cloudTableClient.GetTableReference(storageAccountTableName);

            var dateTimeMin = DateTimeOffset.UtcNow.AddHours(-2);
            var dateTimeMax = dateTimeMin.AddMinutes(5);
            
            //var dateTimeMin = new DateTime(2015, 8, 19, 8, 55, 0, DateTimeKind.Utc);
            //var dateTimeMax = new DateTime(2015, 8, 19, 9, 10, 0, DateTimeKind.Utc);

            var stopwatch = Stopwatch.StartNew();
            GetLogs(clusterOperatingSystemType, cloudTable, dateTimeMin, dateTimeMax);

            Logger.InfoFormat("Done! Total Time Elapsed: {0} secs", stopwatch.Elapsed.TotalSeconds);

            if(Debugger.IsAttached)
            {
                Logger.InfoFormat("Press a key to exit...");
                Console.ReadKey();
            }
        }

        public static void GetLogs(
            OperatingSystemType clusterOperatingSystemType, CloudTable cloudTable,
            DateTimeOffset dateTimeMin, DateTimeOffset dateTimeMax,
            string roleName = null, string roleInstance = null, string componentName = null,
            string endpointSuffix = "core.windows.net")
        {
            //string partitionKeyMin = string.Format("0000000000000000000___{0}", new DateTime(dateTimeMin.Ticks, DateTimeKind.Unspecified).ToBinary().ToString("D19"));
            //string partitionKeyMax = string.Format("0000000000000000100___{0}", new DateTime(dateTimeMax.Ticks, DateTimeKind.Unspecified).ToBinary().ToString("D19"));
            //Logger.InfoFormat(partitionKeyMin);
            //Logger.InfoFormat(partitionKeyMax);                 

            var filters = new List<FilterClause>()
                              {
                                  //new FilterClause("PartitionKey", "gt", partitionKeyMin),
                                  //new FilterClause("PartitionKey", "lt", partitionKeyMax),
                                  new FilterClause("Timestamp", QueryComparisons.GreaterThan, dateTimeMin), //2012-12-23T21:11:32.8339201Z
                                  new FilterClause("Timestamp", QueryComparisons.LessThan, dateTimeMax)
                                  //new FilterClause("TraceLevel", QueryComparisons.Equal, "Error"),
                                  //new FilterClause("Role", QueryComparisons.Equal, "workernode")
                              };

            if (!String.IsNullOrEmpty(roleName))
            {
                filters.Add(new FilterClause("Role", "eq", roleName));
            }

            if (!String.IsNullOrEmpty(roleInstance))
            {
                if (clusterOperatingSystemType == OperatingSystemType.Linux)
                {
                    filters.Add(new FilterClause("Host", "eq", roleInstance));
                }
                else
                {
                    filters.Add(new FilterClause("RoleInstance", "eq", roleInstance));
                }
            }

            if (!String.IsNullOrEmpty(componentName))
            {
                filters.Add(new FilterClause("ComponentName", "eq", componentName));
            }

            var query = BuildQuery<HadoopServiceLogEntity>(filters);

            Logger.InfoFormat("Query = {0}", query.FilterString);

            var logList = RunQuery(clusterOperatingSystemType, cloudTable, query);

            HDInsightClusterLogsWriter.Write(clusterOperatingSystemType, cloudTable.Name + ".xlsx", logList);

            Logger.InfoFormat("Done - {0}. Rows: {1}", cloudTable.Name, logList.Count);
        }

        public static List<Object> RunQuery(OperatingSystemType clusterOperatingSystemType, CloudTable cloudTable, TableQuery<HadoopServiceLogEntity> query)
        {
            var stopwatch = Stopwatch.StartNew();
            List<Object> logList = new List<Object>();
            try
            {
                EntityResolver<HadoopServiceLogEntity> entityResolver = (pk, rk, ts, props, etag) =>
                {

                    HadoopServiceLogEntity resolvedEntity = null;

                    if (clusterOperatingSystemType == OperatingSystemType.Linux)
                    {
                        resolvedEntity = new LinuxHadoopServiceLogEntity();
                    }
                    else
                    {
                        resolvedEntity = new WindowsHadoopServiceLogEntity();
                    }

                    resolvedEntity.PartitionKey = pk;
                    resolvedEntity.RowKey = rk;
                    resolvedEntity.Timestamp = ts;
                    resolvedEntity.ETag = etag;
                    resolvedEntity.ReadEntity(props, null);

                    return resolvedEntity;
                };

                TableQuerySegment<HadoopServiceLogEntity> currentSegment = null;
                while (currentSegment == null || currentSegment.ContinuationToken != null)
                {
                    var task = cloudTable.ExecuteQuerySegmentedAsync(
                        query,
                        entityResolver,
                        currentSegment != null ? currentSegment.ContinuationToken : null);

                    task.Wait();

                    currentSegment = task.Result;
                    if (currentSegment != null)
                    {
                        logList.AddRange(currentSegment.Results as List<HadoopServiceLogEntity>);
                    }

                    Logger.InfoFormat("Rows Retreived: {0}, Time Elapsed: {1:0.00}, Continuation Token: {2}", 
                        logList.Count, stopwatch.Elapsed.TotalSeconds, currentSegment.ContinuationToken);
                    if (logList.Count >= MAX_LOGS || stopwatch.Elapsed > timeout)
                    {
                        Logger.ErrorFormat("Your query result is either very large or taking too long, aborting the fetch. Rows: {0}", logList.Count);
                        Logger.Error("Try reducing the query window or add more filters. Your fetched results will still be available in a excel workbook.");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.InfoFormat(ex.ToString());
                throw;
            }

            return logList;
        }

        public static TableQuery<T> BuildQuery<T>(List<FilterClause> filterClauses)
        {
            var tableQuery = new TableQuery<T>();
            var filters = new List<string>();
            foreach (var filterClause in filterClauses)
            {
                if (filterClause.FilterValue is DateTimeOffset)
                {
                    filters.Add(TableQuery.GenerateFilterConditionForDate(filterClause.FilterColumn, filterClause.FilterCondition, (DateTimeOffset)filterClause.FilterValue));
                }
                else
                {
                    filters.Add(TableQuery.GenerateFilterCondition(filterClause.FilterColumn, filterClause.FilterCondition, filterClause.FilterValue.ToString()));
                }
            }
            var filterString = "(" + String.Join(") " + TableOperators.And + " (", filters) + ")";
            return tableQuery.Where(filterString);
        }

        public class FilterClause
        {
            public string FilterColumn { get; set; }
            public string FilterCondition { get; set; }
            public object FilterValue { get; set; }

            public FilterClause(string filterColumn, string filterCondition, object filterValue)
            {
                this.FilterColumn = filterColumn;
                this.FilterCondition = filterCondition;
                this.FilterValue = filterValue;
            }
        }
    }

}
