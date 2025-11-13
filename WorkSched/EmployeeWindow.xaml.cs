using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Windows;

namespace WorkSched
{
    public partial class EmployeeWindow : Window
    {
        private readonly int _id;
        private readonly string _name;
        private readonly string _role;

        public EmployeeWindow(int id, string name, string role)
        {
            InitializeComponent();
            _id = id; _name = name; _role = role;
            Title = $"Сотрудник — {_name}";
            Loaded += (_, __) => { LoadToday(); LoadMyLeaves(); };
        }

        private static string GetCS() =>
            ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
            ?? Properties.Settings.Default.WorkSchedConnectionString
            ?? throw new InvalidOperationException("Не найдена строка подключения 'WorkSchedConnectionString'.");

        private void LoadToday()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(@"
                SELECT TOP(1)
                       COALESCE(CONVERT(varchar(5), s.PlannedStart, 108), CONVERT(varchar(5), sh.StartTime, 108)) AS PlanStart,
                       COALESCE(CONVERT(varchar(5), s.PlannedEnd, 108), CONVERT(varchar(5), sh.EndTime, 108))   AS PlanEnd,
                       s.Status
                FROM dbo.Schedules s
                LEFT JOIN dbo.Shifts sh ON sh.ShiftId = s.ShiftId
                WHERE s.EmployeeId = @id AND s.WorkDate = CAST(GETDATE() AS date)
                ORDER BY s.ScheduleId DESC", conn))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        var ps = r[0] as string; var pe = r[1] as string; var st = r[2] as string;
                        txtTodayPlan.Text = $"План: {ps ?? "?"}–{pe ?? "?"} (статус {st ?? "?"})";
                    }
                    else
                    {
                        txtTodayPlan.Text = "План: не задан";
                    }
                }
            }

            using (var conn = new SqlConnection(GetCS()))
            using (var cmd = new SqlCommand(@"SELECT CheckIn, CheckOut, Status FROM dbo.Attendance WHERE EmployeeId=@id AND WorkDate=CAST(GETDATE() AS date)", conn))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        var ci = r.IsDBNull(0) ? (DateTime?)null : r.GetDateTime(0);
                        var co = r.IsDBNull(1) ? (DateTime?)null : r.GetDateTime(1);
                        var st = r.IsDBNull(2) ? "" : r.GetString(2);
                        txtTodayInfo.Text = $"Явка: {(ci.HasValue ? ci.Value.ToString("HH:mm") : "—")} / {(co.HasValue ? co.Value.ToString("HH:mm") : "—")} ({st})";
                    }
                    else
                    {
                        txtTodayInfo.Text = "Явка: — / —";
                    }
                }
            }
        }

        private async void OnCheckIn(object sender, RoutedEventArgs e)
        {
            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();
                    using (var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM dbo.Attendance WHERE EmployeeId=@id AND WorkDate=CAST(GETDATE() AS date))
    UPDATE dbo.Attendance SET CheckIn = ISNULL(CheckIn, GETDATE()), Status=N'Present'
    WHERE EmployeeId=@id AND WorkDate=CAST(GETDATE() AS date);
ELSE
    INSERT INTO dbo.Attendance(EmployeeId, WorkDate, CheckIn, Status)
    VALUES(@id, CAST(GETDATE() AS date), GETDATE(), N'Present');", conn))
                    {
                        cmd.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                LoadToday();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка Check-in: " + ex.Message); }
        }

        private async void OnCheckOut(object sender, RoutedEventArgs e)
        {
            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(@"UPDATE dbo.Attendance SET CheckOut=GETDATE() WHERE EmployeeId=@id AND WorkDate=CAST(GETDATE() AS date);", conn))
                {
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                LoadToday();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка Check-out: " + ex.Message); }
        }

        private void LoadMyLeaves()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var da = new SqlDataAdapter(@"
                SELECT LeaveId, Type, Status, StartDate, EndDate, Reason
                FROM dbo.Leaves WHERE EmployeeId=@id ORDER BY StartDate DESC", conn))
            {
                da.SelectCommand.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                var dt = new DataTable(); da.Fill(dt);
                gridMyLeaves.ItemsSource = dt.DefaultView;
            }
        }

        private async void OnLeaveSubmit(object sender, RoutedEventArgs e)
        {
            var type = (cbLeaveType.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
            var d1 = dpStart.SelectedDate; var d2 = dpEnd.SelectedDate;
            var reason = tbReason.Text?.Trim();

            if (string.IsNullOrWhiteSpace(type) || d1 == null || d2 == null || d1 > d2)
            { MessageBox.Show("Заполните тип и корректные даты."); return; }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.Leaves(EmployeeId, Type, Status, StartDate, EndDate, Reason)
                    VALUES(@e, @t, N'Pending', @s, @e2, @r)", conn))
                {
                    cmd.Parameters.Add("@e", SqlDbType.Int).Value = _id;
                    cmd.Parameters.Add("@t", SqlDbType.NVarChar, 50).Value = type;
                    cmd.Parameters.Add("@s", SqlDbType.Date).Value = d1.Value.Date;
                    cmd.Parameters.Add("@e2", SqlDbType.Date).Value = d2.Value.Date;
                    cmd.Parameters.Add("@r", SqlDbType.NVarChar, 200).Value = (object)reason ?? DBNull.Value;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                LoadMyLeaves();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка заявки: " + ex.Message); }
        }
    }
}
