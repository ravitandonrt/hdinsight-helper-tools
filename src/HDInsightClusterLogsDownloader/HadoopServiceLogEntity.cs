using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HDInsightClusterLogsDownloader
{
    public class HadoopServiceLogEntity : TableEntity
    {
        public string Tenant { get; set; }
        public string Role { get; set; }
        public string TraceLevel { get; set; }
        public string ComponentName { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return String.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}", ETag, PartitionKey, RowKey, Timestamp, Role, TraceLevel, ComponentName, Message);
        }
    }

    public class WindowsHadoopServiceLogEntity : HadoopServiceLogEntity
    {
        public string RoleInstance { get; set; }

        public override string ToString()
        {
            return String.Format("{0}|{1}", base.ToString(), RoleInstance);
        }
    }

    public class LinuxHadoopServiceLogEntity : HadoopServiceLogEntity
    {
        public string Host { get; set; }

        public override string ToString()
        {
            return String.Format("{0}|{1}", base.ToString(), Host);
        }
    }
}
