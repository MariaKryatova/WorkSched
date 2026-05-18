using Microsoft.Win32;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorkSched.Facades;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkSched
{
    public partial class AdminPanelWindow : Window
    {
        private readonly int _adminId;
        private readonly string _adminName;
        private readonly NotificationFacade _notificationFacade = new NotificationFacade();
        private ReportBuilder _currentReportBuilder;
        private DataTable _currentReportData;

        private AIClient _aiClient;
        private List<PredictionViewModel> _predictions;

        public AdminPanelWindow(int adminId, string adminName)
        {
            InitializeComponent();
            _adminId = adminId;
            _adminName = adminName;
            Title = "Админ-панель — " + _adminName;

            _aiClient = new AIClient();
            _predictions = new List<PredictionViewModel>();

            dpScheduleDate.SelectedDate = DateTime.Today;

            if (cbPredictionYear != null)
            {
                cbPredictionYear.SelectedIndex = 1; 
            }

            Loaded += async (s, e) =>
            {
                LoadUsers();
                LoadShifts();
                LoadLeaves();
                LoadScheduleData();
                LoadNotificationsForAdmin();
                await CheckAIConnection();
            };
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

                    await _notificationFacade.NotifyEmployeeAsync(
                     Convert.ToInt32(cbScheduleEmployee.SelectedValue),
                     "NewSchedule",
                     $"Вам назначена смена на {dpScheduleDate.SelectedDate.Value:dd.MM.yyyy}"
                );

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

        private void OnBuilderBuildReport(object sender, RoutedEventArgs e)
        {
            if (dpBuilderStart.SelectedDate == null || dpBuilderEnd.SelectedDate == null)
            {
                txtBuilderResult.Text = "Ошибка: Выберите период";
                return;
            }

            var reportTypeItem = cbBuilderReportType.SelectedItem as ComboBoxItem;
            if (reportTypeItem == null)
            {
                txtBuilderResult.Text = "Ошибка: Выберите тип отчета";
                return;
            }

            string reportType = reportTypeItem.Content.ToString();
            ReportType builderReportType;

            if (reportType == "Посещаемость по отделам")
                builderReportType = ReportType.DepartmentAttendance;
            else
                builderReportType = ReportType.LeavesAndSick;

            try
            {
                _currentReportBuilder = new ReportBuilder()
                    .SetReportType(builderReportType)
                    .SetDateRange(dpBuilderStart.SelectedDate.Value, dpBuilderEnd.SelectedDate.Value)
                    .BuildData();

                _currentReportData = _currentReportBuilder.GetDataTableResult();

                gridBuilderReport.ItemsSource = _currentReportData.DefaultView;
                gridBuilderReport.AutoGenerateColumns = true;

                txtBuilderResult.Text = $"✓ Отчет построен успешно!\n" +
                                       $"Тип: {reportType}\n" +
                                       $"Период: {dpBuilderStart.SelectedDate.Value:dd.MM.yyyy} - {dpBuilderEnd.SelectedDate.Value:dd.MM.yyyy}\n" +
                                       $"Записей: {_currentReportData.Rows.Count}";
            }
            catch (Exception ex)
            {
                txtBuilderResult.Text = $"Ошибка построения отчета: {ex.Message}";
            }
        }


        private void OnBuilderGenerateCsv(object sender, RoutedEventArgs e)
        {
            if (_currentReportBuilder == null || _currentReportData == null)
            {
                txtBuilderResult.Text = "Ошибка: Сначала постройте отчет";
                return;
            }

            try
            {
                _currentReportBuilder.GenerateCsv();
                string csvData = _currentReportBuilder.GetCsvResult();

                string preview = csvData.Length > 500
                    ? csvData.Substring(0, 500) + "...\n\n[Показаны первые 500 символов]"
                    : csvData;

                txtBuilderResult.Text = $"📋 CSV сгенерирован:\n" +
                                       $"Длина: {csvData.Length} символов\n" +
                                       $"Строк: {_currentReportData.Rows.Count + 1}\n\n" +
                                       $"Предпросмотр:\n{preview}";
            }
            catch (Exception ex)
            {
                txtBuilderResult.Text = $"Ошибка генерации CSV: {ex.Message}";
            }
        }

        private void OnBuilderExportToFile(object sender, RoutedEventArgs e)
        {
            if (_currentReportBuilder == null || _currentReportData == null)
            {
                txtBuilderResult.Text = "Ошибка: Сначала постройте отчет";
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt",
                FileName = $"Отчет_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    _currentReportBuilder.ExportToFile(saveDialog.FileName);

                    txtBuilderResult.Text = $"✓ Отчет экспортирован успешно!\n" +
                                           $"Файл: {saveDialog.FileName}\n" +
                                           $"Размер: {new FileInfo(saveDialog.FileName).Length} байт";
                }
                catch (Exception ex)
                {
                    txtBuilderResult.Text = $"Ошибка экспорта: {ex.Message}";
                }
            }
        }
    
        private void NumberOnlyPreview(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text[0]);
        }

  
        private void tbStart_TextChanged(object sender, TextChangedEventArgs e)
        {
            AutoFormatTime(sender as TextBox);
        }

        private void tbEnd_TextChanged(object sender, TextChangedEventArgs e)
        {
            AutoFormatTime(sender as TextBox);
        }

        private void AutoFormatTime(TextBox textBox)
        {
            if (textBox == null) return;

            string digits = new string(textBox.Text.Where(char.IsDigit).ToArray());

            if (digits.Length == 4)
            {
                string hours = digits.Substring(0, 2);
                string minutes = digits.Substring(2, 2);

                int h = int.Parse(hours);
                int m = int.Parse(minutes);

                if (h > 23) h = 23;
                if (m > 59) m = 59;

                string formatted = $"{h:00}:{m:00}";

                if (textBox.Text != formatted)
                {
                    int caretPos = textBox.CaretIndex;
                    textBox.Text = formatted;
                    textBox.CaretIndex = Math.Min(caretPos, formatted.Length);
                }
            }
        }

      
        private void tbBreak_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            string digits = new string(textBox.Text.Where(char.IsDigit).ToArray());

            if (digits.Length > 2) digits = digits.Substring(0, 2);

            if (digits.Length > 0 && int.TryParse(digits, out int value) && value > 60)
                digits = "60";

            if (digits != textBox.Text)
            {
                int caretPos = textBox.CaretIndex;
                textBox.Text = digits;
                textBox.CaretIndex = Math.Min(caretPos, digits.Length);
            }
        }

        private async void OnNotifyAll(object sender, RoutedEventArgs e)
        {
            await _notificationFacade.NotifyAllAsync(
                "System",
                "Плановые изменения графика. Проверьте расписание."
            );

            txtFacadeInfo.Text = "✓ Уведомление отправлено всем сотрудникам";
        }

        private async void OnShowNotificationStats(object sender, RoutedEventArgs e)
        {
            var stats = await _notificationFacade.GetStatsAsync();

            txtFacadeInfo.Text =
                $"Всего уведомлений: {stats.Item1}\n" +
                $"Прочитано: {stats.Item2}\n" +
                $"Не прочитано: {stats.Item3}";
        }


        private async Task CheckAIConnection()
        {
            try
            {
                if (_aiClient == null)
                {
                    txtAIConnectionStatus.Text = "⚠️ ИИ-клиент не инициализирован";
                    txtAIConnectionStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                    return;
                }

                var isHealthy = await _aiClient.IsHealthyAsync();
                if (isHealthy)
                {
                    txtAIConnectionStatus.Text = "✅ ИИ-сервис подключен";
                    txtAIConnectionStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                }
                else
                {
                    txtAIConnectionStatus.Text = "⚠️ ИИ-сервис недоступен (проверьте, запущен ли python ai_service.py)";
                    txtAIConnectionStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                }
            }
            catch (Exception ex)
            {
                txtAIConnectionStatus.Text = $"❌ Ошибка подключения: {ex.Message}";
                txtAIConnectionStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private async void OnPredictAll(object sender, RoutedEventArgs e)
        {
            var yearItem = cbPredictionYear.SelectedItem as ComboBoxItem;
            if (yearItem == null)
            {
                txtPredictionSummary.Text = "⚠️ Выберите год прогноза";
                return;
            }

            int year = int.Parse(yearItem.Content.ToString());

            txtPredictionSummary.Text = "⏳ Загрузка прогнозов... Пожалуйста, подождите.";

            try
            {
                if (!await _aiClient.IsHealthyAsync())
                {
                    txtPredictionSummary.Text = "❌ ИИ-сервис недоступен. Убедитесь, что запущен python ai_service.py";
                    return;
                }

                var employees = await GetAllEmployeesAsync();
                var employeeIds = employees.Select(emp => emp.EmployeeId).ToList();

                if (employeeIds.Count == 0)
                {
                    txtPredictionSummary.Text = "⚠️ Нет сотрудников для прогноза";
                    return;
                }

                var result = await _aiClient.PredictBatchAsync(employeeIds, year);

                _predictions = new List<PredictionViewModel>();
                foreach (var pred in result.results)
                {
                    var employee = employees.FirstOrDefault(emp => emp.EmployeeId == pred.employee_id);
                    _predictions.Add(new PredictionViewModel
                    {
                        EmployeeId = pred.employee_id,
                        FullName = employee?.FullName ?? "Неизвестно",
                        Department = employee?.Department ?? "",
                        PredictedVacationDays = pred.predicted_vacation_days,
                        PredictedSickDays = pred.predicted_sick_days,
                        PredictedTotalDays = pred.predicted_total_days,
                        RiskCategory = pred.risk_category,
                        RiskScore = pred.risk_score,
                        RecommendationsText = string.Join("; ", pred.recommendations)
                    });
                }

                gridPredictions.ItemsSource = _predictions;

                var highRisk = _predictions.Count(p => p.RiskCategory == "Высокий");
                var mediumRisk = _predictions.Count(p => p.RiskCategory == "Средний");
                var lowRisk = _predictions.Count(p => p.RiskCategory == "Низкий");
                var avgDays = _predictions.Count > 0 ? _predictions.Average(p => p.PredictedTotalDays) : 0;
                var totalDays = _predictions.Sum(p => p.PredictedTotalDays);

                txtPredictionSummary.Text =
                    $"ПРОГНОЗ НА {year} ГОД\n" +
                    $"Всего сотрудников: {_predictions.Count}\n" +
                    $"Высокий риск (>15 дней): {highRisk} чел.\n" +
                    $"🟠 Средний риск (8-15 дней): {mediumRisk} чел.\n" +
                    $"🟢 Низкий риск (<8 дней): {lowRisk} чел.\n" +
                    $"Среднее количество дней отсутствия: {avgDays:F1} дней\n" +
                    $"Общая нагрузка на год: {totalDays:F0} человеко-дней\n" +
                    $"💡 Рекомендация: {(highRisk > 5 ? "Требуется найм временного персонала" : "Ситуация стабильна")}";
            }
            catch (Exception ex)
            {
                txtPredictionSummary.Text = $"❌ Ошибка: {ex.Message}";
                MessageBox.Show($"Ошибка при получении прогнозов: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<List<EmployeeInfo>> GetAllEmployeesAsync()
        {
            var cs = GetCS();
            var employees = new List<EmployeeInfo>();

            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(@"
                    SELECT e.EmployeeId, e.FullName, ISNULL(d.Name, 'Нет отдела') as Department
                    FROM Employees e
                    LEFT JOIN Departments d ON d.DepartmentId = e.DepartmentId
                    WHERE e.Role != 'Admin'
                    ORDER BY e.FullName", conn))
                {
                    await conn.OpenAsync();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            employees.Add(new EmployeeInfo
                            {
                                EmployeeId = reader.GetInt32(0),
                                FullName = reader.GetString(1),
                                Department = reader.GetString(2)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки сотрудников: {ex.Message}", "Ошибка",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return employees;
        }

        private async void OnRefreshPredictions(object sender, RoutedEventArgs e)
        {
            await CheckAIConnection();
            if (_predictions != null && _predictions.Any())
            {
                OnPredictAll(sender, new RoutedEventArgs());
            }
        }
    }

    public class EmployeeInfo
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }
    }

    public class PredictionViewModel
    {
        public int EmployeeId { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }
        public float PredictedVacationDays { get; set; }
        public float PredictedSickDays { get; set; }
        public float PredictedTotalDays { get; set; }
        public string RiskCategory { get; set; }
        public float RiskScore { get; set; }
        public string RecommendationsText { get; set; }
    }
}
