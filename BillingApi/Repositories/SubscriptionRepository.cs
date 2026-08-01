using Npgsql;
namespace BillingApi.Repositories;

public class SubscriptionRepository
{
    private readonly string _connStr;
    public SubscriptionRepository(string connStr) => _connStr = connStr;

    public void Upsert(int tenantId, string stripeSubscriptionId, string stripeEventId, string status)
    {
        using var conn = new NpgsqlConnection(_connStr);
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO subscriptions (tenant_id, stripe_subscription_id, stripe_event_id, status, updated_at)
            VALUES (@tenantId, @subId, @eventId, @status, now())
            ON CONFLICT (stripe_subscription_id)
            DO UPDATE SET status = @status, stripe_event_id = @eventId, updated_at = now()",
            conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("subId", stripeSubscriptionId);
        cmd.Parameters.AddWithValue("eventId", (object?)stripeEventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("status", status);
        cmd.ExecuteNonQuery();
    }

    public int? UpdateStatusBySubscriptionId(string stripeSubscriptionId, string status, string stripeEventId)
    {
        using var conn = new NpgsqlConnection(_connStr);
        conn.Open();
        using var cmd = new NpgsqlCommand(@"
            UPDATE subscriptions
            SET status = @status, stripe_event_id = @eventId, updated_at = now()
            WHERE stripe_subscription_id = @subId
            RETURNING tenant_id",
            conn);
        cmd.Parameters.AddWithValue("subId", stripeSubscriptionId);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("eventId", stripeEventId);
        var result = cmd.ExecuteScalar();
        return result is null ? null : Convert.ToInt32(result);
    }
}