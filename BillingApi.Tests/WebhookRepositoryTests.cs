using BillingApi.Repositories;
using Npgsql;
using Xunit;

namespace BillingApi.Tests;

public class WebhookRepositoryTests
{
    private const string ConnStr =
        "Host=localhost;Port=5434;Username=billing_user;Password=billing_pass;Database=billingdb";

    [Fact]
    public void TryMarkProcessed_WithSameEventId_IsProcessedOnlyOnce()
    {
        var repo = new WebhookRepository(ConnStr);
        var eventId = "evt_test_" + Guid.NewGuid();

        try
        {
            // First time we see this event -> new (true), should be processed
            var first = repo.TryMarkProcessed(eventId);
            // Same event again (Stripe retry) -> duplicate (false), must be ignored
            var second = repo.TryMarkProcessed(eventId);

            Assert.True(first);
            Assert.False(second);
        }
        finally
        {
            Cleanup(eventId);
        }
    }

    private static void Cleanup(string eventId)
    {
        using var conn = new NpgsqlConnection(ConnStr);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "DELETE FROM processed_webhook_events WHERE stripe_event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        cmd.ExecuteNonQuery();
    }
}