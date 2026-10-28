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
