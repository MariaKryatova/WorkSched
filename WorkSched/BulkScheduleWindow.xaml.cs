using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Windows;

namespace WorkSched
{
    public partial class BulkScheduleWindow : Window
    {
        public BulkScheduleWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => { LoadEmployees(); LoadShifts(); };
            dpBulkStart.SelectedDate = DateTime.Today;
            dpBulkEnd.SelectedDate = DateTime.Today.AddDays(7);
        }

        private static string GetCS() =>
            ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
            ?? Properties.Settings.Default.WorkSchedConnectionString
            ?? throw new InvalidOperationException("Не найдена строка подключения 'WorkSchedConnectionString'.");

        private void LoadEmployees()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var da = new SqlDataAdapter("SELECT EmployeeId, FullName FROM dbo.Employees ORDER BY FullName", conn))
            {
                var dt = new DataTable();
                da.Fill(dt);
                lbEmployees.ItemsSource = dt.DefaultView;
            }
        }

        private void LoadShifts()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var da = new SqlDataAdapter("SELECT ShiftId, Name FROM dbo.Shifts ORDER BY Name", conn))
            {
                var dt = new DataTable();
                da.Fill(dt);
                cbBulkShift.ItemsSource = dt.DefaultView;
            }
        }

        private async void OnBulkCreate(object sender, RoutedEventArgs e)
        {
            if (lbEmployees.SelectedItems.Count == 0 || dpBulkStart.SelectedDate == null ||
                dpBulkEnd.SelectedDate == null || cbBulkShift.SelectedValue == null)
            {
                MessageBox.Show("Заполните все поля.");
                return;
            }

            var selectedDays = new[]
            {
                chkMonday.IsChecked == true ? DayOfWeek.Monday : (DayOfWeek?)null,
                chkTuesday.IsChecked == true ? DayOfWeek.Tuesday : (DayOfWeek?)null,
                chkWednesday.IsChecked == true ? DayOfWeek.Wednesday : (DayOfWeek?)null,
                chkThursday.IsChecked == true ? DayOfWeek.Thursday : (DayOfWeek?)null,
                chkFriday.IsChecked == true ? DayOfWeek.Friday : (DayOfWeek?)null,
                chkSaturday.IsChecked == true ? DayOfWeek.Saturday : (DayOfWeek?)null,
                chkSunday.IsChecked == true ? DayOfWeek.Sunday : (DayOfWeek?)null
            }.Where(d => d.HasValue).Select(d => d.Value).ToList();

            if (selectedDays.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы один день недели.");
                return;
            }

            var cs = GetCS();
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();

                    foreach (DataRowView employee in lbEmployees.SelectedItems)
                    {
                        int employeeId = Convert.ToInt32(employee["EmployeeId"]);

                        for (var date = dpBulkStart.SelectedDate.Value;
                             date <= dpBulkEnd.SelectedDate.Value;
                             date = date.AddDays(1))
                        {
                            if (selectedDays.Contains(date.DayOfWeek))
                            {
                                using (var cmd = new SqlCommand(@"
                                    IF NOT EXISTS (SELECT 1 FROM dbo.Schedules 
                                                 WHERE EmployeeId = @empId AND WorkDate = @date)
                                    INSERT INTO dbo.Schedules (EmployeeId, WorkDate, ShiftId, Status)
                                    VALUES (@empId, @date, @shiftId, N'Planned')", conn))
                                {
                                    cmd.Parameters.Add("@empId", SqlDbType.Int).Value = employeeId;
                                    cmd.Parameters.Add("@date", SqlDbType.Date).Value = date;
                                    cmd.Parameters.Add("@shiftId", SqlDbType.Int).Value = cbBulkShift.SelectedValue;

                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }
                        }
                    }
                }

                MessageBox.Show("Графики успешно созданы.");
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка создания графиков: " + ex.Message);
            }
        }
    }
}