// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Authentication;

/// <summary>
/// The dashboard's JwtBearer setup, in one place so the running API and the tests that prove what a token
/// authenticates as use the same pipeline.
/// </summary>
public static class DashboardJwtBearer
{
    public static void Configure(JwtBearerOptions options, TokenValidationParameters parameters)
    {
        options.TokenValidationParameters = parameters;
        options.Events = new()
        {
            OnMessageReceived = ReadHubAccessToken,
            OnTokenValidated = RejectEndedSessionAsync,
        };
    }

    /// <summary>SignalR sends the token on the query string; accept it there for hub paths only.</summary>
    private static Task ReadHubAccessToken(MessageReceivedContext ctx)
    {
        StringValues accessToken = ctx.Request.Query["access_token"];
        if (
            !string.IsNullOrEmpty(accessToken)
            && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")
        )
            ctx.Token = accessToken;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Immediate session end (S098b, owner decision): logout / impersonation-end revoke the token's `sid`, so a
    /// still-unexpired access token must stop authenticating on its very next request. An act-as token
    /// additionally dies with its support-access grant, however that grant was closed.
    /// </summary>
    private static async Task RejectEndedSessionAsync(TokenValidatedContext ctx)
    {
        IServiceProvider services = ctx.HttpContext.RequestServices;
        CancellationToken ct = ctx.HttpContext.RequestAborted;

        if (
            await SessionRevocationCheck.IsSessionRevokedAsync(
                ctx.Principal,
                services.GetRequiredService<ISessionRevocationService>(),
                ct
            )
        )
        {
            ctx.Fail("Session has been revoked.");
            return;
        }

        if (
            ImpersonationSessionCheck.IsActAsToken(ctx.Principal)
            && await ImpersonationSessionCheck.IsEndedAsync(
                ctx.Principal,
                services.GetRequiredService<IImpersonationSessionService>(),
                ct
            )
        )
            ctx.Fail("Impersonation session has ended.");
    }
}
