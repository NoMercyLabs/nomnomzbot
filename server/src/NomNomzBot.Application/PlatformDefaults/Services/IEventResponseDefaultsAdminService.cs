// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;

namespace NomNomzBot.Application.PlatformDefaults.Services;

/// <summary>
/// The platform admin's runtime editor for event-response defaults (plan item A4): what every channel that
/// never saved its own response does when an event fires. A channel's own response keeps winning. Every
/// change is previewed with its counted blast radius, audited, and read back.
/// </summary>
public interface IEventResponseDefaultsAdminService
{
    Task<Result<IReadOnlyList<EventResponseDefaultDto>>> ListAsync(CancellationToken ct = default);

    /// <summary>The counted blast radius of applying <paramref name="change"/> to <paramref name="eventType"/>.</summary>
    Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        string eventType,
        EventResponseDefaultChange change,
        CancellationToken ct = default
    );

    /// <summary>Applies the previewed change and returns the default read back from the database.</summary>
    Task<Result<EventResponseDefaultDto>> SetAsync(
        string eventType,
        SetEventResponseDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    );
}
