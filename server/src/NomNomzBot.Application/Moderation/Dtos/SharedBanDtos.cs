// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

/// <summary>The outcome of one inbound shared-ban application attempt (moderation.md §3.5).</summary>
public sealed record SharedBanApplicationResult(bool Applied, string? SkippedReason, int? ActionId);
