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

using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Admin.Services;

/// <summary>
/// The admin read of one channel's data (S-ADMIN-DIAG-READ). Read-only, so no reason is asked (Stoney,
/// 2026-10-30): each read is gated on <c>tenant:read</c> and recorded in the operator's internal IAM audit, and
/// nothing about it is shown to the streamer.
/// </summary>
public interface IChannelDiagnosticsService
{
    /// <summary>Runs the requested <see cref="IChannelDiagnosticSection"/> classes (all when
    /// <paramref name="sections"/> is empty). An unknown section key is refused, naming it.</summary>
    Task<Result<ChannelDiagnosticsDto>> GetAsync(
        Guid actingPrincipalId,
        ChannelDiagnosticRequest request,
        IReadOnlyCollection<string>? sections,
        CancellationToken cancellationToken = default
    );

    /// <summary>One run of the channel, with its ordered step logs.</summary>
    Task<Result<PipelineExecutionDetailDto>> GetRunAsync(
        Guid actingPrincipalId,
        Guid broadcasterId,
        long runId,
        CancellationToken cancellationToken = default
    );
}
