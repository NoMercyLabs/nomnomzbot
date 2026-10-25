// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Discord;

/// <summary>
/// One event the server can post to Discord for. A trigger is listed here only when an event handler
/// raises it, so the dashboard never offers a rule that can not fire.
/// </summary>
/// <param name="Name">The stored trigger name, for example <c>go_live</c>.</param>
/// <param name="LabelKey">The dashboard translation key of the trigger's display name.</param>
/// <param name="Placeholders">The message placeholders the trigger's handler fills, without braces.</param>
public sealed record DiscordNotificationTrigger(
    string Name,
    string LabelKey,
    IReadOnlyList<string> Placeholders
);
