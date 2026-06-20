// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity.Enums;

/// <summary>
/// Canonical string constants for the identity-auth [VC:enum] columns (identity-auth §1). Stored as the
/// schema's <c>string</c> values, not CLR enums, so the DB carries the readable token and the set is the
/// single source of truth for every producer/consumer. Grouped by the column they constrain.
/// </summary>
public static class AuthEnums
{
    /// <summary>Identity platform — <c>Users.Platform</c> / <c>Channel</c> / <c>BotAccount.Platform</c>.</summary>
    public static class Platform
    {
        public const string Twitch = "twitch";
        public const string Kick = "kick";
        public const string YouTube = "youtube";
    }

    /// <summary><c>Channel.Status</c> lifecycle.</summary>
    public static class ChannelStatus
    {
        public const string Active = "active";
        public const string Suspended = "suspended";
        public const string Churned = "churned";
        public const string PlatformBanned = "platform_banned";
    }

    /// <summary><c>Channel.DeploymentMode</c>.</summary>
    public static class DeploymentMode
    {
        public const string Saas = "saas";
        public const string SelfHostLite = "self_host_lite";
        public const string SelfHostFull = "self_host_full";
    }

    /// <summary><c>AuthSession.ClientType</c> — the device class a session was opened from.</summary>
    public static class ClientType
    {
        public const string Web = "web";
        public const string Desktop = "desktop";
        public const string Mobile = "mobile";
        public const string IpcDev = "ipc_dev";
    }

    /// <summary><c>RefreshToken.RevokedReason</c>.</summary>
    public static class RefreshTokenRevokedReason
    {
        public const string Logout = "logout";
        public const string Rotation = "rotation";
        public const string ReuseDetected = "reuse_detected";
        public const string Erasure = "erasure";
        public const string Admin = "admin";
    }

    /// <summary><c>IntegrationConnection.Provider</c> — the full provider surface (identity-auth §1).</summary>
    public static class IntegrationProvider
    {
        public const string Twitch = "twitch";
        public const string Spotify = "spotify";
        public const string Discord = "discord";
        public const string YouTube = "youtube";
        public const string AzureTts = "azure_tts";
        public const string ElevenLabs = "elevenlabs";
    }

    /// <summary><c>IntegrationConnection.Status</c>.</summary>
    public static class IntegrationStatus
    {
        public const string Connected = "connected";
        public const string Expired = "expired";
        public const string Revoked = "revoked";
        public const string NeedsReauth = "needs_reauth";
        public const string Pending = "pending";
    }

    /// <summary><c>IntegrationToken.TokenType</c>.</summary>
    public static class TokenType
    {
        public const string Access = "access";
        public const string Refresh = "refresh";
        public const string App = "app";
    }

    /// <summary><c>BotAccount.IdentityType</c> — one shared platform bot, optional per-channel custom.</summary>
    public static class BotIdentityType
    {
        public const string Shared = "shared";
        public const string Custom = "custom";
    }
}
