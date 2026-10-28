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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Platform-operator hub. Plane-C gated on <c>iam:manage</c> (platform-conventions.md §5 hub table) — the
/// policy is evaluated by endpoint authorization at connection time (SignalR hubs are mapped endpoints, so a
/// class-level <c>[Authorize]</c> policy gates the handshake), routing through
/// <c>PlatformIamAuthorizationHandler</c> exactly like the admin controllers: platform-principal marker
/// first, then the audited IAM check on SaaS.
/// </summary>
[Authorize(Policy = IamPermissionKeys.IamManage)]
public class AdminHub(IAuthorizationService authorization) : Hub<IAdminClient>
{
    /// <summary>Connections holding <c>platform:analytics:read</c>: they receive the platform stats.</summary>
    public const string AnalyticsGroup = "admin-analytics";

    /// <summary>Every other <c>iam:manage</c> connection: it receives the health snapshot without stats.</summary>
    public const string NoAnalyticsGroup = "admin-no-analytics";

    public override async Task OnConnectedAsync()
    {
        AuthorizationResult analytics = await authorization.AuthorizeAsync(
            Context.User!,
            null,
            IamPermissionKeys.PlatformAnalyticsRead
        );
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            analytics.Succeeded ? AnalyticsGroup : NoAnalyticsGroup
        );
        await base.OnConnectedAsync();
    }
}
