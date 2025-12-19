using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Windows;

namespace WorkSched
{
    public partial class LoginWindow : Window
    {
        private bool _passwordVisible;

        public LoginWindow() => InitializeComponent();

        private static string GetCS() =>
            ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
            ?? Properties.Settings.Default.WorkSchedConnectionString
            ?? throw new InvalidOperationException("Не найдена строка подключения 'WorkSchedConnectionString'.");

        private async void OnLogin(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text?.Trim();
            string password = _passwordVisible ? pwdVisible.Text : pwdHidden.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Введите логин и пароль.");
                return;
            }

            try
            {
                var cs = GetCS();
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(@"
                    SELECT TOP(1) EmployeeId, FullName, Role, PasswordHash
                    FROM dbo.Employees WHERE Login=@l
                    ORDER BY EmployeeId DESC;", conn))
                {
                    cmd.Parameters.Add("@l", SqlDbType.NVarChar, 50).Value = login;
                    await conn.OpenAsync();
                    using (var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (!await r.ReadAsync())
                        {
                            MessageBox.Show("Пользователь не найден.");
                            return;
                        }

                        int id = r.GetInt32(0);
                        string name = r.GetString(1);
                        string role = r.GetString(2);

                        string storedHash;
                        object raw = r.GetValue(3);

                        if (raw is string s)
                            storedHash = s;
                        else if (raw is byte[] bytes)
                            storedHash = Encoding.Unicode.GetString(bytes);
                        else
                            storedHash = Convert.ToString(raw) ?? string.Empty;

                        if (!Passwords.Verify(password, storedHash))
                        {
                            MessageBox.Show("Неверный логин или пароль.");
                            return;
                        }

                        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                            new AdminPanelWindow(id, name).Show();
                        else
                            new EmployeeWindow(id, name, role).Show();

                        Close();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message);
            }
        }

        private void OnRegister(object sender, RoutedEventArgs e)
        {
            new RegistrationWindow { Owner = this }.ShowDialog();
        }

        private void OnTogglePassword(object sender, RoutedEventArgs e)
        {
            if (_passwordVisible)
            {
                pwdHidden.Password = pwdVisible.Text;
                pwdHidden.Visibility = Visibility.Visible;
                pwdVisible.Visibility = Visibility.Collapsed;
                pwdHidden.Focus();
            }
            else
            {
                pwdVisible.Text = pwdHidden.Password;
                pwdVisible.Visibility = Visibility.Visible;
                pwdHidden.Visibility = Visibility.Collapsed;
                pwdVisible.Focus();
                pwdVisible.CaretIndex = pwdVisible.Text.Length;
            }

            _passwordVisible = !_passwordVisible;
        }
    }
}
