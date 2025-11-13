using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Windows;

namespace WorkSched
{
    public partial class RegistrationWindow : Window
    {
        public RegistrationWindow()
        {
            InitializeComponent();
            LoadDepartments();
        }

        private static string GetCS() =>
            ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
            ?? Properties.Settings.Default.WorkSchedConnectionString
            ?? throw new InvalidOperationException("Не найдена строка подключения 'WorkSchedConnectionString'.");

        private void LoadDepartments()
        {
            var cs = GetCS();
            using (var conn = new SqlConnection(cs))
            using (var da = new SqlDataAdapter("SELECT DepartmentId, Name FROM dbo.Departments ORDER BY Name", conn))
            {
                var dt = new DataTable();
                da.Fill(dt);
                cbDept.ItemsSource = dt.DefaultView;
            }
        }

        private async void OnCreate(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text?.Trim();
            string fullName = txtFullName.Text?.Trim();
            string password = pwd1.Password;
            string confirm = pwd2.Password;

            if (string.IsNullOrWhiteSpace(login) ||
                string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Заполните все поля.");
                return;
            }
            if (!string.Equals(password, confirm, StringComparison.Ordinal))
            {
                MessageBox.Show("Пароли не совпадают.");
                return;
            }

            int? deptId = null;
            if (cbDept.SelectedValue != null && int.TryParse(cbDept.SelectedValue.ToString(), out var id))
                deptId = id;

            try
            {
                var cs = GetCS();
                using (var conn = new SqlConnection(cs))
                {
                    await conn.OpenAsync();

                    using (var check = new SqlCommand("SELECT COUNT(1) FROM dbo.Employees WHERE Login=@l", conn))
                    {
                        check.Parameters.Add("@l", SqlDbType.NVarChar, 50).Value = login;
                        if ((int)await check.ExecuteScalarAsync() > 0)
                        {
                            MessageBox.Show("Пользователь с таким логином уже существует.");
                            return;
                        }
                    }

                    using (var cmd = new SqlCommand(@"
                        INSERT INTO dbo.Employees(Login, PasswordHash, FullName, Role, DepartmentId)
                        VALUES(@l, @p, @f, N'Employee', @d)", conn))
                    {
                        cmd.Parameters.Add("@l", SqlDbType.NVarChar, 50).Value  = login;
                        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 200).Value = password;  // plain
                        cmd.Parameters.Add("@f", SqlDbType.NVarChar, 150).Value = fullName;
                        cmd.Parameters.Add("@d", SqlDbType.Int).Value = (object)deptId ?? DBNull.Value;

                        var rows = await cmd.ExecuteNonQueryAsync();
                        if (rows == 1)
                        {
                            MessageBox.Show("Аккаунт создан. Теперь войдите.");
                            Close();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
            }
        }
    }
}
