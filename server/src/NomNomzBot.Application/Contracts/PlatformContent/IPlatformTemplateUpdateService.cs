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

namespace NomNomzBot.Application.Contracts.PlatformContent;

/// <summary>
/// The channel side of template updates: which of this channel's installed template copies are behind their
/// definition's current published version, and pulling that version into one copy. A publish never overwrites
/// a copy the channel edited; this is how the channel takes the new version anyway, by its own choice.
/// </summary>
public interface IPlatformTemplateUpdateService
{
    /// <summary>This channel's copies of <paramref name="kind"/> installed from an older published version.</summary>
    Task<Result<IReadOnlyList<PlatformTemplateUpdateDto>>> ListAsync(
        Guid broadcasterId,
        string kind,
        CancellationToken ct = default
    );

    /// <summary>
    /// Rewrites one copy from the definition's current published version through the kind's own save path. Gated
    /// by the kind's write key in the channel. Fails <c>NOT_FOUND</c> when the copy is not this channel's copy of
    /// that definition, and <c>ALREADY_CURRENT</c> when it is already on the current version.
    /// </summary>
    Task<Result<PlatformTemplateUpdateDto>> ApplyAsync(
        Guid callerUserId,
        Guid broadcasterId,
        Guid definitionId,
        Guid rowId,
        CancellationToken ct = default
    );
}
