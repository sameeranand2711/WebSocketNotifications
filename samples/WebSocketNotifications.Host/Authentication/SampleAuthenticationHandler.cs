using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace WebSocketNotifications.Host.Authentication;

/// <summary>
/// Demonstrates authentication by turning a query-string user ID into an authenticated principal.
/// </summary>
/// <remarks>This deliberately insecure scheme is suitable only for a local sample.</remarks>
internal sealed class SampleAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SampleQueryUser";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Query["userId"].ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // The library never interprets the query string itself. It sees only the authenticated
        // principal, and ClaimUserResolver maps that principal to an application user ID.
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
