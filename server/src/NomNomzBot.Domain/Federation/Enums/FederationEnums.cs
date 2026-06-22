// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Federation.Enums;

/// <summary>A peer's trust lifecycle (federation-oidc.md §1, D.1). Default-deny: a peer is pending until trusted.</summary>
public static class FederationTrustState
{
    public const string Pending = "pending";
    public const string Trusted = "trusted";
    public const string Revoked = "revoked";
    public const string Blocked = "blocked";
}

/// <summary>What a channel may share/accept across instances (federation-oidc.md §1, D.3).</summary>
public static class FederationOptInType
{
    public const string SharedChatBans = "shared_chat_bans";
    public const string SharedBanList = "shared_ban_list";
    public const string SharedTrustList = "shared_trust_list";
    public const string SharedSavings = "shared_savings";
}

/// <summary>The direction a federation opt-in flows.</summary>
public static class FederationDirection
{
    public const string Accept = "accept";
    public const string Share = "share";
    public const string Both = "both";
}

/// <summary>Peer signing algorithms (federation-oidc.md §1, D.2). This instance signs/verifies rsa-sha256 only.</summary>
public static class FederationKeyAlgorithm
{
    public const string RsaSha256 = "rsa-sha256";
    public const string Ed25519 = "ed25519"; // accepted in the directory for forward-compat; not used by this instance
}
