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

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NomNomzBot.Application.AutomationApi;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Security;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Resolves who is making the current change. The request principal wins: an automation token is an API key
/// (its token id, no user), any other authenticated principal is a person. With no request, a background-work
/// sanction that names a person (a user action run on a worker) is that person; every other background path
/// is the system. Reads only claims and the ambient sanction, so it holds no state and is safe as a singleton.
/// </summary>
public sealed class JournalActorAccessor(
    IHttpContextAccessor httpContextAccessor,
    IOutboundSanctionAccessor sanctions
) : IJournalActorAccessor
{
    public JournalActor Current => FromRequest() ?? FromSanction() ?? JournalActor.System;

    private JournalActor? FromRequest()
    {
        ClaimsPrincipal? principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        string? tokenId = principal.FindFirstValue(AutomationClaimTypes.TokenId);
        if (tokenId is not null)
            return JournalActor.ApiKey(tokenId);

        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId)
            ? JournalActor.Person(userId)
            : null;
    }

    private JournalActor? FromSanction()
    {
        OutboundSanction? sanction = sanctions.Current;
        return sanction is { Basis: OutboundSanctionBasis.UserAction, ActorUserId: { } userId }
            ? JournalActor.Person(userId)
            : null;
    }
}
