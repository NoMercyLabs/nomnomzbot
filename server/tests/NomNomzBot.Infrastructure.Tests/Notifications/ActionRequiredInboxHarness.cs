// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Builds the inbox exactly as DI does: every production <see cref="IActionRequiredSource"/> over one database,
/// so a test proves the aggregated result the dashboard receives rather than one source in isolation.
/// </summary>
internal static class ActionRequiredInboxHarness
{
    public static List<IActionRequiredSource> Sources(IApplicationDbContext db) =>
        [
            new DeadIntegrationTokenSource(db),
            new HeldChatMessageSource(db),
            new UnmanagedRewardSource(db),
        ];

    public static ActionRequiredInboxService Create(
        IApplicationDbContext db,
        TimeProvider? clock = null
    ) =>
        new(
            Sources(db),
            db,
            clock ?? TimeProvider.System,
            NullLogger<ActionRequiredInboxService>.Instance
        );
}
