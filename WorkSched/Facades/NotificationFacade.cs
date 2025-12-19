using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace WorkSched.Facades
{
    public class NotificationFacade
    {
        private string GetCS()
        {
            return ConfigurationManager
                .ConnectionStrings["WorkSchedConnectionString"]
                .ConnectionString;
        }

        public async Task NotifyEmployeeAsync(int employeeId, string type, string message)
        {
            using (var conn = new SqlConnection(GetCS()))
            {
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.Notifications (EmployeeId, Type, Message, IsRead, CreatedDate)
                    SELECT EmployeeId, @type, @message, 0, GETDATE() FROM dbo.Employees", conn))
                {
                    cmd.Parameters.AddWithValue("@empId", employeeId);
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@message", message);

                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task NotifyAllAsync(string type, string message)
        {
            using (var conn = new SqlConnection(GetCS()))
            {
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.Notifications (EmployeeId, Type, Message)
                    SELECT EmployeeId, @type, @message FROM dbo.Employees", conn))
                {
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@message", message);

                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<Tuple<int, int, int>> GetStatsAsync()
        {
            using (var conn = new SqlConnection(GetCS()))
            {
                using (var cmd = new SqlCommand(@"
                    SELECT 
                        COUNT(*) AS Total,
                        SUM(CASE WHEN IsRead = 1 THEN 1 ELSE 0 END) AS ReadCount
                    FROM dbo.Notifications", conn))
                {
                    await conn.OpenAsync();

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            int total = reader.GetInt32(0);
                            int read = reader.IsDBNull(1) ? 0 : reader.GetInt32(1); 
                            int unread = total - read;

                            return Tuple.Create(total, read, unread);
                        }
                    }
                }
            }   

            return Tuple.Create(0, 0, 0);
        }
    }
}
