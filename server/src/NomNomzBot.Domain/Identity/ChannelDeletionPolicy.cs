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
