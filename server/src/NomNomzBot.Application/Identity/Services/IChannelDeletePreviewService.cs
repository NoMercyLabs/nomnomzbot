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
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Counts what deleting a channel destroys, BEFORE the operator confirms it (S-CONSEQ-DELETE-CHANNEL).
/// A failed lookup returns a <see cref="Result"/> failure and never a zero — a preview that reports "nothing"
/// for a check that did not run causes exactly the loss it exists to prevent.
/// </summary>
public interface IChannelDeletePreviewService
{
    /// <summary>The counted categories, the named external consequences, and the restore window.</summary>
    Task<Result<ChannelDeletePreviewDto>> PreviewAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>The configuration rows a channel reset clears, grouped by settings area.</summary>
    Task<Result<ChannelResetPreviewDto>> PreviewResetAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );
}
