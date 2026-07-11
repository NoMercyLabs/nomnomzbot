// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Giveaways.Dtos;

/// <summary>The giveaway read model (giveaways.md §3.1) — config + lifecycle + live entry stats.</summary>
public sealed record GiveawayDto(
    Guid Id,
    string Title,
    string EntryMode,
    string? Keyword,
    long? EntryCost,
    int MaxEntriesPerUser,
    string? EligibilityJson,
    string? WeightingJson,
    int WinnerCount,
    bool ExcludeModerators,
    int? ClaimWindowMinutes,
    string PrizeMode,
    long? PrizeCurrencyAmount,
    bool PrizeFromPot,
    Guid? PrizePipelineId,
    Guid? PrizeCodePoolId,
    string Status,
    DateTime? OpenedAt,
    DateTime? ClosesAt,
    DateTime? DrawnAt,
    int EntryCount,
    DateTime CreatedAt
);

/// <summary>Create/update payload — the full configurable surface (draft/closed only for updates).</summary>
public sealed record UpsertGiveawayRequest(
    string Title,
    string EntryMode,
    string? Keyword = null,
    long? EntryCost = null,
    int MaxEntriesPerUser = 1,
    string? EligibilityJson = null,
    string? WeightingJson = null,
    int WinnerCount = 1,
    bool ExcludeModerators = false,
    int? ClaimWindowMinutes = null,
    string PrizeMode = "announce",
    long? PrizeCurrencyAmount = null,
    bool PrizeFromPot = false,
    Guid? PrizePipelineId = null,
    Guid? PrizeCodePoolId = null
);

/// <summary>List filter — null status = all live (non-archived) giveaways.</summary>
public sealed record GiveawayFilter(string? Status = null);

/// <summary>One recorded entry (giveaways.md §3.1).</summary>
public sealed record GiveawayEntryDto(
    Guid Id,
    Guid GiveawayId,
    Guid ViewerUserId,
    int TicketCount,
    DateTime EnteredAt
);

/// <summary>One drawn winner with its fulfillment trail (giveaways.md §3.1).</summary>
public sealed record GiveawayWinnerDto(
    Guid Id,
    Guid GiveawayId,
    Guid ViewerUserId,
    string ViewerDisplayName,
    DateTime DrawnAt,
    string Status,
    bool IsRedraw,
    Guid? AssignedCodeId,
    bool? WhisperDelivered
);

/// <summary>A code pool summary — counts only, never code contents (D6).</summary>
public sealed record CodePoolDto(
    Guid Id,
    string Name,
    string? Description,
    int Total,
    int Available,
    int Assigned
);

/// <summary>Pool detail: the codes MASKED (label + status), never plaintext (D6).</summary>
public sealed record CodePoolDetailDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<MaskedCodeDto> Codes
);

/// <summary>One masked code row — the label/last-4 tail is all a read ever shows.</summary>
public sealed record MaskedCodeDto(Guid Id, string? Label, string Status, DateTime? AssignedAt);

public sealed record CreateCodePoolRequest(string Name, string? Description = null);

/// <summary>Bulk code intake — each plaintext is AEAD-encrypted on write and never echoed back.</summary>
public sealed record AddCodesRequest(IReadOnlyList<CodeInput> Codes);

public sealed record CodeInput(string Code, string? Label = null);
