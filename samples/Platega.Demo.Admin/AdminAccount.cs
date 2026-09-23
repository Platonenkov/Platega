using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Platega.Demo.Admin;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Password of the single demo administrator. Login is disabled while it is empty.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Minimal single-user cookie login for the demo admin panel.</summary>
public static class AdminAccount
{
    public static void MapAdminAccount(this IEndpointRouteBuilder app)
    {
        app.MapPost("/account/login", LoginAsync);
        app.MapPost("/account/logout", (Delegate)LogoutAsync);
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        [FromForm] string? password,
        [FromForm] string? returnUrl,
        IOptions<AdminOptions> options)
    {
        string expected = options.Value.Password;
        bool valid = !string.IsNullOrEmpty(expected)
            && password is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(expected));

        if (!valid)
        {
            return Results.Redirect("/login?error=1");
        }

        ClaimsIdentity identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        string target = !string.IsNullOrEmpty(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : "/";
        return Results.LocalRedirect(target);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }
}
