using log4net;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using OfficeOpenXml.Style;
using OfficeOpenXml.Table.PivotTable;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace HDInsightClusterLogsDownloader
{
    public class PivotField
    {
        public string FieldName;
        public string DisplayName;

        public PivotField()
        {
        }

        public PivotField(string fieldName, string displayName = null)
        {
            this.FieldName = fieldName;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                this.DisplayName = fieldName;
            }
            else
            {
                this.DisplayName = displayName;
            }
        }
    }

    public class PivotDataField : PivotField
    {
        public DataFieldFunctions Function;

        public PivotDataField(string fieldName, string displayName = null, DataFieldFunctions funtion = DataFieldFunctions.Average)
        {
            this.FieldName = fieldName;

            if (string.IsNullOrWhiteSpace(displayName))
            {
                this.DisplayName = fieldName;
            }
            else
            {
                this.DisplayName = displayName;
            }

            this.Function = funtion;
        }
    }

    public class HDInsightClusterLogsWriter
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(HDInsightClusterLogsDownloader));

        public static void Write(OperatingSystemType clusterOperatingSystemType, string filePath, List<Object> logEntities)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            var excelPackageLogs = new ExcelPackage(new FileInfo(filePath));
            List<MemberInfo> columnInfoBuilds;

            var excelWorksheetHadoopServiceLogs = CreateExcelWorksheetForDataList(excelPackageLogs, "HadoopServiceLog", logEntities, out columnInfoBuilds);

            if (columnInfoBuilds != null && columnInfoBuilds.Count > 0)
            {
                CreateExcelPivotForWorksheet(excelPackageLogs, excelWorksheetHadoopServiceLogs, "ComponentLogTypePivot",
                    new List<PivotField>() { new PivotField("Role") },
                    new List<PivotField>() { new PivotField("ComponentName") },
                    new List<PivotField>() { new PivotField("TraceLevel") },
                    new List<PivotDataField>() { new PivotDataField("Message", "Count", DataFieldFunctions.Count) },
                    logEntities.Count, columnInfoBuilds.Count
                    );
            }
            excelPackageLogs.Save();
        }

        public static ExcelWorksheet CreateExcelWorksheetForDataList(ExcelPackage exPackage, string sheetName, List<Object> dataList, out List<MemberInfo> columnInfo)
        {
            var exWorksheet = exPackage.Workbook.Worksheets.Add(sheetName);
            int i = 1, j = 1;

            columnInfo = null;
            if (dataList.Count > 0)
            {
                var obj = dataList[0];

                columnInfo = (obj.GetType()).GetProperties(BindingFlags.Public | BindingFlags.Instance).Cast<MemberInfo>().ToList();

                foreach (var member in columnInfo)
                {
                    exWorksheet.Cells[1, i].Value = member.Name;
                    if (member.MemberType == MemberTypes.Property &&
                        (((PropertyInfo)member).PropertyType == typeof(DateTime)) || (((PropertyInfo)member).PropertyType == typeof(DateTimeOffset)))
                    {
                        exWorksheet.Column(i).Style.Numberformat.Format = "yyyy-MM-dd";
                    }
                    i++;
                }

                i = 2;
                foreach (var data in dataList)
                {
                    j = 1;
                    foreach (var member in columnInfo)
                    {
                        exWorksheet.Cells[i, j++].Value = data.GetType().GetProperty(member.Name).GetValue(data);
                    }
                    i++;
                }
                exWorksheet.Cells[1, 1, i - 1, j - 1].AutoFilter = true;
                using (var range = exWorksheet.Cells[1, 1, 1, j - 1])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(Color.DarkBlue);
                    range.Style.Font.Color.SetColor(Color.White);
                    range.AutoFitColumns();
                }
            }
            return exWorksheet;
        }

        public static void CreateExcelPivotForWorksheet(ExcelPackage exPackage, ExcelWorksheet exWorksheet, string name,
            List<PivotField> pageFields, List<PivotField> rowFields, List<PivotField> columnFields, List<PivotDataField> dataFields,
            int rowCount, int columnCount)
        {
            //Pivot PassRate
            var exPivotSheet = exPackage.Workbook.Worksheets.Add(name);

            //add 1 to rowCount as TopRow is ColumnNames
            var exPivotTable = exPivotSheet.PivotTables.Add(exPivotSheet.Cells["A3"], exWorksheet.Cells[1, 1, rowCount + 1, columnCount], name);

            if (pageFields != null)
            {
                foreach (var field in pageFields)
                {
                    exPivotTable.PageFields.Add(exPivotTable.Fields[field.FieldName]);
                }
            }

            if (rowFields != null)
            {
                foreach (var field in rowFields)
                {
                    exPivotTable.RowFields.Add(exPivotTable.Fields[field.FieldName]);
                }
            }

            if (columnFields != null)
            {
                foreach (var field in columnFields)
                {
                    exPivotTable.ColumnFields.Add(exPivotTable.Fields[field.FieldName]);
                }
            }

            if (dataFields != null)
            {
                var i = 0;
                foreach (var field in dataFields)
                {
                    exPivotTable.DataFields.Add(exPivotTable.Fields[field.FieldName]);
                    exPivotTable.DataFields[i++].Function = field.Function;
                }

                foreach (var df in exPivotTable.DataFields)
                {
                    if (df.Function == DataFieldFunctions.Average)
                    {
                        df.Format = "#.00";
                    }
                }
            }

            exPivotTable.DataOnRows = false;

            var chart = exPivotSheet.Drawings.AddChart("PC" + name, eChartType.ColumnClustered, exPivotTable) as ExcelBarChart;

            if (dataFields.Count > 1)
            {
                chart.VaryColors = true;
            }

            chart.SetPosition(4, 0, 6, 0);
            chart.SetSize(640, 480);

            chart.Title.Text = name;
            chart.Title.Font.Size = 12;

            chart.XAxis.Title.Text = String.Join(", ", rowFields.Select(f => f.DisplayName).ToList());
            chart.XAxis.Title.Font.Size = 10;

            chart.YAxis.Title.Text = String.Join(", ", dataFields.Select(f => f.DisplayName).ToList());
            chart.YAxis.Title.Font.Size = 10;

            chart.DataLabel.ShowValue = true;
        }
    }
}
