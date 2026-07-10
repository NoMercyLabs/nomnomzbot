// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Provisions the tenant <c>Channel</c> for a streamer's presence on a NON-Twitch platform — their YouTube or
/// Kick channel as a first-class chat source (combined-chat item 6). The schema models each platform presence
/// as its own <c>Channel</c> row under the same owner, keyed uniquely by <c>(Provider, ExternalChannelId)</c>;
/// this is the single get-or-create for those rows so the cross-platform chat poller has a stable tenant
/// <see cref="Guid"/> to persist and broadcast messages under. Twitch channels keep their existing onboarding
/// path (<c>AuthService</c>/<c>ChannelService</c>); this covers only the extra platforms.
/// </summary>
public interface IPlatformChannelProvisioner
{
    /// <summary>
    /// Returns the tenant <see cref="Guid"/> for <paramref name="ownerUserId"/>'s channel on
    /// <paramref name="provider"/> with external id <paramref name="externalChannelId"/>, creating the row on
    /// first sight and reusing it thereafter. Idempotent and race-safe against the unique
    /// <c>(Provider, ExternalChannelId)</c> index (a concurrent create is caught and the winner adopted). The
    /// new tenant inherits the owner's existing deployment mode + billing tier so it runs under the same
    /// profile as their primary channel.
    /// </summary>
    Task<Guid> GetOrCreateAsync(
        Guid ownerUserId,
        string provider,
        string externalChannelId,
        string displayName,
        CancellationToken cancellationToken = default
    );
}
