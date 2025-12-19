using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

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
            Loaded += (_, __) => { LoadToday(); LoadMyLeaves(); LoadNotifications(); };
        }

        private static string GetCS() =>
            ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
            ?? Properties.Settings.Default.WorkSchedConnectionString
            ?? throw new InvalidOperationException("Не найдена строка подключения 'WorkSchedConnectionString'.");

        private void LoadToday()
        {
            try
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
                            var ps = r[0] as string;
                            var pe = r[1] as string;
                            var st = r[2] as string;
                            txtTodayPlan.Text = $"План: {ps ?? "?"}–{pe ?? "?"} (статус {st ?? "?"})";
                        }
                        else
                        {
                            txtTodayPlan.Text = "План: не задан";
                        }
                    }
                }

                using (var conn = new SqlConnection(GetCS()))
                using (var cmd = new SqlCommand(
                    @"SELECT CheckIn, CheckOut, Status FROM dbo.Attendance 
              WHERE EmployeeId=@id AND WorkDate=CAST(GETDATE() AS date)", conn))
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

                LoadRecentAttendance();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки данных на сегодня: " + ex.Message);
            }
        }

        private void LoadRecentAttendance()
        {
            try
            {
                var cs = GetCS();
                using (var conn = new SqlConnection(cs))
                using (var da = new SqlDataAdapter(@"
            SELECT TOP(10) 
                   a.WorkDate,
                   a.CheckIn,
                   a.CheckOut,
                   a.Status,
                   CASE 
                       WHEN a.CheckIn IS NOT NULL AND a.CheckOut IS NOT NULL 
                       THEN CONVERT(varchar(5), DATEADD(MINUTE, 
                              DATEDIFF(MINUTE, a.CheckIn, a.CheckOut), 0), 108)
                       ELSE '—'
                   END AS Duration
            FROM dbo.Attendance a
            WHERE a.EmployeeId = @id 
            ORDER BY a.WorkDate DESC", conn))
                {
                    da.SelectCommand.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                    var dt = new DataTable();
                    da.Fill(dt);
                    gridRecentAttendance.ItemsSource = dt.DefaultView;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки последних отметок: " + ex.Message);
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
                LoadRecentAttendance();
                MessageBox.Show("Check-in выполнен успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка Check-in: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void OnCheckOut(object sender, RoutedEventArgs e)
        {
            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(
                    @"UPDATE dbo.Attendance SET CheckOut=GETDATE() 
              WHERE EmployeeId=@id AND WorkDate=CAST(GETDATE() AS date);", conn))
                {
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                LoadToday(); 
                LoadRecentAttendance(); 
                MessageBox.Show("Check-out выполнен успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка Check-out: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadMyLeaves()
        {
            try
            {
                var cs = GetCS();
                using (var conn = new SqlConnection(cs))
                using (var da = new SqlDataAdapter(@"
                    SELECT LeaveId, Type, Status, StartDate, EndDate, Reason
                    FROM dbo.Leaves WHERE EmployeeId=@id 
                    ORDER BY StartDate DESC", conn))
                {
                    da.SelectCommand.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                    var dt = new DataTable();
                    da.Fill(dt);
                    gridMyLeaves.ItemsSource = dt.DefaultView;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки заявок: " + ex.Message);
            }
        }

        private async void OnLeaveSubmit(object sender, RoutedEventArgs e)
        {
            var typeItem = cbLeaveType.SelectedItem as ComboBoxItem;
            var type = typeItem?.Content?.ToString();
            var d1 = dpStart.SelectedDate;
            var d2 = dpEnd.SelectedDate;
            var reason = tbReason.Text?.Trim();

            if (string.IsNullOrWhiteSpace(type) || d1 == null || d2 == null)
            {
                MessageBox.Show("Заполните тип и даты заявки.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (d1 > d2)
            {
                MessageBox.Show("Дата начала не может быть позже даты окончания.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(
                    @"INSERT INTO dbo.Leaves(EmployeeId, Type, Status, StartDate, EndDate, Reason)
              VALUES(@empId, @type, N'Pending', @startDate, @endDate, @reason)", conn)) // ИСПРАВЛЕНО!
                {
                    cmd.Parameters.Add("@empId", SqlDbType.Int).Value = _id;
                    cmd.Parameters.Add("@type", SqlDbType.NVarChar, 50).Value = type;
                    cmd.Parameters.Add("@startDate", SqlDbType.Date).Value = d1.Value.Date;
                    cmd.Parameters.Add("@endDate", SqlDbType.Date).Value = d2.Value.Date;
                    cmd.Parameters.Add("@reason", SqlDbType.NVarChar, 200).Value =
                        string.IsNullOrWhiteSpace(reason) ? DBNull.Value : (object)reason;

                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }

                LoadMyLeaves();

                cbLeaveType.SelectedIndex = -1;
                dpStart.SelectedDate = null;
                dpEnd.SelectedDate = null;
                tbReason.Clear();

                MessageBox.Show("Заявка успешно отправлена на рассмотрение!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка отправки заявки: " + ex.Message, "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadNotifications()
        {
            try
            {
                var cs = GetCS();
                using (var conn = new SqlConnection(cs))
                using (var da = new SqlDataAdapter(@"
                    SELECT 
                        NotificationId, 
                        Type, 
                        Message, 
                        IsRead, 
                        CreatedDate
                    FROM dbo.Notifications 
                    WHERE EmployeeId = @id 
                    ORDER BY CreatedDate DESC", conn))
                {
                    da.SelectCommand.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                    var dt = new DataTable();
                    da.Fill(dt);
                    gridNotifications.ItemsSource = dt.DefaultView;

                    int total = dt.Rows.Count;
                    int unread = dt.AsEnumerable()
                        .Count(r => r["IsRead"] != DBNull.Value && Convert.ToBoolean(r["IsRead"]) == false);

                    txtNotificationsInfo.Text = $"Уведомлений: {total} (новых: {unread})";
                }
            }
            catch (SqlException sqlEx)
            {
                if (!sqlEx.Message.Contains("Invalid object name 'Notifications'"))
                {
                    MessageBox.Show("Ошибка загрузки уведомлений: " + sqlEx.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    txtNotificationsInfo.Text = "Уведомлений: 0 (новых: 0)";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки уведомлений: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnNotificationsRefresh(object sender, RoutedEventArgs e)
        {
            LoadNotifications();
        }

        private async void OnMarkAsRead(object sender, RoutedEventArgs e)
        {
            var selectedItems = gridNotifications.SelectedItems;
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Выберите уведомления для отметки как прочитанные.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();

                    foreach (DataRowView row in selectedItems)
                    {
                        int notificationId = Convert.ToInt32(row["NotificationId"]);
                        using (var cmd = new SqlCommand(
                            "UPDATE dbo.Notifications SET IsRead = 1 WHERE NotificationId = @id", conn))
                        {
                            cmd.Parameters.Add("@id", SqlDbType.Int).Value = notificationId;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                LoadNotifications();
                MessageBox.Show($"Отмечено как прочитанных: {selectedItems.Count}", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка обновления уведомлений: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnMarkAllAsRead(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Отметить все уведомления как прочитанные?", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(
                    "UPDATE dbo.Notifications SET IsRead = 1 WHERE EmployeeId = @id AND IsRead = 0", conn))
                {
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = _id;
                    await conn.OpenAsync();
                    int affectedRows = await cmd.ExecuteNonQueryAsync();

                    LoadNotifications();
                    MessageBox.Show($"Отмечено как прочитанных: {affectedRows} уведомлений", "Успех",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка обновления уведомлений: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}