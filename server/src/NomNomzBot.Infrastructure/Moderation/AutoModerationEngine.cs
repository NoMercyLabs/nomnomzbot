// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The result of evaluating a chat message against auto-mod rules.
/// </summary>
public sealed record AutoModResult(
    bool ShouldAct,
    AutoModAction Action,
    int DurationSeconds,
    string Reason
);

/// <summary>
/// Action taken by the auto-mod engine.
/// </summary>
public enum AutoModAction
{
    None,
    Warn,
    Timeout,
    Ban,
    Delete,
}

/// <summary>
/// Per-channel auto-moderation configuration.
/// Loaded from rules stored as Records in the database.
/// </summary>
public sealed class AutoModSettings
{
    /// <summary>Block links not in the allowed-domains list.</summary>
    public bool FilterLinks { get; set; }

    /// <summary>Domains that are allowed even with link filtering on.</summary>
    public HashSet<string> AllowedDomains { get; set; } = [];

    /// <summary>Action to take when a blocked link is detected.</summary>
    public AutoModAction LinkAction { get; set; } = AutoModAction.Delete;

    /// <summary>Caps detection — threshold percentage (0–100) to trigger.</summary>
    public int CapsThresholdPercent { get; set; } = 70;

    /// <summary>Minimum message length before caps check applies.</summary>
    public int CapsMinLength { get; set; } = 8;

    /// <summary>Whether caps detection is enabled.</summary>
    public bool FilterCaps { get; set; }

    /// <summary>Action for caps violations.</summary>
    public AutoModAction CapsAction { get; set; } = AutoModAction.Timeout;

    /// <summary>Timeout duration for caps violations (seconds).</summary>
    public int CapsDurationSeconds { get; set; } = 30;

    /// <summary>Custom banned phrases (exact or regex).</summary>
    public List<string> BannedPhrases { get; set; } = [];

    /// <summary>Whether banned phrases use regex matching.</summary>
    public bool BannedPhrasesUseRegex { get; set; }

    /// <summary>Action for banned phrase violations.</summary>
    public AutoModAction BannedPhrasesAction { get; set; } = AutoModAction.Timeout;

    /// <summary>Timeout duration for banned phrase violations (seconds).</summary>
    public int BannedPhrasesDurationSeconds { get; set; } = 60;

    /// <summary>Slow mode: minimum seconds between messages per user. 0 = off.</summary>
    public int SlowModeSeconds { get; set; }

    /// <summary>Action for slow mode violations.</summary>
    public AutoModAction SlowModeAction { get; set; } = AutoModAction.Delete;

    /// <summary>Roles exempt from all auto-mod checks (e.g. "moderator", "vip").</summary>
    public HashSet<string> ExemptRoles { get; set; } = [];
}

/// <summary>
/// Evaluates incoming chat messages against auto-moderation rules.
/// Stateless — caller provides per-channel settings and user role context.
/// </summary>
public sealed class AutoModerationEngine : IAutoModerationEngine
{
    // Regex matching URLs (http/https/www) — compiled once per process
    private static readonly Regex UrlPattern = new(
        @"(?:https?://|www\.)\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250)
    );

    // Captures hostname from a URL string
    private static readonly Regex HostnamePattern = new(
        @"(?:https?://)?(?:www\.)?([^/\s?#]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250)
    );

    private readonly ICooldownManager _cooldowns;
    private readonly ILogger<AutoModerationEngine> _logger;

