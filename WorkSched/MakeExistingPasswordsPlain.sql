-- ВНИМАНИЕ: только для отладки!
-- Обновить пароль конкретного пользователя на простой '1234'
UPDATE dbo.Employees SET PasswordHash = N'1234' WHERE Login = N'login_here';

-- Пример для админа из сида:
-- UPDATE dbo.Employees SET PasswordHash = N'admin123' WHERE Login = N'admin';
