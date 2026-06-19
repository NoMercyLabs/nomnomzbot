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
/// Authorizes whether an authenticated user may resolve (act under) a given channel/tenant.
/// A user may act on their own channel (Channel.Id == User.Id), channels they actively
/// moderate, or — for platform admins — any channel. Used to gate tenant resolution so a
/// caller cannot impersonate a channel they do not control.
/// </summary>
public interface IChannelAccessService
{
    Task<bool> CanResolveTenantAsync(
        string userId,
        string channelId,
        CancellationToken cancellationToken = default
    );
}
