using BillingApi.Models;
using Npgsql;

namespace BillingApi.Repositories
{
    public class TenantRepository
    {
        private readonly string _connStr;

        public TenantRepository(string connStr) => _connStr = connStr;

        public Tenant? GetByEmail(string email)
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();


            using var cmd = new NpgsqlCommand(
                "SELECT id, email, password_hash, plan_id, stripe_customer_id FROM tenants WHERE email = @e", conn);

            cmd.Parameters.AddWithValue("e", email);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new Tenant
            {
                Id = reader.GetInt32(0),
                Email = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                PlanId = reader.GetInt32(3),
                StripeCustomerId = reader.IsDBNull(4) ? null : reader.GetString(4)
            };
        }

        public Tenant Create(string email, string passwordHash)
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                "INSERT INTO tenants (email, password_hash) VALUES (@e, @p) RETURNING id, plan_id", conn);
            cmd.Parameters.AddWithValue("e", email);
            cmd.Parameters.AddWithValue("p", passwordHash);

            using var reader = cmd.ExecuteReader();
            reader.Read();

            return new Tenant
            {
                Id = reader.GetInt32(0),
                Email = email,
                PasswordHash = passwordHash,
                PlanId = reader.GetInt32(1)
            };
        }

        public void UpdatePlan(int tenantId, int planId)
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                "UPDATE tenants SET plan_id = @p WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("p", planId);
            cmd.Parameters.AddWithValue("id", tenantId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateStatus(int tenantId, string status)
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();
            using var cmd = new NpgsqlCommand(
                "UPDATE tenants SET subscription_status = @status WHERE id = @tenantId", conn);
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateStripeCustomerId(int tenantId, string customerId)
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                "UPDATE tenants SET stripe_customer_id = @c WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("c", customerId);
            cmd.Parameters.AddWithValue("id", tenantId);
            cmd.ExecuteNonQuery();
        }

        public Tenant? GetById(int id)
        {
            using var conn = new NpgsqlConnection(_connStr);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                "SELECT id, email, password_hash, plan_id, stripe_customer_id FROM tenants WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new Tenant
            {
                Id = reader.GetInt32(0),
                Email = reader.GetString(1),
                PasswordHash = reader.GetString(2),
                PlanId = reader.GetInt32(3),
                StripeCustomerId = reader.IsDBNull(4) ? null : reader.GetString(4)
            };
        }
    }
}
