// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.RateLimiting;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

public class AutoModerationEngineTests
{
    private static AutoModerationEngine Create()
    {
        CooldownManager cooldowns = new(TimeProvider.System);
        NullLogger<AutoModerationEngine> logger = NullLogger<AutoModerationEngine>.Instance;
        return new(cooldowns, logger);
    }

    private static AutoModResult Evaluate(
        AutoModerationEngine engine,
        string message,
        AutoModSettings settings,
        string[]? roles = null,
        string broadcasterId = "chan",
        string userId = "user1"
    ) => engine.Evaluate(broadcasterId, userId, message, roles ?? [], settings);

    // ─── No action ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CleanMessage_ReturnsNoAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new();

        AutoModResult result = Evaluate(engine, "hello everyone", settings);

        result.ShouldAct.Should().BeFalse();
        result.Action.Should().Be(AutoModAction.None);
    }

    // ─── Exempt roles ─────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ExemptRole_BypassesAllChecks()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterLinks = true, ExemptRoles = ["moderator"] };

        AutoModResult result = engine.Evaluate(
            "chan",
            "mod1",
            "https://badlink.com",
            ["moderator"],
            settings
        );

        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NonExemptRole_NotBypassed()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterLinks = true, ExemptRoles = ["moderator"] };

        AutoModResult result = engine.Evaluate(
            "chan",
            "viewer1",
            "https://badlink.com",
            ["viewer"],
            settings
        );

        result.ShouldAct.Should().BeTrue();
    }

    // ─── Slow mode ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_SlowMode_FirstMessage_NotViolation()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { SlowModeSeconds = 5 };

        // First message: no cooldown, so it's allowed (cooldown is SET after)
        AutoModResult result = engine.Evaluate("chan", "user1", "hello", [], settings);

        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_SlowMode_SecondMessageImmediately_IsViolation()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            SlowModeSeconds = 60,
            SlowModeAction = AutoModAction.Delete,
        };

        engine.Evaluate("chan", "user1", "first", [], settings); // sets cooldown
        AutoModResult result = engine.Evaluate("chan", "user1", "second", [], settings); // hits cooldown

        result.ShouldAct.Should().BeTrue();
        result.Action.Should().Be(AutoModAction.Delete);
        result.Reason.Should().Contain("Slow mode");
    }

    // ─── Link filtering ───────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_LinkFilter_PlainText_NoAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterLinks = true };

        AutoModResult result = Evaluate(engine, "this has no link", settings);
        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_LinkFilter_HttpsLink_TriggersAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterLinks = true, LinkAction = AutoModAction.Delete };

        AutoModResult result = Evaluate(engine, "check out https://evil.com/hack", settings);

        result.ShouldAct.Should().BeTrue();
        result.Action.Should().Be(AutoModAction.Delete);
        result.Reason.Should().ContainEquivalentOf("link");
    }

    [Fact]
    public void Evaluate_LinkFilter_WwwLink_TriggersAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterLinks = true, LinkAction = AutoModAction.Delete };

        AutoModResult result = Evaluate(engine, "visit www.example.com today", settings);

        result.ShouldAct.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_LinkFilter_AllowedDomain_NoAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            FilterLinks = true,
            AllowedDomains = ["twitch.tv"],
            LinkAction = AutoModAction.Delete,
        };

        AutoModResult result = Evaluate(engine, "watch at https://twitch.tv/streamer", settings);

        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_LinkFilter_Disabled_AllowsLinks()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterLinks = false };

        AutoModResult result = Evaluate(engine, "https://any-site.com", settings);
        result.ShouldAct.Should().BeFalse();
    }

    // ─── Caps detection ───────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CapsFilter_BelowThreshold_NoAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            FilterCaps = true,
            CapsThresholdPercent = 70,
            CapsMinLength = 4,
        };

        // "Hello" → 1/5 = 20% uppercase, below threshold
        AutoModResult result = Evaluate(engine, "Hello there", settings);
        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_CapsFilter_AboveThreshold_TriggersAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            FilterCaps = true,
            CapsThresholdPercent = 70,
            CapsMinLength = 4,
            CapsAction = AutoModAction.Timeout,
            CapsDurationSeconds = 30,
        };

        AutoModResult result = Evaluate(engine, "STOP SPAMMING RIGHT NOW", settings);

        result.ShouldAct.Should().BeTrue();
        result.Action.Should().Be(AutoModAction.Timeout);
        result.DurationSeconds.Should().Be(30);
        result.Reason.Should().Contain("capital");
    }

    [Fact]
    public void Evaluate_CapsFilter_ShortMessage_NotChecked()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterCaps = true, CapsMinLength = 8 };

        // "LOL" is 3 chars, below CapsMinLength=8
        AutoModResult result = Evaluate(engine, "LOL", settings);
        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_CapsFilter_Disabled_NoCapsCheck()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { FilterCaps = false };

        AutoModResult result = Evaluate(engine, "YELLING IN ALL CAPS", settings);
        result.ShouldAct.Should().BeFalse();
    }

    // ─── Banned phrases ───────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_BannedPhrase_ExactMatch_TriggersAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            BannedPhrases = ["bad word"],
            BannedPhrasesAction = AutoModAction.Timeout,
            BannedPhrasesDurationSeconds = 60,
        };

        AutoModResult result = Evaluate(engine, "this has a bad word in it", settings);

        result.ShouldAct.Should().BeTrue();
        result.Action.Should().Be(AutoModAction.Timeout);
        result.DurationSeconds.Should().Be(60);
        result.Reason.Should().Contain("Banned phrase");
    }

    [Fact]
    public void Evaluate_BannedPhrase_CaseInsensitive_TriggersAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { BannedPhrases = ["spam"] };

        AutoModResult result = Evaluate(engine, "This is SPAM here", settings);
        result.ShouldAct.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_BannedPhrase_NotInMessage_NoAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { BannedPhrases = ["forbidden"] };

        AutoModResult result = Evaluate(engine, "This is fine", settings);
        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_BannedPhrase_RegexMode_MatchesPattern()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            BannedPhrases = [@"\b(buy|cheap|discount)\b"],
            BannedPhrasesUseRegex = true,
        };

        AutoModResult result = Evaluate(engine, "Get cheap pills here", settings);
        result.ShouldAct.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_BannedPhrase_InvalidRegex_FallsBackToLiteralMatch()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            BannedPhrases = ["[invalid regex"],
            BannedPhrasesUseRegex = true,
        };

        // Doesn't contain literal "[invalid regex" so no action
        AutoModResult result = Evaluate(engine, "hello world", settings);
        result.ShouldAct.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_BannedPhrase_InvalidRegex_LiteralMatchHits()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            BannedPhrases = ["[invalid regex"],
            BannedPhrasesUseRegex = true,
        };

        // Contains the literal text "[invalid regex"
        AutoModResult result = Evaluate(engine, "here is [invalid regex in message", settings);
        result.ShouldAct.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NoBannedPhrases_NoAction()
    {
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new() { BannedPhrases = [] };

        AutoModResult result = Evaluate(engine, "anything goes", settings);
        result.ShouldAct.Should().BeFalse();
    }

    // ─── Priority order ───────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_SlowModeViolation_TakesPriorityOverLinks()
    {
        // Slow mode is checked first, so a slow-mode violation is returned
        AutoModerationEngine engine = Create();
        AutoModSettings settings = new()
        {
            SlowModeSeconds = 60,
            SlowModeAction = AutoModAction.Delete,
            FilterLinks = true,
            LinkAction = AutoModAction.Ban,
        };

        engine.Evaluate("chan", "user1", "first", [], settings); // set cooldown
        AutoModResult result = engine.Evaluate("chan", "user1", "https://evil.com", [], settings);

        result.Action.Should().Be(AutoModAction.Delete); // slow mode action, not ban
        result.Reason.Should().Contain("Slow mode");
    }
}
