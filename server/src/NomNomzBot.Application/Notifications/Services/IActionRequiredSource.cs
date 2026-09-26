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
using NomNomzBot.Application.Notifications.Dtos;

namespace NomNomzBot.Application.Notifications.Services;

/// <summary>
/// One producer of action-required inbox items (plan item A0). Every subsystem that can detect a condition the
/// streamer must fix reports it through this contract; <see cref="IActionRequiredInboxService"/> aggregates every
/// registered source, applies the persisted dismissals, and serves the result.
/// <para>
/// Items are DERIVED ON READ from state the subsystem already persists (a connection status, a failed
/// subscription row, a failed build, a journaled event). Nothing is copied into a second store, so an item can
/// never outlive its condition: fixing the cause removes the item with no clean-up step. A source must only
/// report a condition it has actually detected; it never guesses.
/// </para>
/// </summary>
public interface IActionRequiredSource
{
    /// <summary>
    /// The id prefixes this source mints (e.g. <c>token:</c>). The dismiss endpoint routes a requested id to the
    /// source whose prefix it carries, and rejects an id no source owns.
    /// </summary>
    IReadOnlyCollection<string> KeyPrefixes { get; }

    /// <summary>
    /// The domain-event type names (the journal's CLR-name discriminator) whose publication can change what this
    /// source reports. The inbox pushes a live invalidation to the channel's dashboards when one is journaled.
    /// </summary>
    IReadOnlyCollection<string> InvalidatingEventTypes { get; }

    /// <summary>
    /// The channel's current items from this source, excluding any whose dismissal key is in
    /// <paramref name="dismissedKeys"/>.
    /// </summary>
    Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The persisted dismissal keys a dismiss of <paramref name="itemId"/> writes. Most items dismiss as their own
    /// id; a grouped item expands into the keys of its members, so a NEW member surfaces again.
    /// </summary>
    Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    );
}
