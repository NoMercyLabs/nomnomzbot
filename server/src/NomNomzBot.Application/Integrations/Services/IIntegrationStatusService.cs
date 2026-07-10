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
using NomNomzBot.Application.Integrations.Dtos;

namespace NomNomzBot.Application.Integrations.Services;

/// <summary>
/// Resolves the connection state of every known integration for a channel from the stored service
/// tokens and Discord guild connections. The single source of truth behind the integrations list
/// endpoint and the dashboard render manifest.
/// </summary>
public interface IIntegrationStatusService
{
    /// <summary>Get the connection state of every known integration for the channel.</summary>
    Task<Result<List<ChannelIntegrationDto>>> GetStatusesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