    public AutoModerationEngine(ICooldownManager cooldowns, ILogger<AutoModerationEngine> logger)
    {
        _cooldowns = cooldowns;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates a single chat message against the provided auto-mod settings.
    /// Returns the first matching rule's action, or None if no rules trigger.
    /// </summary>
    public AutoModResult Evaluate(
        string broadcasterId,
        string userId,
        string message,
        IEnumerable<string> userRoles,
        AutoModSettings settings
    )
    {
        HashSet<string> roles = new(userRoles, StringComparer.OrdinalIgnoreCase);

        // Exempt users bypass all checks — exempt if the user's highest role is at or above the lowest exempt role
        // on the unified ladder (so a Lead Moderator is exempt wherever a Moderator is), not a brittle exact match.
        if (settings.ExemptRoles.Count > 0)
        {
            int userLevel =
                roles.Count == 0 ? 0 : roles.Max(role => ChatRole.Parse(role).ToLevelValue());
            int exemptLevel = settings.ExemptRoles.Min(role => ChatRole.Parse(role).ToLevelValue());
            if (userLevel >= exemptLevel)
                return new(false, AutoModAction.None, 0, string.Empty);
        }

        // 1. Slow mode
        if (settings.SlowModeSeconds > 0)
        {
            if (!_cooldowns.IsOnCooldown(broadcasterId, "slowmode", userId))
            {
                _cooldowns.SetCooldown(
                    broadcasterId,
                    "slowmode",
                    TimeSpan.FromSeconds(settings.SlowModeSeconds),
                    userId
                );
            }
            else
            {
                _logger.LogDebug(
                    "AutoMod: slow mode violation from {UserId} in {BroadcasterId}",
                    userId,
                    broadcasterId
                );
                return new(true, settings.SlowModeAction, 0, "Slow mode active");
            }
        }

        // 2. Link filtering
        if (settings.FilterLinks && UrlPattern.IsMatch(message))
        {
            if (!IsAllowedLink(message, settings.AllowedDomains))
            {
                _logger.LogDebug(
                    "AutoMod: link blocked from {UserId} in {BroadcasterId}",
                    userId,
                    broadcasterId
                );
                return new(true, settings.LinkAction, 0, "Links are not permitted");
            }
        }

        // 3. Caps detection
        if (settings.FilterCaps && message.Length >= settings.CapsMinLength)
        {
            int letters = message.Count(char.IsLetter);
            if (letters > 0)
            {
                int upperCount = message.Count(char.IsUpper);
                double capsPercent = (double)upperCount / letters * 100.0;

                if (capsPercent >= settings.CapsThresholdPercent)
                {
                    _logger.LogDebug(
                        "AutoMod: caps violation ({Pct:F0}%) from {UserId} in {BroadcasterId}",
                        capsPercent,
                        userId,
                        broadcasterId
                    );
                    return new(
                        true,
                        settings.CapsAction,
                        settings.CapsDurationSeconds,
                        "Excessive capital letters"
                    );
                }
            }
        }

        // 4. Banned phrases
        if (settings.BannedPhrases.Count > 0)
        {
            foreach (string phrase in settings.BannedPhrases)
            {
                bool match = settings.BannedPhrasesUseRegex
                    ? IsRegexMatch(message, phrase)
                    : message.Contains(phrase, StringComparison.OrdinalIgnoreCase);

                if (match)
                {
                    _logger.LogDebug(
                        "AutoMod: banned phrase matched from {UserId} in {BroadcasterId}",
                        userId,
                        broadcasterId
                    );
                    return new(
                        true,
                        settings.BannedPhrasesAction,
                        settings.BannedPhrasesDurationSeconds,
                        "Banned phrase detected"
                    );
                }
            }
        }

        return new(false, AutoModAction.None, 0, string.Empty);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsAllowedLink(string message, HashSet<string> allowedDomains)
    {
        if (allowedDomains.Count == 0)
            return false;

        foreach (Match match in UrlPattern.Matches(message))
        {
            Match hostMatch = HostnamePattern.Match(match.Value);
            if (!hostMatch.Success)
                continue;

            string hostname = hostMatch.Groups[1].Value.ToLowerInvariant();

            // Strip common subdomains for matching
            string[] parts = hostname.Split('.');
            string domain = parts.Length >= 2 ? string.Join('.', parts[^2..]) : hostname;

            if (!allowedDomains.Contains(hostname) && !allowedDomains.Contains(domain))
                return false;
        }

        return true;
    }

    private static bool IsRegexMatch(string message, string pattern)
    {
        try
        {
            return Regex.IsMatch(
                message,
                pattern,
                RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100)
            );
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid regex — fall back to literal match
            return message.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
