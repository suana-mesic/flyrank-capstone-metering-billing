using Npgsql;

namespace BillingApi.Database
{
    public static class DatabaseInitializer
    {
        public static void Initialize(string connectionString)
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();

            var sql = File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "Database", "init.sql"));

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.ExecuteNonQuery();

            Console.WriteLine("Database initialized.");
        }
    }
}
