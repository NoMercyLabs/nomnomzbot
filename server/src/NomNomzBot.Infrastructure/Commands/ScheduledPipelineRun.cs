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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// One due scheduled task, already marked fired, handed to the <see cref="IScheduledPipelineDispatcher"/>. A plain
/// snapshot (never the tracked entity): the run outlives the sweep's DbContext. <paramref name="DirectTarget"/> is
/// set only for a command or action target, which runs through the command invoker instead of the pipeline engine.
/// </summary>
public sealed record ScheduledPipelineRun(
    Guid TaskId,
    string? PipelineName,
    PipelineRequest Request,
    BindingTarget? DirectTarget = null
);
