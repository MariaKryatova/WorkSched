using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace WorkSched
{
    public class ReportBuilder
    {
        private readonly string _connectionString;
        private DataTable _reportData;
        private StringBuilder _csvBuilder;
        private ReportType _reportType;
        private DateTime _startDate;
        private DateTime _endDate;

        public ReportBuilder()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
                ?? Properties.Settings.Default.WorkSchedConnectionString
                ?? throw new InvalidOperationException("Не найдена строка подключения.");

            _reportData = new DataTable();
            _csvBuilder = new StringBuilder();
        }

        public ReportBuilder SetReportType(ReportType reportType)
        {
            _reportType = reportType;
            return this;
        }

        public ReportBuilder SetDateRange(DateTime startDate, DateTime endDate)
        {
            _startDate = startDate;
            _endDate = endDate;
            return this;
        }

        public ReportBuilder BuildData()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                if (_reportType == ReportType.DepartmentAttendance)
                {
                    BuildDepartmentAttendanceReport(conn);
                }
                else 
                {
                    BuildLeavesAndSickReport(conn);
                }
            }

            return this;
        }

        public ReportBuilder GenerateCsv()
        {
            _csvBuilder.Clear();

            foreach (DataColumn column in _reportData.Columns)
            {
                _csvBuilder.Append(EscapeCsv(column.ColumnName));
                _csvBuilder.Append(";");
            }
            _csvBuilder.AppendLine();

            foreach (DataRow row in _reportData.Rows)
            {
                foreach (var item in row.ItemArray)
                {
                    _csvBuilder.Append(EscapeCsv(item?.ToString() ?? ""));
                    _csvBuilder.Append(";");
                }
                _csvBuilder.AppendLine();
            }

            return this;
        }

        public DataTable GetDataTableResult()
        {
            return _reportData;
        }

        public string GetCsvResult()
        {
            return _csvBuilder.ToString();
        }

        public void ExportToFile(string filePath)
        {
            if (string.IsNullOrEmpty(_csvBuilder.ToString()))
            {
                GenerateCsv();
            }

            System.IO.File.WriteAllText(filePath, _csvBuilder.ToString(), Encoding.UTF8);
        }

        private void BuildDepartmentAttendanceReport(SqlConnection conn)
        {
            using (var cmd = new SqlCommand(@"
                SELECT 
                    d.Name as [Отдел],
                    COUNT(DISTINCT e.EmployeeId) as [Всего сотрудников],
                    SUM(CASE WHEN a.CheckIn IS NOT NULL THEN 1 ELSE 0 END) as [Отметившихся],
                    CAST(SUM(CASE WHEN a.CheckIn IS NOT NULL THEN 1 ELSE 0 END) * 100.0 / 
                         NULLIF(COUNT(DISTINCT e.EmployeeId), 0) as decimal(5,2)) as [Процент, %]
                FROM dbo.Departments d
                LEFT JOIN dbo.Employees e ON e.DepartmentId = d.DepartmentId
                LEFT JOIN dbo.Attendance a ON a.EmployeeId = e.EmployeeId 
                    AND a.WorkDate BETWEEN @start AND @end
                GROUP BY d.DepartmentId, d.Name
                ORDER BY d.Name", conn))
            {
                cmd.Parameters.Add("@start", SqlDbType.Date).Value = _startDate;
                cmd.Parameters.Add("@end", SqlDbType.Date).Value = _endDate;

                using (var da = new SqlDataAdapter(cmd))
                {
                    _reportData.Clear();
                    da.Fill(_reportData);
                }
            }
        }

        private void BuildLeavesAndSickReport(SqlConnection conn)
        {
            using (var cmd = new SqlCommand(@"
                SELECT 
                    e.FullName as [Сотрудник], 
                    d.Name as [Отдел],
                    l.Type as [Тип], 
                    l.StartDate as [С], 
                    l.EndDate as [По],
                    l.Status as [Статус], 
                    l.Reason as [Причина]
                FROM dbo.Leaves l
                JOIN dbo.Employees e ON e.EmployeeId = l.EmployeeId
                LEFT JOIN dbo.Departments d ON d.DepartmentId = e.DepartmentId
                WHERE (l.StartDate <= @end AND l.EndDate >= @start)
                ORDER BY l.StartDate DESC", conn))
            {
                cmd.Parameters.Add("@start", SqlDbType.Date).Value = _startDate;
                cmd.Parameters.Add("@end", SqlDbType.Date).Value = _endDate;

                using (var da = new SqlDataAdapter(cmd))
                {
                    _reportData.Clear();
                    da.Fill(_reportData);
                }
            }
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.Contains(";") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                value = value.Replace("\"", "\"\"");
                return $"\"{value}\"";
            }
            return value;
        }
    }

    public enum ReportType
    {
        DepartmentAttendance,
        LeavesAndSick
    }
}