using System;
using System.Security.Cryptography;

namespace WorkSched
{
    public static class Passwords
    {
        public static string Hash(string password, int iterations = 100_000)
        {
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations))
            {
                var hash = pbkdf2.GetBytes(32);
                return $"PBKDF2${{iterations}}${{Convert.ToBase64String(salt)}}${{Convert.ToBase64String(hash)}}";
            }
        }

        public static bool Verify(string password, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return false;
            var parts = stored.Split('$');
            if (parts.Length == 4 && parts[0] == "PBKDF2" && int.TryParse(parts[1], out var iterations))
            {
                try
                {
                    var salt = Convert.FromBase64String(parts[2]);
                    var expected = Convert.FromBase64String(parts[3]);
                    using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations))
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
            return password == stored;
        }

        public static bool IsPlain(string stored) => !(stored?.StartsWith("PBKDF2$") ?? false);
    }
}
