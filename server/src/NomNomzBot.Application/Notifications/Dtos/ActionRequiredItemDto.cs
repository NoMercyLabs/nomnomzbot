// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Notifications.Dtos;

/// <summary>
/// One row of the dashboard's action-required inbox (S071a) — a real, already-detected condition that needs
/// the streamer's attention, surfaced from an existing signal (never fabricated). <see cref="Kind"/> is a
/// stable machine key the dashboard groups/icons by (e.g. <c>integration_token_dead</c>,
/// <c>held_chat_message</c>); <see cref="Severity"/> is <c>critical</c> | <c>warning</c> | <c>info</c>.
/// </summary>
public sealed record ActionRequiredItemDto(
    string Kind,
    string Severity,
    string Title,
    string Message,
    DateTime DetectedAt,
    string DeepLinkRoute
);
