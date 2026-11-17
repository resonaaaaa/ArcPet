using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ArcPet.Behavior
{
    public static class DailyFortune
    {
        private static readonly string[] Results = { "大吉", "吉", "中吉", "小吉", "末吉", "凶", "大凶" };

        // Use the computer's local calendar rather than an elapsed 24-hour timer.
        public static DateTime DayFor(DateTime localTime)
        {
            return localTime.TimeOfDay < TimeSpan.FromHours(6) ? localTime.Date.AddDays(-1) : localTime.Date;
        }

        public static string ResultFor(DateTime localTime)
        {
            // A versioned date key keeps the draw stable across app restarts.
            string key = "ArcPet daily fortune 1:" + DayFor(localTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using (var hash = SHA256.Create())
            {
                foreach (byte value in hash.ComputeHash(Encoding.UTF8.GetBytes(key)))
                {
                    // Reject the four leftover values so all seven results have equal weight.
                    if (value < 252) return Results[value % Results.Length];
                }
            }
            return Results[0];
        }
    }
}
