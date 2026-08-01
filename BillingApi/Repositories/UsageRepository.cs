using Npgsql;

namespace BillingApi.Repositories;

public class UsageRepository
{
    private readonly string _connStr;

    public UsageRepository(string connStr) => _connStr = connStr;

    // The token split (input / cached / output) is optional and defaults to 0,
    // so callers that meter a plain count (e.g. "api_call") stay unchanged.
    public bool TryRecord(int tenantId, string usageType, int quantity, string idempotencyKey,
        int inputTokens = 0, int cachedInputTokens = 0, int outputTokens = 0)
    {
        using var conn = new NpgsqlConnection(_connStr);
        conn.Open();

        using var cmd = new NpgsqlCommand("""
            INSERT INTO usage_events (tenant_id, usage_type, quantity, idempotency_key,
                                     input_tokens, cached_input_tokens, output_tokens)
            VALUES (@tid, @type, @qty, @key, @in, @cached, @out)
            ON CONFLICT (tenant_id, idempotency_key) DO NOTHING
            """, conn);

        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("type", usageType);
        cmd.Parameters.AddWithValue("qty", quantity);
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        cmd.Parameters.AddWithValue("in", inputTokens);
        cmd.Parameters.AddWithValue("cached", cachedInputTokens);
        cmd.Parameters.AddWithValue("out", outputTokens);

        var rows = cmd.ExecuteNonQuery();
        return rows > 0;
    }

    // Sums the three token cost categories for the current month, so /usage can
    // price cached input cheaper and treat reasoning as part of output.
    public (long inputTokens, long cachedInputTokens, long outputTokens) GetMonthlyTokenBreakdown(int tenantId)
    {
        using var conn = new NpgsqlConnection(_connStr);
        conn.Open();

        using var cmd = new NpgsqlCommand("""
            SELECT COALESCE(SUM(input_tokens), 0),
                   COALESCE(SUM(cached_input_tokens), 0),
                   COALESCE(SUM(output_tokens), 0)
            FROM usage_events
            WHERE tenant_id = @tid
              AND created_at >= date_trunc('month', NOW())
            """, conn);

        cmd.Parameters.AddWithValue("tid", tenantId);

        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }


    public Dictionary<string, long> GetMonthlyUsage(int tenantId)
    {
        using var conn = new NpgsqlConnection(_connStr);
        conn.Open();

        using var cmd = new NpgsqlCommand("""
            SELECT usage_type, COALESCE(SUM(quantity), 0)
            FROM usage_events
            WHERE tenant_id = @tid
              AND created_at >= date_trunc('month', NOW())
            GROUP BY usage_type
            """, conn);

        cmd.Parameters.AddWithValue("tid", tenantId);

        // Dictionary: "api_call" → 150, "token" → 45000
        var result = new Dictionary<string, long>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }

        return result;
    }
}