namespace CoiVerify.Api;

/// <summary>
/// Requires a valid API key on the "X-Api-Key" header. Keys are configured as
/// "ApiKeys:&lt;key&gt;" = "&lt;customer name&gt;" (user-secrets locally, App Service
/// config values once deployed) - the customer name is just for identifying who made
/// a call in logs, not used for anything else yet. This is deliberately simple: a
/// flat set of shared-secret keys, no database, no per-key rate limiting or usage
/// metering. Direct customers get real per-customer keys and Stripe-backed usage
/// tracking later; this is what stands between the endpoints and an open bill today.
/// </summary>
public sealed class ApiKeyAuthFilter(IConfiguration configuration) : IEndpointFilter
{
    private const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var providedKey = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(providedKey) || configuration[$"ApiKeys:{providedKey}"] is null)
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
