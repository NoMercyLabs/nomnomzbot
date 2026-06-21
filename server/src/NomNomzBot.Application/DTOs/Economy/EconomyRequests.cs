// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DTOs.Economy;

/// <summary>Upsert a channel's currency definition (economy.md §4).</summary>
public sealed record UpsertCurrencyConfigRequest(
    string CurrencyName,
    string? CurrencyNamePlural,
    string? IconUrl,
    bool IsEnabled,
    long StartingBalance,
    long? MaxBalance,
    int DecimalPlaces
);

/// <summary>Upsert one earning rule (economy.md §4). <see cref="Source"/> is an <c>EarningSource</c> token.</summary>
public sealed record UpsertEarningRuleRequest(
    string Source,
    bool IsEnabled,
    long Rate,
    int? UnitWindowSeconds,
    long? PerWindowCap,
    long? PerStreamCap,
    int? MinRoleLevel,
    IReadOnlyDictionary<string, object?>? BonusConfig
);

/// <summary>
/// A ledger post (economy.md §4). The sign of <see cref="Amount"/> decides credit (+) vs debit (−);
/// <see cref="EntryType"/> is a <c>CurrencyEntryType</c> token.
/// </summary>
public sealed record PostLedgerEntryCommand(
    Guid ViewerUserId,
    long Amount,
    string EntryType,
    string? SourceType,
    Guid? SourceId,
    Guid? EventId,
    string? Reason,
    Guid? ActorUserId,
    string? IdempotencyKey
);

/// <summary>A point-to-point transfer between two viewers in the same channel (economy.md §4). <see cref="Amount"/> &gt; 0.</summary>
public sealed record TransferCommand(
    Guid FromViewerUserId,
    Guid ToViewerUserId,
    long Amount,
    string? Reason,
    Guid? ActorUserId
);

/// <summary>A broadcaster/admin manual credit or debit (economy.md §4). Sign of <see cref="Amount"/> decides direction.</summary>
public sealed record AdminAdjustCommand(
    Guid ViewerUserId,
    long Amount,
    string Reason,
    Guid ActorUserId
);

/// <summary>An engagement event to accrue currency for (economy.md §4). <see cref="Source"/> is an <c>EarningSource</c> token.</summary>
public sealed record EarnRequest(
    Guid ViewerUserId,
    string Source,
    long Units,
    Guid? EventId,
    int? ViewerRoleLevel,
    IReadOnlyDictionary<string, object?>? Context
);

/// <summary>The watch-time presence sweep batch (economy.md §4).</summary>
public sealed record WatchTimeBatchRequest(
    IReadOnlyList<WatchTimeViewer> Viewers,
    int WindowSeconds,
    Guid? StreamId
);

/// <summary>One viewer in a watch-time sweep (economy.md §4).</summary>
public sealed record WatchTimeViewer(
    Guid ViewerUserId,
    int PresentSeconds,
    bool PresenceVerified,
    int RoleLevel
);
