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
using Microsoft.AspNetCore.Mvc.Filters;
using NomNomzBot.Application.Contracts.Security;

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// Turns a passed Gate-2 check into the sanction the Helix transport requires for any outbound write.
///
/// <para>
/// The sanction is deliberately derived from <see cref="RequireActionAttribute"/> and nothing else. An
/// endpoint that declares no action key opens no scope, so a write attempted from an ungated endpoint is
/// refused at the transport — the permission model and the ability to change a third party's state become
/// the same thing rather than two systems that have to be kept in agreement by hand.
/// </para>
/// </summary>
public sealed class OutboundSanctionFilter(IOutboundSanctionAccessor sanctions) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        RequireActionAttribute? gate = context
            .ActionDescriptor.EndpointMetadata.OfType<RequireActionAttribute>()
            .FirstOrDefault();

        if (gate is null)
        {
            await next();
            return;
        }

        string? subject = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? actor = Guid.TryParse(subject, out Guid parsed) ? parsed : null;

        using IDisposable scope = sanctions.Begin(
            OutboundSanction.UserAction(gate.ActionKey, actor)
        );
        await next();
    }
}
