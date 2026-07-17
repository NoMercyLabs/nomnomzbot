// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Authentication;

/// <summary>
/// The automation data plane's authentication scheme (automation-api.md D3/D4): reads
/// <c>Authorization: Bearer &lt;secret&gt;</c> — header only, never a query parameter — resolves it
/// through <see cref="IAutomationTokenAuthenticator"/>, parks the <see cref="AutomationPrincipal"/>
/// on <c>HttpContext.Items</c> for the controller, and sets the tenant to the token's channel (the
/// secret IS the tenant selector; the token principal deliberately carries no NameIdentifier so the
/// JWT-oriented tenant middleware never mistakes it for a dashboard user).
/// </summary>
public sealed class ApiTokenAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AutomationToken";
    private const string BearerPrefix = "Bearer ";

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    )
        : base(options, logger, encoder) { }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (
            string.IsNullOrEmpty(header)
            || !header.StartsWith(BearerPrefix, StringComparison.Ordinal)
        )
            return AuthenticateResult.NoResult();

        string secret = header[BearerPrefix.Length..].Trim();
        IAutomationTokenAuthenticator authenticator =
            Context.RequestServices.GetRequiredService<IAutomationTokenAuthenticator>();
        Result<AutomationPrincipal> authenticated = await authenticator.AuthenticateAsync(
            secret,
            Context.RequestAborted
        );
        if (authenticated.IsFailure)
            return AuthenticateResult.Fail("Invalid automation token.");

        AutomationPrincipal principal = authenticated.Value;
        Context.Items[typeof(AutomationPrincipal)] = principal;
        Context
            .RequestServices.GetRequiredService<ICurrentTenantService>()
            .SetTenant(principal.BroadcasterId);

        ClaimsIdentity identity = new(
            [
                new Claim("automation:token_id", principal.TokenId.ToString()),
                new Claim("automation:token_name", principal.TokenName),
                new Claim("automation:broadcaster_id", principal.BroadcasterId.ToString()),
            ],
            SchemeName
        );
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)
        );
    }
}
