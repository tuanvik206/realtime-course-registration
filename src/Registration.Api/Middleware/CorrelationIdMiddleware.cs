namespace Registration.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemName = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = Guid.TryParse(incoming, out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();
        context.Items[ItemName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();
        await next(context);
    }

    public static Guid Get(HttpContext context) => context.Items[ItemName] is Guid id ? id : Guid.NewGuid();
}
