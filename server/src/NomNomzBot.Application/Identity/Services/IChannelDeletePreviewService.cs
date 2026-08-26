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
}
