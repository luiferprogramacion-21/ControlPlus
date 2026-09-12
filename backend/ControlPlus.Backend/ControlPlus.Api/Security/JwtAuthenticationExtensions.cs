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
                    }
                };
            });

        var fallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
        services.AddAuthorizationBuilder().SetFallbackPolicy(fallbackPolicy);

        return services;
    }
}
