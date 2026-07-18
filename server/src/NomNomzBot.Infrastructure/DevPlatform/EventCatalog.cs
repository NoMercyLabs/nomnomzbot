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
using System.Text;
using System.Text.RegularExpressions;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// The reflected Event Catalog (dev-platform.md §1.2) — built once at startup by scanning the Domain assembly
/// for every concrete <see cref="IDomainEvent"/> and turning each into an <see cref="EventDescriptor"/>. The
/// record IS the schema; the only manual inputs are the optional <c>[Event]</c> attribute (wire name + tier)
/// and the field-level <c>[Pii]</c>/<c>[NotExposed]</c> attributes the emitter reads. Fails fast on a duplicate
/// wire name, exactly like the <c>AutomationEventRegistry</c> (which this does NOT replace — this is additive).
/// </summary>
public sealed partial class EventCatalog : IEventCatalog
{
    public EventCatalog()
        : this(typeof(IDomainEvent).Assembly) { }

    // Assembly-injectable so tests can point the same discovery at a fixture assembly.
    public EventCatalog(Assembly domainAssembly)
    {
        List<EventDescriptor> discovered = [];
        foreach (Type type in domainAssembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IDomainEvent).IsAssignableFrom(type))
                continue;

            EventAttribute? attribute = type.GetCustomAttribute<EventAttribute>(inherit: false);
            string wireName = attribute?.WireName ?? DeriveWireName(type);
            EventVisibility visibility = attribute?.Visibility ?? EventVisibility.Broadcaster;
            discovered.Add(new EventDescriptor(wireName, visibility, type));
        }

        List<string> duplicateNames =
        [
            .. discovered
                .GroupBy(d => d.WireName, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
        ];
        if (duplicateNames.Count > 0)
            throw new InvalidOperationException(
                "Duplicate event wire name(s) in the Event Catalog: "
                    + $"{string.Join(", ", duplicateNames)}. Pin one with [Event(\"…\")]."
            );

        Descriptors = [.. discovered.OrderBy(d => d.WireName, StringComparer.Ordinal)];
    }

    public IReadOnlyList<EventDescriptor> Descriptors { get; }

    /// <summary>
    /// The wire-name convention when a record carries no <c>[Event("…")]</c> override (dev-platform.md §1.2):
    /// <list type="number">
    ///   <item><c>module</c> = the namespace segment immediately after <c>NomNomzBot.Domain.</c>
    ///   (e.g. <c>Chat</c>, <c>Stream</c>), lowercased; <c>event</c> if the type is outside that root.</item>
    ///   <item><c>words</c> = the type name with a trailing <c>Event</c> removed, split on PascalCase/acronym
    ///   boundaries, each lowercased.</item>
    ///   <item>if <c>words[0] == module</c> the leading word is dropped (no <c>chat.chat.*</c> stutter).</item>
    ///   <item>the name is <c>module</c> joined to the remaining words with <c>.</c>.</item>
    /// </list>
    /// Examples: <c>ChannelOnlineEvent</c> (Stream) → <c>stream.channel.online</c>;
    /// <c>RaidReceivedEvent</c> (Stream) → <c>stream.raid.received</c>. A <c>[Event("chat.message")]</c>
    /// override bypasses all of this.
    /// </summary>
    public static string DeriveWireName(Type type)
    {
        string module = ModuleOf(type.Namespace);

        string bareName = type.Name;
        if (
            bareName.EndsWith("Event", StringComparison.Ordinal)
            && bareName.Length > "Event".Length
        )
            bareName = bareName[..^"Event".Length];

        List<string> words =
        [
            .. WordBoundary()
                .Split(bareName)
                .Where(w => w.Length > 0)
                .Select(w => w.ToLowerInvariant()),
        ];

        if (words.Count > 0 && string.Equals(words[0], module, StringComparison.Ordinal))
            words.RemoveAt(0);

        StringBuilder sb = new(module);
        foreach (string word in words)
        {
            sb.Append('.');
            sb.Append(word);
        }
        return sb.ToString();
    }

    private static string ModuleOf(string? ns)
    {
        const string root = "NomNomzBot.Domain.";
        if (ns is null || !ns.StartsWith(root, StringComparison.Ordinal))
            return "event";

        string remainder = ns[root.Length..];
        int dot = remainder.IndexOf('.', StringComparison.Ordinal);
        string segment = dot < 0 ? remainder : remainder[..dot];
        return segment.ToLowerInvariant();
    }

    // Inserts a boundary between a lower/digit and an upper, and at the end of an acronym run (an upper
    // followed by an Upper-then-lower) — the standard PascalCase splitter.
    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBoundary();
}
