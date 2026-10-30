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

namespace NomNomzBot.Application.Admin.Diagnostics;

/// <summary>
/// What an admin asked about one channel: the channel, and the run list options the channel's own
/// pipeline-executions endpoint takes (page, failures only, one pipeline).
/// </summary>
public sealed record ChannelDiagnosticRequest(
    Guid BroadcasterId,
    PaginationParams RunPage,
    bool FailedRunsOnly,
    Guid? PipelineId
);
