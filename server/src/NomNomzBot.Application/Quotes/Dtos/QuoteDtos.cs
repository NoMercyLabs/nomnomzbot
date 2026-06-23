// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Quotes.Dtos;

/// <summary>Create payload for a new quote (quotes.md §3). <c>QuotedAt</c> defaults to creation when null.</summary>
public sealed record AddQuoteRequest(
    string Text,
    string? QuotedDisplayName,
    string? ContextGame,
    DateTime? QuotedAt,
    Guid? CreatedByUserId
);

/// <summary>Edit payload (quotes.md §3). The per-channel <c>Number</c> is immutable and not editable.</summary>
public sealed record EditQuoteRequest(string Text, string? QuotedDisplayName, string? ContextGame);

/// <summary>Free-text search filter over <c>Text</c>/<c>QuotedDisplayName</c> (quotes.md §1).</summary>
public sealed record QuoteSearch(string? Term);

/// <summary>The transport/read shape of a quote (quotes.md §3).</summary>
public sealed record QuoteDto(
    Guid Id,
    int Number,
    string Text,
    string? QuotedDisplayName,
    string? ContextGame,
    DateTime? QuotedAt,
    DateTime CreatedAt
);
