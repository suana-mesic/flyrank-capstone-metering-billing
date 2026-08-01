using Npgsql;

namespace BillingApi.Repositories
{
    public class WebhookRepository
    {
        private readonly string _connStr;
        public WebhookRepository(string connStr) => _connStr = connStr;

        public bool TryMarkProcessed(string stripeEventId)
        {

            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();
            using var cmd = new NpgsqlCommand(
                "INSERT INTO processed_webhook_events (stripe_event_id) VALUES (@id) ON CONFLICT DO NOTHING",
                conn);
            cmd.Parameters.AddWithValue("id", stripeEventId);
            var rowsAffected = cmd.ExecuteNonQuery();
            return rowsAffected > 0; // 0 = već postojao = duplikat
        }
    }
}
