using Npgsql;
namespace BillingApi.Repositories;

public sealed record Plan(int Id, string Name, int ApiCallLimit, int TokenLimit);

public class PlanRepository
{
    private readonly string _connStr;
    public PlanRepository(string connStr) => _connStr = connStr;

    public (int apiCallLimit, int tokenLimit)? GetLimits(int planId)
    {
        using var conn = new NpgsqlConnection(_connStr);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT api_call_limit, token_limit FROM plans WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", planId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    // NOVO — za prikaz imena plana u /usage
    public Plan? GetById(int planId)
    {
        using var conn = new NpgsqlConnection(_connStr);
        conn.Open();
        using var cmd = new NpgsqlCommand(
            "SELECT id, name, api_call_limit, token_limit FROM plans WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", planId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Plan(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
    }
}