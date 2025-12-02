using Microsoft.Win32;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WorkSched
{
    public partial class AdminPanelWindow : Window
    {
        private readonly int _adminId;
        private readonly string _adminName;

        public AdminPanelWindow(int adminId, string adminName)
        {
            InitializeComponent();
            _adminId = adminId;
            _adminName = adminName;
            Title = "Админ-панель — " + _adminName;
            Loaded += (s, e) => {
                LoadUsers();
                LoadShifts();
                LoadLeaves();
                LoadScheduleData();
                LoadNotificationsForAdmin();
                InitializeReportDates();
            };
        }

        private void InitializeReportDates()
        {
            dpReportStart.SelectedDate = DateTime.Today.AddDays(-7);
            dpReportEnd.SelectedDate = DateTime.Today;
            dpScheduleDate.SelectedDate = DateTime.Today;
        }

        private static string GetCS() =>
            ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
            ?? Properties.Settings.Default.WorkSchedConnectionString
            ?? throw new InvalidOperationException("Не найдена строка подключения 'WorkSchedConnectionString'.");

        private void LoadUsers()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var da = new SqlDataAdapter(@"
                SELECT e.EmployeeId, e.Login, e.FullName, e.Role, ISNULL(d.Name, N'') AS Department
                FROM dbo.Employees e
                LEFT JOIN dbo.Departments d ON d.DepartmentId = e.DepartmentId
                ORDER BY e.EmployeeId", conn))
            {
                var dt = new DataTable();
                da.Fill(dt);
                gridUsers.ItemsSource = dt.DefaultView;
                txtUsersInfo.Text = "Всего пользователей: " + dt.Rows.Count;
            }
        }

        private void OnUsersRefresh(object sender, RoutedEventArgs e) { LoadUsers(); }

        private async void OnUsersDelete(object sender, RoutedEventArgs e)
        {
            var rows = gridUsers.SelectedItems.Cast<DataRowView>().ToList();
            if (rows.Count == 0) { MessageBox.Show("Выберите строки."); return; }

            var toDelete = rows
                .Where(r => !string.Equals(Convert.ToString(r.Row["Role"]), "Admin", StringComparison.OrdinalIgnoreCase)
                            && Convert.ToInt32(r.Row["EmployeeId"]) != _adminId)
                .Select(r => new { Id = Convert.ToInt32(r.Row["EmployeeId"]), Login = Convert.ToString(r.Row["Login"]) })
                .ToList();
            if (toDelete.Count == 0) { MessageBox.Show("Нельзя удалить админов или самого себя."); return; }

            if (MessageBox.Show("Удалить " + toDelete.Count + " пользователей?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();
                    foreach (var item in toDelete)
                    {
                        using (var cmd = new SqlCommand("DELETE FROM dbo.Employees WHERE EmployeeId=@id", conn))
                        {
                            cmd.Parameters.Add("@id", SqlDbType.Int).Value = item.Id;
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }
                LoadUsers();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка удаления: " + ex.Message); }
        }

        private async void OnUsersSetRole(object sender, RoutedEventArgs e)
        {
            var row = gridUsers.SelectedItem as DataRowView;
            if (row == null) { MessageBox.Show("Выберите пользователя."); return; }

            var cbi = cbRole.SelectedItem as ComboBoxItem;
            var roleItem = cbi != null ? (cbi.Content != null ? cbi.Content.ToString() : null) : null;
            if (string.IsNullOrWhiteSpace(roleItem)) { MessageBox.Show("Выберите роль в списке."); return; }

            int id = Convert.ToInt32(row.Row["EmployeeId"]);
            if (id == _adminId) { MessageBox.Show("Нельзя менять роль своему пользователю из этой панели."); return; }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand("UPDATE dbo.Employees SET Role=@r WHERE EmployeeId=@id", conn))
                {
                    cmd.Parameters.Add("@r", SqlDbType.NVarChar, 20).Value = roleItem;
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                LoadUsers();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка смены роли: " + ex.Message); }
        }

        private async void OnUsersResetPwd(object sender, RoutedEventArgs e)
        {
            var row = gridUsers.SelectedItem as DataRowView;
            if (row == null) { MessageBox.Show("Выберите пользователя."); return; }

            int id = Convert.ToInt32(row.Row["EmployeeId"]);
            if (string.Equals(Convert.ToString(row.Row["Role"]), "Admin", StringComparison.OrdinalIgnoreCase))
            { MessageBox.Show("Сброс пароля админам запрещён здесь."); return; }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand("UPDATE dbo.Employees SET PasswordHash=N'1234' WHERE EmployeeId=@id", conn))
                {
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                MessageBox.Show("Пароль сброшен на '1234'.");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка сброса: " + ex.Message); }
        }

        private void LoadShifts()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var da = new SqlDataAdapter("SELECT ShiftId, Name, StartTime, EndTime, BreakMinutes FROM dbo.Shifts ORDER BY ShiftId", conn))
            {
                var dt = new DataTable();
                da.Fill(dt);
                gridShifts.ItemsSource = dt.DefaultView;
            }
        }

        private void OnShiftSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = gridShifts.SelectedItem as DataRowView;
            if (row != null)
            {
                tbShiftName.Text = Convert.ToString(row.Row["Name"]) ?? "";
                tbStart.Text = Convert.ToString(row.Row["StartTime"]) ?? "";
                tbEnd.Text = Convert.ToString(row.Row["EndTime"]) ?? "";
                tbBreak.Text = Convert.ToString(row.Row["BreakMinutes"]) ?? "";
            }
        }

        private static bool TryParseTime(string s, out TimeSpan t)
        {
            return TimeSpan.TryParse(s, out t);
        }

        private async void OnShiftAdd(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbShiftName.Text) ||
                string.IsNullOrWhiteSpace(tbStart.Text) ||
                string.IsNullOrWhiteSpace(tbEnd.Text) ||
                string.IsNullOrWhiteSpace(tbBreak.Text))
            { MessageBox.Show("Заполните все поля смены."); return; }

            TimeSpan t1, t2; int br;
            if (!TryParseTime(tbStart.Text, out t1) || !TryParseTime(tbEnd.Text, out t2) || !int.TryParse(tbBreak.Text, out br))
            { MessageBox.Show("Неверные форматы. Пример времени: 09:00"); return; }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(
                    "INSERT INTO dbo.Shifts(Name,StartTime,EndTime,BreakMinutes) VALUES(@n,@s,@e,@b)", conn))
                {
                    cmd.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = tbShiftName.Text.Trim();
                    cmd.Parameters.Add("@s", SqlDbType.Time).Value = t1;
                    cmd.Parameters.Add("@e", SqlDbType.Time).Value = t2;
                    cmd.Parameters.Add("@b", SqlDbType.Int).Value = br;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                LoadShifts();
                tbShiftName.Clear(); tbStart.Clear(); tbEnd.Clear(); tbBreak.Clear();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка добавления смены: " + ex.Message); }
        }

        private async void OnShiftUpdate(object sender, RoutedEventArgs e)
        {
            var row = gridShifts.SelectedItem as DataRowView;
            if (row == null) { MessageBox.Show("Выберите смену."); return; }

            TimeSpan t1, t2; int br;
            if (!TryParseTime(tbStart.Text, out t1) || !TryParseTime(tbEnd.Text, out t2) || !int.TryParse(tbBreak.Text, out br))
            { MessageBox.Show("Неверные форматы. Пример времени: 09:00"); return; }

            int id = Convert.ToInt32(row.Row["ShiftId"]);

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(
                    "UPDATE dbo.Shifts SET Name=@n, StartTime=@s, EndTime=@e, BreakMinutes=@b WHERE ShiftId=@id", conn))
                {
                    cmd.Parameters.Add("@n", SqlDbType.NVarChar, 100).Value = tbShiftName.Text.Trim();
                    cmd.Parameters.Add("@s", SqlDbType.Time).Value = t1;
                    cmd.Parameters.Add("@e", SqlDbType.Time).Value = t2;
                    cmd.Parameters.Add("@b", SqlDbType.Int).Value = br;
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                LoadShifts();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка изменения: " + ex.Message); }
        }

        private async void OnShiftDelete(object sender, RoutedEventArgs e)
        {
            var row = gridShifts.SelectedItem as DataRowView;
            if (row == null) { MessageBox.Show("Выберите смену."); return; }

            int id = Convert.ToInt32(row.Row["ShiftId"]);
            if (MessageBox.Show("Удалить выбранную смену?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand("DELETE FROM dbo.Shifts WHERE ShiftId=@id", conn))
                {
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();
                }
                LoadShifts();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка удаления: " + ex.Message); }
        }

        private void LoadLeaves()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var da = new SqlDataAdapter(@"
                SELECT l.LeaveId, e.FullName, l.Type, l.StartDate, l.EndDate, l.Reason
                FROM dbo.Leaves l
                JOIN dbo.Employees e ON e.EmployeeId=l.EmployeeId
                WHERE l.Status=N'Pending'
                ORDER BY l.StartDate", conn))
            {
                var dt = new DataTable();
                da.Fill(dt);
                gridLeaves.ItemsSource = dt.DefaultView;
                txtLeavesInfo.Text = "Ожидает: " + dt.Rows.Count;
            }
        }

        private void OnLeavesRefresh(object sender, RoutedEventArgs e) { LoadLeaves(); }

        private async void OnLeavesApprove(object sender, RoutedEventArgs e)
        {
            var rows = gridLeaves.SelectedItems.Cast<DataRowView>().ToList();
            if (rows.Count == 0) { MessageBox.Show("Выберите заявки."); return; }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();
                    foreach (var r in rows)
                    {
                        int id = Convert.ToInt32(r.Row["LeaveId"]);
                        using (var cmd = new SqlCommand("UPDATE dbo.Leaves SET Status=N'Approved' WHERE LeaveId=@id", conn))
                        { cmd.Parameters.Add("@id", SqlDbType.Int).Value = id; await cmd.ExecuteNonQueryAsync(); }
                    }
                }
                LoadLeaves();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка утверждения: " + ex.Message); }
        }

        private async void OnLeavesReject(object sender, RoutedEventArgs e)
        {
            var rows = gridLeaves.SelectedItems.Cast<DataRowView>().ToList();
            if (rows.Count == 0) { MessageBox.Show("Выберите заявки."); return; }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();
                    foreach (var r in rows)
                    {
                        int id = Convert.ToInt32(r.Row["LeaveId"]);
                        using (var cmd = new SqlCommand("UPDATE dbo.Leaves SET Status=N'Rejected' WHERE LeaveId=@id", conn))
                        { cmd.Parameters.Add("@id", SqlDbType.Int).Value = id; await cmd.ExecuteNonQueryAsync(); }
                    }
                }
                LoadLeaves();
            }
            catch (Exception ex) { MessageBox.Show("Ошибка отклонения: " + ex.Message); }
        }

        private void LoadScheduleData()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            {
                using (var da = new SqlDataAdapter("SELECT EmployeeId, FullName FROM dbo.Employees ORDER BY FullName", conn))
                {
                    var dt = new DataTable();
                    da.Fill(dt);
                    cbScheduleEmployee.ItemsSource = dt.DefaultView;
                }

                using (var da = new SqlDataAdapter("SELECT ShiftId, Name FROM dbo.Shifts ORDER BY Name", conn))
                {
                    var dt = new DataTable();
                    da.Fill(dt);
                    cbScheduleShift.ItemsSource = dt.DefaultView;
                }

                using (var da = new SqlDataAdapter(@"
                    SELECT s.ScheduleId, e.FullName, s.WorkDate, sh.Name as ShiftName, 
                           s.PlannedStart, s.PlannedEnd, s.Status
                    FROM dbo.Schedules s
                    JOIN dbo.Employees e ON e.EmployeeId = s.EmployeeId
                    LEFT JOIN dbo.Shifts sh ON sh.ShiftId = s.ShiftId
                    WHERE s.WorkDate >= DATEADD(day, -30, GETDATE())
                    ORDER BY s.WorkDate DESC", conn))
                {
                    var dt = new DataTable();
                    da.Fill(dt);
                    gridSchedules.ItemsSource = dt.DefaultView;
                }
            }
        }

        private async void OnScheduleAdd(object sender, RoutedEventArgs e)
        {
            if (cbScheduleEmployee.SelectedValue == null || dpScheduleDate.SelectedDate == null)
            {
                MessageBox.Show("Выберите сотрудника и дату.");
                return;
            }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(@"
                    INSERT INTO dbo.Schedules (EmployeeId, WorkDate, ShiftId, PlannedStart, PlannedEnd, Status)
                    VALUES (@empId, @date, @shiftId, @start, @end, N'Planned')", conn))
                {
                    cmd.Parameters.Add("@empId", SqlDbType.Int).Value = cbScheduleEmployee.SelectedValue;
                    cmd.Parameters.Add("@date", SqlDbType.Date).Value = dpScheduleDate.SelectedDate.Value.Date;
                    cmd.Parameters.Add("@shiftId", SqlDbType.Int).Value = cbScheduleShift.SelectedValue ?? DBNull.Value;

                    if (string.IsNullOrEmpty(tbPlannedStart.Text))
                    {
                        cmd.Parameters.Add("@start", SqlDbType.Time).Value = DBNull.Value;
                    }
                    else
                    {
                        if (TimeSpan.TryParse(tbPlannedStart.Text, out TimeSpan startTime))
                        {
                            cmd.Parameters.Add("@start", SqlDbType.Time).Value = startTime;
                        }
                        else
                        {
                            MessageBox.Show("Неверный формат времени начала. Используйте формат HH:mm");
                            return;
                        }
                    }

                    if (string.IsNullOrEmpty(tbPlannedEnd.Text))
                    {
                        cmd.Parameters.Add("@end", SqlDbType.Time).Value = DBNull.Value;
                    }
                    else
                    {
                        if (TimeSpan.TryParse(tbPlannedEnd.Text, out TimeSpan endTime))
                        {
                            cmd.Parameters.Add("@end", SqlDbType.Time).Value = endTime;
                        }
                        else
                        {
                            MessageBox.Show("Неверный формат времени окончания. Используйте формат HH:mm");
                            return;
                        }
                    }

                    await conn.OpenAsync();
                    await cmd.ExecuteNonQueryAsync();

                    await CreateNotification(Convert.ToInt32(cbScheduleEmployee.SelectedValue),
                        "NewSchedule", $"Вам назначена смена на {dpScheduleDate.SelectedDate.Value:dd.MM.yyyy}");

                    MessageBox.Show("График добавлен.");
                    LoadScheduleData();

                    tbPlannedStart.Clear();
                    tbPlannedEnd.Clear();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка добавления графика: " + ex.Message);
            }
        }

        private void OnBulkSchedule(object sender, RoutedEventArgs e)
        {
            var bulkWindow = new BulkScheduleWindow();
            if (bulkWindow.ShowDialog() == true)
            {
                LoadScheduleData();
            }
        }

        private async void OnGenerateReport(object sender, RoutedEventArgs e)
        {
            if (dpReportStart.SelectedDate == null || dpReportEnd.SelectedDate == null)
            {
                MessageBox.Show("Выберите период для отчета.");
                return;
            }

            var reportTypeItem = cbReportType.SelectedItem as ComboBoxItem;
            if (reportTypeItem == null)
            {
                MessageBox.Show("Выберите тип отчета.");
                return;
            }

            var reportType = reportTypeItem.Content.ToString();
            var cs = GetCS();

            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();
                    DataTable dt = new DataTable();

                    switch (reportType)
                    {
                        case "Посещаемость по отделам":
                            using (var cmd = new SqlCommand(@"
                                SELECT 
                                    d.Name as [Отдел],
                                    COUNT(DISTINCT e.EmployeeId) as [Всего сотрудников],
                                    SUM(CASE WHEN a.CheckIn IS NOT NULL THEN 1 ELSE 0 END) as [Отметившихся],
                                    CAST(SUM(CASE WHEN a.CheckIn IS NOT NULL THEN 1 ELSE 0 END) * 100.0 / NULLIF(COUNT(DISTINCT e.EmployeeId), 0) as decimal(5,2)) as [Процент, %]
                                FROM dbo.Departments d
                                LEFT JOIN dbo.Employees e ON e.DepartmentId = d.DepartmentId
                                LEFT JOIN dbo.Attendance a ON a.EmployeeId = e.EmployeeId 
                                    AND a.WorkDate BETWEEN @start AND @end
                                GROUP BY d.DepartmentId, d.Name
                                ORDER BY d.Name", conn))
                            {
                                cmd.Parameters.Add("@start", SqlDbType.Date).Value = dpReportStart.SelectedDate.Value;
                                cmd.Parameters.Add("@end", SqlDbType.Date).Value = dpReportEnd.SelectedDate.Value;

                                using (var da = new SqlDataAdapter(cmd))
                                {
                                    da.Fill(dt);
                                }
                            }
                            break;

                        case "Опоздания":
                            using (var cmd = new SqlCommand(@"
                                SELECT 
                                    e.FullName as [Сотрудник], 
                                    d.Name as [Отдел],
                                    a.WorkDate as [Дата], 
                                    CONVERT(varchar(5), a.CheckIn, 108) as [Время прихода],
                                    CONVERT(varchar(5), s.PlannedStart, 108) as [Плановое время],
                                    DATEDIFF(MINUTE, s.PlannedStart, a.CheckIn) as [Опоздание, мин]
                                FROM dbo.Attendance a
                                JOIN dbo.Employees e ON e.EmployeeId = a.EmployeeId
                                LEFT JOIN dbo.Departments d ON d.DepartmentId = e.DepartmentId
                                LEFT JOIN dbo.Schedules s ON s.EmployeeId = a.EmployeeId AND s.WorkDate = a.WorkDate
                                WHERE a.WorkDate BETWEEN @start AND @end
                                  AND a.CheckIn > s.PlannedStart
                                  AND s.PlannedStart IS NOT NULL
                                ORDER BY a.WorkDate DESC", conn))
                            {
                                cmd.Parameters.Add("@start", SqlDbType.Date).Value = dpReportStart.SelectedDate.Value;
                                cmd.Parameters.Add("@end", SqlDbType.Date).Value = dpReportEnd.SelectedDate.Value;

                                using (var da = new SqlDataAdapter(cmd))
                                {
                                    da.Fill(dt);
                                }
                            }
                            break;

                        case "Отпуска и больничные":
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
                                cmd.Parameters.Add("@start", SqlDbType.Date).Value = dpReportStart.SelectedDate.Value;
                                cmd.Parameters.Add("@end", SqlDbType.Date).Value = dpReportEnd.SelectedDate.Value;

                                using (var da = new SqlDataAdapter(cmd))
                                {
                                    da.Fill(dt);
                                }
                            }
                            break;
                    }

                    gridReports.ItemsSource = dt.DefaultView;
                    gridReports.Columns.Clear();

                    foreach (DataColumn column in dt.Columns)
                    {
                        gridReports.Columns.Add(new DataGridTextColumn
                        {
                            Header = column.ColumnName,
                            Binding = new System.Windows.Data.Binding(column.ColumnName)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка формирования отчета: " + ex.Message);
            }
        }

        private void OnExportExcel(object sender, RoutedEventArgs e)
        {
            var dataView = gridReports.ItemsSource as DataView;
            if (dataView == null || dataView.Table.Rows.Count == 0)
            {
                MessageBox.Show("Нет данных для экспорта.");
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"Отчет_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using (var writer = new StreamWriter(saveDialog.FileName, false, Encoding.UTF8))
                    {
                        var headers = dataView.Table.Columns.Cast<DataColumn>()
                            .Select(col => EscapeCsv(col.ColumnName));
                        writer.WriteLine(string.Join(";", headers));

                        foreach (DataRowView row in dataView)
                        {
                            var values = dataView.Table.Columns.Cast<DataColumn>()
                                .Select(col => EscapeCsv(row[col.ColumnName]?.ToString() ?? ""));
                            writer.WriteLine(string.Join(";", values));
                        }
                    }
                    MessageBox.Show("Отчет экспортирован успешно.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка экспорта: " + ex.Message);
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

        private async Task CreateNotification(int employeeId, string type, string message)
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Notifications' AND xtype='U')
                CREATE TABLE dbo.Notifications (
                    NotificationId INT IDENTITY(1,1) PRIMARY KEY,
                    EmployeeId INT NOT NULL,
                    Type NVARCHAR(50) NOT NULL,
                    Message NVARCHAR(500) NOT NULL,
                    IsRead BIT NOT NULL DEFAULT 0,
                    CreatedDate DATETIME2 NOT NULL DEFAULT GETDATE()
                );
                
                INSERT INTO dbo.Notifications (EmployeeId, Type, Message, IsRead, CreatedDate)
                VALUES (@empId, @type, @message, 0, GETDATE())", conn))
            {
                cmd.Parameters.Add("@empId", SqlDbType.Int).Value = employeeId;
                cmd.Parameters.Add("@type", SqlDbType.NVarChar, 50).Value = type;
                cmd.Parameters.Add("@message", SqlDbType.NVarChar, 500).Value = message;

                await conn.OpenAsync();
                await cmd.ExecuteNonQueryAsync();
            }
        }
        private void LoadNotificationsForAdmin()
        {
            try
            {
                var cs = GetCS();
                using (var conn = new SqlConnection(cs))
                using (var da = new SqlDataAdapter(@"
            SELECT 
                n.NotificationId,
                e.FullName,
                n.Type,
                n.Message,
                n.IsRead,
                n.CreatedDate
            FROM dbo.Notifications n
            LEFT JOIN dbo.Employees e ON e.EmployeeId = n.EmployeeId
            ORDER BY n.CreatedDate DESC", conn))
                {
                    var dt = new DataTable();
                    da.Fill(dt);
                    gridNotificationsAdmin.ItemsSource = dt.DefaultView;

                    int total = dt.Rows.Count;
                    int read = dt.AsEnumerable()
                        .Where(r => r["IsRead"] != DBNull.Value)
                        .Count(r => Convert.ToBoolean(r["IsRead"]));
                    int unread = total - read;

                    txtNotificationsInfoAdmin.Text = $"Всего: {total} | ✓ Прочитано: {read} | ✗ Не прочитано: {unread}";
                }
            }
            catch (SqlException sqlEx)
            {
                if (sqlEx.Message.Contains("Invalid object name 'Notifications'"))
                {
                    txtNotificationsInfoAdmin.Text = "Нет данных об уведомлениях";
                    gridNotificationsAdmin.ItemsSource = null;
                }
                else
                {
                    MessageBox.Show("Ошибка загрузки уведомлений: " + sqlEx.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
            }
        }

        private void OnNotificationsRefreshAdmin(object sender, RoutedEventArgs e)
        {
            LoadNotificationsForAdmin();
        }
    }
}