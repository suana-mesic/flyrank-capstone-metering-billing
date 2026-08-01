using Stripe;

namespace BillingApi.Tests;

public class WebhookSignatureTests
{
    [Fact]
    public void ForgedWebhook_WithInvalidSignature_IsRejected()
    {
        // Same mechanism your /webhooks/stripe endpoint uses.
        // A forged signature MUST throw StripeException — it must never be processed.
        var payload = "{\"id\":\"evt_fake\",\"type\":\"checkout.session.completed\"}";
        var forgedSignature = "t=123,v1=forgedsignature";
        var webhookSecret = "whsec_testsecret";

        Assert.Throws<StripeException>(() =>
            EventUtility.ConstructEvent(
                payload, forgedSignature, webhookSecret, throwOnApiVersionMismatch: false));
    }
}