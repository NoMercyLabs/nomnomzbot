// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// The live overlay attachment map: which widget groups each <c>OverlayHub</c> connection has joined. The hub
/// owns the writes (join / leave / disconnect); everything that needs to know whether a browser source is
/// actually listening reads it through <see cref="IOverlayPresenceRegistry"/>.
/// <para>
/// A single browser source can host many widgets on one page, and one widget can be open in several sources,
/// so this is a set per connection and attachment is "any connection holds it".
/// </para>
/// </summary>
public sealed class OverlayPresenceRegistry : IOverlayPresenceRegistry
{
    private readonly ConcurrentDictionary<
        string,
        ConcurrentDictionary<string, byte>
    > _connectionWidgets = new(StringComparer.Ordinal);

    public void Attach(string connectionId, string groupName) =>
        _connectionWidgets
            .GetOrAdd(connectionId, static _ => new(StringComparer.Ordinal))
            .TryAdd(groupName, 0);

    public void Detach(string connectionId, string groupName)
    {
        if (
            _connectionWidgets.TryGetValue(
                connectionId,
                out ConcurrentDictionary<string, byte>? groups
            )
        )
            groups.TryRemove(groupName, out _);
    }

    /// <summary>Drops a whole connection, returning the groups it held so the hub can leave each one.</summary>
    public IReadOnlyCollection<string> Drop(string connectionId) =>
        _connectionWidgets.TryRemove(connectionId, out ConcurrentDictionary<string, byte>? groups)
            ? groups.Keys.ToArray()
            : [];

    public IReadOnlyCollection<string> GroupsFor(string connectionId) =>
        _connectionWidgets.TryGetValue(connectionId, out ConcurrentDictionary<string, byte>? groups)
            ? groups.Keys.ToArray()
            : [];

    public static string GroupName(Guid broadcasterId, string widgetId) =>
        $"widget-{broadcasterId}-{widgetId}";

    public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId)
    {
        string groupName = GroupName(broadcasterId, widgetId.ToString());
        foreach (ConcurrentDictionary<string, byte> groups in _connectionWidgets.Values)
            if (groups.ContainsKey(groupName))
                return true;
        return false;
    }
}
