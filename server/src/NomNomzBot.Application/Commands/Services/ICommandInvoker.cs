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
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The one runner behind every binding surface. It runs a target directly: no cooldown and no permission check,
/// because the streamer chose the binding (commands-pipelines.md section 12.8, Q1).
/// </summary>
public interface ICommandInvoker
{
    /// <summary>
    /// Fails with <c>NOT_FOUND</c> for an unknown or disabled pipeline, <c>VALIDATION_FAILED</c> for a malformed
    /// target and <c>NOT_SUPPORTED</c> for a target kind the invoker does not run yet. An action failure keeps
    /// its own code (<c>NOT_FOUND</c>, <c>FORBIDDEN</c>, <c>RATE_LIMITED</c>).
    /// </summary>
    Task<Result<InvocationOutcome>> InvokeAsync(
        Guid broadcasterId,
        BindingTarget target,
        InvocationContext context,
        CancellationToken cancellationToken = default
    );
}
