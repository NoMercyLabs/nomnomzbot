// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The Event Catalog offers every <c>IDomainEvent</c> as a script trigger, so an event nothing ever
/// raises is a trigger that can never fire (the raid earning once sat on an event the Twitch raid
/// translator never published). This guard reads the real
/// source under <c>server/src</c> (outside the Domain project) and fails when a catalogued event has no
/// construction site, unless it is in the explicit allowlist below. The allowlist only shrinks: an
/// allowlisted event that IS raised fails the test until it is taken off the list.
/// </summary>
public sealed class EventCatalogRaisedEventsGuardTests
{
    // Plan slice S-EVENTS-NEVER-RAISED: each of these is offered as a trigger but nothing raises it yet.
    // Raise it (or remove the event), then delete the name from this list.
    private static readonly HashSet<string> NeverRaisedAllowlist = new(StringComparer.Ordinal)
    {
        "AfterRewardProcessedEvent",
        "BeforeRewardProcessedEvent",
        "ChannelJoinedEvent",
        "ChannelLeftEvent",
        "ChannelSuspendedEvent",
        "ChannelReinstatedEvent",
        "CommandFailedEvent",
        "EventPayloadShreddedEvent",
        "FeatureToggledEvent",
        "FederatedEventDispatchedEvent",
        "IntegrationErrorEvent",
        "MessageAutoModdedEvent",
        "PermissionChangedEvent",
        "ReplayStatusChangedEvent",
        "RewardRefundedEvent",
        "SongSkippedEvent",
        "StreamStatusChangedEvent",
        "TrackChangedEvent",
        "UserFirstChatEvent",
        "ViewerRowAbsorbedEvent",
        "WidgetConnectedEvent",
        "WidgetDisconnectedEvent",
    };

    [Fact]
    public void Every_catalogued_event_is_raised_somewhere_or_is_on_the_allowlist()
    {
        string source = ReadAllNonDomainSource();
        List<string> catalogued =
        [
            .. new EventCatalog()
                .Descriptors.Select(d => d.ClrType.Name)
                .Distinct(StringComparer.Ordinal),
        ];
        catalogued.Should().NotBeEmpty("the scan is broken if the catalog is empty");

        List<string> unraised =
        [
            .. catalogued
                .Where(name => !NeverRaisedAllowlist.Contains(name))
                .Where(name => !IsConstructed(source, name)),
        ];

        unraised
            .Should()
            .BeEmpty(
                "every catalogued event must be raised somewhere in server/src, or sit on the "
                    + "S-EVENTS-NEVER-RAISED allowlist; a trigger nothing raises can never fire"
            );
    }

    [Fact]
    public void The_allowlist_only_holds_catalogued_events_that_are_still_never_raised()
    {
        string source = ReadAllNonDomainSource();
        HashSet<string> catalogued = new(
            new EventCatalog().Descriptors.Select(d => d.ClrType.Name),
            StringComparer.Ordinal
        );

        List<string> notCatalogued = [.. NeverRaisedAllowlist.Where(n => !catalogued.Contains(n))];
        List<string> nowRaised = [.. NeverRaisedAllowlist.Where(n => IsConstructed(source, n))];

        notCatalogued.Should().BeEmpty("an allowlisted name must be a real catalogued event");
        nowRaised
            .Should()
            .BeEmpty("a raised event must come off the allowlist so the list only shrinks");
    }

    // Counts `new Name`, a target-typed `Name x = new(...)`, and `Name M(...) => new(...)`.
    private static bool IsConstructed(string source, string eventName)
    {
        string name = Regex.Escape(eventName);
        return Regex.IsMatch(source, $@"\bnew\s+{name}\b")
            || Regex.IsMatch(source, $@"\b{name}\??\s+\w+\s*=\s*new\s*\(")
            || Regex.IsMatch(source, $@"\b{name}\??\s+\w+\s*\([^)]*\)\s*=>\s*new\s*\(");
    }

    private static string ReadAllNonDomainSource([CallerFilePath] string thisFilePath = "")
    {
        string serverRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", "..")
        );
        string srcRoot = Path.Combine(serverRoot, "src");
        Directory.Exists(srcRoot).Should().BeTrue($"expected the real source at {srcRoot}");

        string domainRoot =
            Path.Combine(srcRoot, "NomNomzBot.Domain") + Path.DirectorySeparatorChar;
        string binRoot = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
        string objRoot = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;

        List<string> files =
        [
            .. Directory
                .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                    !f.StartsWith(domainRoot, StringComparison.OrdinalIgnoreCase)
                    && !f.Contains(binRoot, StringComparison.Ordinal)
                    && !f.Contains(objRoot, StringComparison.Ordinal)
                ),
        ];
        files.Should().NotBeEmpty("the scan is broken if it reads no source");

        return string.Join(
            "\n",
            files.SelectMany(File.ReadLines).Where(l => !l.TrimStart().StartsWith("//"))
        );
    }
}
