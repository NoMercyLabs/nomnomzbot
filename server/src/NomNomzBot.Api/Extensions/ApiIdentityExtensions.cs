// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using NomNomzBot.Api.Authentication;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Middleware;

namespace NomNomzBot.Api.Extensions;

/// <summary>
/// Who is calling and what they may do: the bearer schemes, the Gate-2 and Plane-C authorization handlers,
/// and the middleware order that ties them to the tenant. <c>Program.cs</c> and the API test host both call
/// these, so a test authenticates through the exact pipeline the deployed API runs.
/// </summary>
public static class ApiIdentityExtensions
{
    public static IServiceCollection AddApiIdentity(
        this IServiceCollection services,
        TokenValidationParameters bearerValidationParameters
    )
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            // Hub query-string tokens + the session-end checks (revoked sid, closed act-as grant) live in
            // DashboardJwtBearer so tests authenticate through the exact same pipeline.
            .AddJwtBearer(options =>
                DashboardJwtBearer.Configure(options, bearerValidationParameters)
            )
            // Automation data plane (automation-api.md D3/D4): channel API tokens over the
            // Authorization header — a separate scheme so /automation/v1 never accepts a dashboard JWT.
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenAuthenticationHandler.SchemeName,
                null
            );

        services.AddAuthorization();
        // A failed RequireAction answers which action, the role it needs and the role the caller holds.
        services.AddSingleton<
            IAuthorizationMiddlewareResultHandler,
            ForbiddenActionResultHandler
        >();

        // Roles-permissions Gate 2 (§6) + Plane-C IAM (§3.7): ONE dynamic policy provider that synthesizes
        // rbac:<actionKey> policies (Gate-2 handler over IActionAuthorizationService) and verbatim
        // IamPermission-key policies (Plane-C handler over IPlatformIamService, audited on SaaS).
        services.AddSingleton<IAuthorizationPolicyProvider, ActionAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, ActionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, PlatformIamAuthorizationHandler>();

        return services;
    }

    /// <summary>
    /// Tenant resolution MUST sit between authentication and authorization: it needs the authenticated
    /// principal (to resolve the caller's own channel / verify channel access), and the [RequireAction]
    /// Gate-2 policies in UseAuthorization read the resolved tenant. Placing it after UseAuthorization
    /// left every action check tenant-less, an unconditional 403 on channel-scoped endpoints.
    /// </summary>
    public static IApplicationBuilder UseApiIdentity(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();
        return app;
    }
}
