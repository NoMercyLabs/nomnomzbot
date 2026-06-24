// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.EventStore.LegacyImport;

/// <summary>
/// Outcome of a one-shot legacy <c>ChannelEvents</c> import. <see cref="Imported"/> counts the journal rows newly
/// appended; <see cref="SkippedUnmapped"/> counts rows whose type/shape is not an imported channel event;
/// <see cref="SkippedDuplicate"/> counts rows whose derived <c>EventId</c> already existed (re-run idempotency).
/// </summary>
public sealed record LegacyImportSummary(
    long TotalRead,
    long Imported,
    long SkippedUnmapped,
    long SkippedDuplicate
);
