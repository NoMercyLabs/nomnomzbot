// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Consequences;

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>
/// Everything the delete-channel confirmation must state BEFORE the button can be pressed — the largest blast
/// radius in the product.
/// <para>
/// Channel delete is a SOFT delete with a restore window: the tenant's rows stay on disk and every query
/// filter hides them, and only after <see cref="PermanentAfterUtc"/> does the data become unrecoverable. The
/// dialog states both the window and the date, because a button that looks irreversible and is not is as
/// dishonest as one that looks reversible and is not.
/// </para>
/// </summary>
/// <param name="ChannelName">
/// The channel's real name — the dialog requires the operator to TYPE it to arm the confirm, so a delete can
/// never be a mis-click, and so the operator sees which channel they are actually on.
/// </param>
/// <param name="BlastRadius">
/// The counted, curated categories of rows that disappear. <see cref="BlastRadiusDto.IsMinimum"/> is set when
/// any category could not be counted exhaustively.
/// </param>
/// <param name="ExternalConsequences">
/// What stops working OUTSIDE our database. Never numbers alone — each names an effect.
/// </param>
/// <param name="RestoreWindowDays">How many days the channel can be restored for. Never zero.</param>
/// <param name="PermanentAfterUtc">
/// The instant the soft-deleted tenant becomes unrecoverable, computed from the server clock at preview time.
/// </param>
public sealed record ChannelDeletePreviewDto(
    string ChannelName,
    BlastRadiusDto BlastRadius,
    IReadOnlyList<ExternalConsequenceDto> ExternalConsequences,
    int RestoreWindowDays,
    DateTime PermanentAfterUtc
);
