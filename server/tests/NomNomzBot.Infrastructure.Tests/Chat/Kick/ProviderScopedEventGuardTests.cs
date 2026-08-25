// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Structural guard for S022: every canonical community/monetization domain event that BOTH Twitch
/// EventSub and Kick's webhook ingest publish onto the SAME type (supporter-events.md §4.1) must carry
/// <see cref="IProviderScopedEvent.Provider"/> — without it a Kick delivery of, say,
/// <c>NewSubscriptionEvent</c> is byte-for-byte indistinguishable from a Twitch one and per-platform
/// display/behavior (and the S022 done-when) is impossible.
///
/// The set of "canonical cross-platform events" is enumerated STRUCTURALLY, not hand-typed: this test
/// reads the real, on-disk <c>KickWebhookIngest.cs</c> — the one file whose entire job is mapping Kick's
/// wire payloads onto Twitch's canonical event types — and regex-extracts every <c>new XxxEvent { ... }</c>
/// construction site, resolves each name against the compiled Domain assembly, and asserts it implements
/// <see cref="IProviderScopedEvent"/>. Add a new Kick-mapped event to that file and forget
/// <c>IProviderScopedEvent</c> and this test names it and fails — no list to remember to update.
/// </summary>
public sealed class ProviderScopedEventGuardTests
{
    [Fact]
    public void Every_canonical_event_KickWebhookIngest_constructs_is_provider_scoped()
    {
        string sourcePath = ResolveKickWebhookIngestSourcePath();
        File.Exists(sourcePath).Should().BeTrue($"expected the real source file at {sourcePath}");
        string source = File.ReadAllText(sourcePath);

        // Matches `new FollowEvent`, `new NewSubscriptionEvent { ... }`, etc. — every domain-event
        // construction site in the file, keyed off the "Event" suffix convention every domain event in
        // this codebase follows (DomainEventBase subclasses are all named `*Event`).
        MatchCollection matches = Regex.Matches(
            source,
            @"new\s+([A-Z][A-Za-z0-9]*Event)\b\s*[\{\(]"
        );
        matches
            .Count.Should()
            .BeGreaterThan(
                0,
                "the scan itself is broken if it finds zero construction sites in a file whose job is publishing canonical events"
            );

        Assembly domainAssembly = typeof(DomainEventBase).Assembly;
        HashSet<string> eventTypeNames = matches
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        List<string> missingProvider = new();
        List<string> unresolved = new();
        foreach (string typeName in eventTypeNames)
        {
            Type? domainEventType = domainAssembly
                .GetTypes()
                .FirstOrDefault(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && typeof(DomainEventBase).IsAssignableFrom(t)
                    && t.Name == typeName
                );

            if (domainEventType is null)
            {
                // Not every `new XxxEvent` match is a domain event (e.g. a nested record/local type
                // that happens to end in "Event") — only flag it as unresolved if nothing in the Domain
                // assembly matches; genuinely canonical events always resolve.
                unresolved.Add(typeName);
                continue;
            }

            if (!typeof(IProviderScopedEvent).IsAssignableFrom(domainEventType))
                missingProvider.Add(typeName);
        }

        missingProvider
            .Should()
            .BeEmpty(
                "every canonical event Kick publishes onto the same domain type Twitch uses must implement "
                    + "IProviderScopedEvent, or Kick's delivery is indistinguishable from Twitch's"
            );

        // KickWebhookIngest is known (from the audit backing S022) to publish exactly these DomainEventBase
        // types today: FollowEvent, NewSubscriptionEvent, ResubscriptionEvent, GiftSubscriptionEvent,
        // CheerEvent, ChannelUpdatedEvent, ChatMessageReceivedEvent — asserting the resolved set is
        // non-empty and none of the known five are unresolved keeps the scan itself honest.
        unresolved
            .Should()
            .NotContain(
                [
                    "FollowEvent",
                    "NewSubscriptionEvent",
                    "ResubscriptionEvent",
                    "GiftSubscriptionEvent",
                    "CheerEvent",
                ],
                "these are known DomainEventBase types the scan must resolve — an unresolved hit here means the regex or the Domain assembly lookup broke, not that the event stopped being canonical"
            );
    }

    private static string ResolveKickWebhookIngestSourcePath(
        [CallerFilePath] string thisFilePath = ""
    )
    {
        // thisFilePath is .../server/tests/NomNomzBot.Infrastructure.Tests/Chat/Kick/ThisFile.cs — walk up
        // to the `server` root then down into the Infrastructure project, so the test finds the real
        // source regardless of the CI runner's working directory.
        string testsChatKickDir = Path.GetDirectoryName(thisFilePath)!;
        string serverRoot = Path.GetFullPath(
            Path.Combine(testsChatKickDir, "..", "..", "..", "..")
        );
        return Path.Combine(
            serverRoot,
            "src",
            "NomNomzBot.Infrastructure",
            "Chat",
            "Kick",
            "KickWebhookIngest.cs"
        );
    }
}
