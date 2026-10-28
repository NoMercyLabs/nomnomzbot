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

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>The channel's shared-ban policy + its trust list (moderation.md §3.5 / §4).</summary>
public sealed record SharedBanSettingsDto(
    bool AcceptSharedChatBans,
    bool ShareOutgoingBans,
    IReadOnlyList<SharedBanTrustedChannelDto> TrustedChannels
);

/// <summary>Both switches are explicit on every save — no partial policy writes (opt-in, default-deny).</summary>
public sealed record SaveSharedBanSettingsRequest(
    bool AcceptSharedChatBans,
    bool ShareOutgoingBans
);

/// <summary>One trusted partner row (J.9a) with its display name for the trust-list UI.</summary>
public sealed record SharedBanTrustedChannelDto(
    Guid TrustedChannelId,
    string TrustedChannelName,
    Guid? AddedByUserId,
    DateTime CreatedAt
);

/// <summary>Adds one partner to the inbound-ban trust list.</summary>
public sealed record AddTrustedChannelRequest(Guid TrustedChannelId);

/// <summary>
/// A shared ban that WAS placed (moderation.md §3.5): the id of the provenance record. A ban that was refused or
/// skipped is never this type — it is a failure Result whose error code is the reason.
/// </summary>
public sealed record SharedBanApplicationResult(int ActionId);
