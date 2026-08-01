namespace BillingApi.Models;

public class Tenant
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int PlanId { get; set; }
    public string? StripeCustomerId { get; set; }
}