// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity;

/// <summary>
/// The one definition of how long a deleted channel can be brought back.
/// <para>
/// Deleting a channel is a SOFT delete: the tenant row gets a <c>DeletedAt</c>, every global query filter
/// hides it and everything under it, and the bot stops serving the channel immediately — but the rows survive
/// for <see cref="RestoreWindowDays"/> days so a mistake is a mistake and not a catastrophe. After that the
/// data is unrecoverable. The preview, the confirm dialog and the restore path all read this constant, so the
/// promise the dialog makes is the promise the restore keeps.
/// </para>
/// </summary>
public static class ChannelDeletionPolicy
{
    public const int RestoreWindowDays = 30;

    /// <summary>The instant a channel soft-deleted at <paramref name="deletedAtUtc"/> becomes permanent.</summary>
    public static DateTime PermanentAfter(DateTime deletedAtUtc) =>
        deletedAtUtc.AddDays(RestoreWindowDays);
}
