using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using System.Windows;

namespace WorkSched
{
    public partial class LoginWindow : Window
    {
        public LoginWindow() => InitializeComponent();

        private static string GetCS() =>
            ConfigurationManager.ConnectionStrings["WorkSchedConnectionString"]?.ConnectionString
            ?? Properties.Settings.Default.WorkSchedConnectionString
            ?? throw new InvalidOperationException("Не найдена строка подключения 'WorkSchedConnectionString'.");

        private static readonly Dictionary<string, string> Hardcoded =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["admin"]        = "admin123",
                ["mgr_it"]       = "mgrit!",
                ["ivan.ivanov"]  = "pass1",
                ["mgr_sales"]    = "mgrsales!",
                ["maria.smir"]   = "pass2"
            };

        private async void OnLogin(object sender, RoutedEventArgs e)
        {
            string login = txtLogin.Text?.Trim();
            string password = pwd.Password;

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

                        int id      = r.GetInt32(0);
                        string name = r.GetString(1);
                        string role = r.GetString(2);

                        string stored;
                        object raw = r.GetValue(3);
                        if (raw is string s) stored = s;
                        else if (raw is byte[] bytes) stored = Encoding.Unicode.GetString(bytes);
                        else stored = Convert.ToString(raw) ?? string.Empty;

                        if (Hardcoded.TryGetValue(login, out var expectedPlain))
                        {
                            if (!string.Equals(password, expectedPlain, StringComparison.Ordinal))
                            {
                                MessageBox.Show("Пароль не подошёл.");
                                return;
                            }
                        }
                        else
                        {
                            if (!VerifyFlexible(password, stored))
                            {
                                MessageBox.Show("Пароль не подошёл.");
                                return;
                            }
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

        private static bool VerifyFlexible(string password, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;

            if (stored.StartsWith("PBKDF2$", StringComparison.Ordinal))
            {
                var parts = stored.Split('$');
                if (parts.Length != 4) return false;
                if (!int.TryParse(parts[1], out var iterations)) return false;

                try
                {
                    var salt     = Convert.FromBase64String(parts[2]);
                    var expected = Convert.FromBase64String(parts[3]);
                    using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, iterations))
                    {
                        var actual = pbkdf2.GetBytes(expected.Length);
                        if (actual.Length != expected.Length) return false;
                        int diff = 0;
                        for (int i = 0; i < actual.Length; i++) diff |= actual[i] ^ expected[i];
                        return diff == 0;
                    }
                }
                catch { return false; }
            }
            return string.Equals(password, stored, StringComparison.Ordinal);
        }
    }
}
