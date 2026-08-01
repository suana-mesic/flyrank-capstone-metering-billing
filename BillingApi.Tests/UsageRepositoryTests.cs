using BillingApi.Repositories;
using Npgsql;

namespace BillingApi.Tests;

public class UsageRepositoryTests
{
    // Points at the same test Postgres (docker, port 5434). Adjust if your .env differs.
    private const string ConnStr =
        "Host=localhost;Port=5434;Username=billing_user;Password=billing_pass;Database=billingdb";

    [Fact]
    public void TryRecord_WithSameIdempotencyKey_RecordsUsageOnlyOnce()
    {
        var repo = new UsageRepository(ConnStr);
        var key = "test-" + Guid.NewGuid();   // unique key per test run
        const int tenantId = 1;                // seeded tenant

        try
        {
            // First call: brand new key -> must insert (true)
            var first = repo.TryRecord(tenantId, "api_call", 1, key);
            // Second call: same key (a retry) -> must be ignored (false)
            var second = repo.TryRecord(tenantId, "api_call", 1, key);

            Assert.True(first);
            Assert.False(second);

            // And the DB must contain exactly ONE row for this key.
            Assert.Equal(1, CountRowsForKey(key));
        }
        finally
        {
            CleanupKey(key); // keep the test DB clean
        }
    }

    private static int CountRowsForKey(string key)
    {
        using var conn = new NpgsqlConnection(ConnStr);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM usage_events WHERE idempotency_key = @k", conn);
        cmd.Parameters.AddWithValue("k", key);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void CleanupKey(string key)
    {
        using var conn = new NpgsqlConnection(ConnStr);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "DELETE FROM usage_events WHERE idempotency_key = @k", conn);
        cmd.Parameters.AddWithValue("k", key);
        cmd.ExecuteNonQuery();
    }
}