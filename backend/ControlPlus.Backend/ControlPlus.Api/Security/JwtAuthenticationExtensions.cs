using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ControlPlus.Api.Security;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddControlPlusJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwt = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwt);
        jwt.EnsureValid();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !string.Equals(
                    configuration["ASPNETCORE_ENVIRONMENT"],
                    "Development",
                    StringComparison.OrdinalIgnoreCase);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
                    NameClaimType = ClaimTypes.Name
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                        var securityStamp = context.Principal?.FindFirst(JwtClaimTypes.SecurityStamp)?.Value;
                        if (!Guid.TryParse(subject, out var userId) || string.IsNullOrWhiteSpace(securityStamp))
                        {
                            context.Fail("The access token does not contain a valid session identity.");
                            return;
                        }

                        var sessionValidator = context.HttpContext.RequestServices
                            .GetRequiredService<ICurrentUserSessionValidator>();
                        if (!await sessionValidator.ValidateAsync(userId, securityStamp, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The user session is no longer valid.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                        await WriteAuthenticationProblemAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "authentication.unauthorized",
                            "La autenticación es obligatoria para acceder a este recurso.");
                    },
                    OnForbidden = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        await WriteAuthenticationProblemAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "authorization.forbidden",
                            "No tiene permiso para realizar esta acción.");
                    }
                };
            });

        var fallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
        services.AddAuthorizationBuilder().SetFallbackPolicy(fallbackPolicy);

        return services;
    }

    private static Task WriteAuthenticationProblemAsync(
        HttpContext httpContext,
        int statusCode,
        string code,
        string title) =>
        Results.Problem(
                statusCode: statusCode,
                title: title,
                type: $"https://controlplus.local/problems/{code.Replace('.', '-')}",
                instance: httpContext.Request.Path,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = code
                })
            .ExecuteAsync(httpContext);
}
