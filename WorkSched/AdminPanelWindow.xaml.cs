using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Windows;

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
            Loaded += (s, e) => { LoadUsers(); LoadShifts(); LoadLeaves(); };
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
            var rows = gridUsers.SelectedItems.Cast<System.Data.DataRowView>().ToList();
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
            var row = gridUsers.SelectedItem as System.Data.DataRowView;
            if (row == null) { MessageBox.Show("Выберите пользователя."); return; }

            var cbi = cbRole.SelectedItem as System.Windows.Controls.ComboBoxItem;
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
            var row = gridUsers.SelectedItem as System.Data.DataRowView;
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

        // ===== SHIFTS =====
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

        private void OnShiftSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var row = gridShifts.SelectedItem as System.Data.DataRowView;
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
            var row = gridShifts.SelectedItem as System.Data.DataRowView;
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
            var row = gridShifts.SelectedItem as System.Data.DataRowView;
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

        // ===== LEAVES =====
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
            var rows = gridLeaves.SelectedItems.Cast<System.Data.DataRowView>().ToList();
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
            var rows = gridLeaves.SelectedItems.Cast<System.Data.DataRowView>().ToList();
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
    }
}
