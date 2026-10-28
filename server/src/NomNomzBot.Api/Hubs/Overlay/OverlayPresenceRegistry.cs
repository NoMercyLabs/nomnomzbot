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

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
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

    /// <summary>The canonical gallery key of the Audio page — the one widget every sound and TTS line plays on.</summary>
    public const string AudioSourceNaturalKey = WidgetKeyAliases.Audio;

    /// <summary>The gallery key of the TTS page — spoken messages play there when it is open.</summary>
    public const string TtsSourceNaturalKey = "tts_caption";

    private sealed record OverlayConnection(
        Guid BroadcasterId,
        long Order,
        bool IsAudioSource,
        Guid? AudioWidgetId,
        bool IsTtsSource = false
    );

    /// <summary>A widget-scoped connection and the exact token it connected with, kept so a rotation can close it.</summary>
    public sealed record BoundConnection(
        string ConnectionId,
        Guid WidgetId,
        string Token,
        HubCallerContext Context
    );

    private readonly ConcurrentDictionary<string, BoundConnection> _bound = new(
        StringComparer.Ordinal
    );

    /// <summary>Remembers the widget token a widget-scoped connection was admitted with.</summary>
    public void BindToken(
        string connectionId,
        Guid widgetId,
        string token,
        HubCallerContext context
    ) => _bound[connectionId] = new(connectionId, widgetId, token, context);

    /// <summary>The token-bound connections of one widget, or of every widget when null.</summary>
    public IReadOnlyList<BoundConnection> BoundConnections(Guid? widgetId) =>
        _bound.Values.Where(b => widgetId is null || b.WidgetId == widgetId).ToArray();

    private readonly object _audioLock = new();
    private readonly Dictionary<string, OverlayConnection> _overlays = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MusicConnection> _music = new(StringComparer.Ordinal);
    private long _order;

    private sealed record MusicConnection(Guid BroadcasterId, long Order, Guid WidgetId);

    /// <summary>Records a new overlay connection, in join order, for audio routing.</summary>
    public void RegisterOverlay(string connectionId, Guid broadcasterId)
    {
        lock (_audioLock)
            _overlays[connectionId] = new(broadcasterId, ++_order, false, null);
    }

    /// <summary>Marks a connection as an Audio source page; it becomes the newest one.</summary>
    public void MarkAudioSource(string connectionId, Guid? widgetId = null)
    {
        lock (_audioLock)
            if (_overlays.TryGetValue(connectionId, out OverlayConnection? current))
                _overlays[connectionId] = current with
                {
                    Order = ++_order,
                    IsAudioSource = true,
                    AudioWidgetId = widgetId,
                };
    }

    /// <summary>Marks a connection as a TTS page; it becomes the newest one.</summary>
    public void MarkTtsSource(string connectionId)
    {
        lock (_audioLock)
            if (_overlays.TryGetValue(connectionId, out OverlayConnection? current))
                _overlays[connectionId] = current with { Order = ++_order, IsTtsSource = true };
    }

    public string? GetTtsTarget(Guid broadcasterId)
    {
        lock (_audioLock)
        {
            KeyValuePair<string, OverlayConnection>[] sources = _overlays
                .Where(o => o.Value.BroadcasterId == broadcasterId && o.Value.IsTtsSource)
                .ToArray();
            return sources.Length == 0 ? null : sources.MaxBy(o => o.Value.Order).Key;
        }
    }

    public bool IsTtsSourceConnected(Guid broadcasterId)
    {
        lock (_audioLock)
            return _overlays.Values.Any(o => o.BroadcasterId == broadcasterId && o.IsTtsSource);
    }

    public string? GetAudioTarget(Guid broadcasterId)
    {
        lock (_audioLock)
        {
            KeyValuePair<string, OverlayConnection>[] sources = _overlays
                .Where(o => o.Value.BroadcasterId == broadcasterId && o.Value.IsAudioSource)
                .ToArray();
            return sources.Length == 0 ? null : sources.MaxBy(o => o.Value.Order).Key;
        }
    }

    public Guid? GetAudioWidgetId(Guid broadcasterId)
    {
        lock (_audioLock)
            return _overlays
                .Values.Where(o => o.BroadcasterId == broadcasterId && o.IsAudioSource)
                .OrderByDescending(o => o.Order)
                .Select(o => o.AudioWidgetId)
                .FirstOrDefault();
    }

    public bool IsAudioSourceConnected(Guid broadcasterId)
    {
        lock (_audioLock)
            return _overlays.Values.Any(o => o.BroadcasterId == broadcasterId && o.IsAudioSource);
    }

    public string? GetMusicTarget(Guid broadcasterId)
    {
        lock (_audioLock)
            return _music
                .Where(m => m.Value.BroadcasterId == broadcasterId)
                .OrderByDescending(m => m.Value.Order)
                .Select(m => m.Key)
                .FirstOrDefault();
    }

    public Guid? GetMusicWidgetId(Guid broadcasterId)
    {
        lock (_audioLock)
            return _music
                .Values.Where(m => m.BroadcasterId == broadcasterId)
                .OrderByDescending(m => m.Order)
                .Select(m => (Guid?)m.WidgetId)
                .FirstOrDefault();
    }

    public bool IsMusicSourceConnected(Guid broadcasterId)
    {
        lock (_audioLock)
            return _music.Values.Any(m => m.BroadcasterId == broadcasterId);
    }

    /// <summary>
    /// Marks a connection as the Music Player page; it becomes the newest one. Returns the music connection it
    /// displaced, or null when no other connection held the role.
    /// </summary>
    public (string ConnectionId, Guid? WidgetId)? MarkMusicSource(
        string connectionId,
        Guid widgetId
    )
    {
        lock (_audioLock)
        {
            if (!_overlays.TryGetValue(connectionId, out OverlayConnection? current))
                return null;
            KeyValuePair<string, MusicConnection>? newestOther = _music
                .Where(m => m.Value.BroadcasterId == current.BroadcasterId && m.Key != connectionId)
                .OrderByDescending(m => m.Value.Order)
                .Select(m => (KeyValuePair<string, MusicConnection>?)m)
                .FirstOrDefault();
            _music[connectionId] = new(current.BroadcasterId, ++_order, widgetId);
            return newestOther is { } other ? (other.Key, other.Value.WidgetId) : null;
        }
    }

    /// <summary>Records the group on the connection; true only when it was not already held.</summary>
    public bool Attach(string connectionId, string groupName) =>
        _connectionWidgets
            .GetOrAdd(connectionId, static _ => new(StringComparer.Ordinal))
            .TryAdd(groupName, 0);

    /// <summary>Forgets the group on the connection; true only when it was held.</summary>
    public bool Detach(string connectionId, string groupName) =>
        _connectionWidgets.TryGetValue(connectionId, out ConcurrentDictionary<string, byte>? groups)
        && groups.TryRemove(groupName, out _);

    /// <summary>Drops a whole connection, returning the groups it held so the hub can leave each one.</summary>
    public IReadOnlyCollection<string> Drop(string connectionId)
    {
        lock (_audioLock)
        {
            _overlays.Remove(connectionId);
            _music.Remove(connectionId);
        }
        _bound.TryRemove(connectionId, out _);
        return _connectionWidgets.TryRemove(
            connectionId,
            out ConcurrentDictionary<string, byte>? groups
        )
            ? groups.Keys.ToArray()
            : [];
    }

    public IReadOnlyCollection<string> GroupsFor(string connectionId) =>
        _connectionWidgets.TryGetValue(connectionId, out ConcurrentDictionary<string, byte>? groups)
            ? groups.Keys.ToArray()
            : [];

    public static string GroupName(Guid broadcasterId, string widgetId) =>
        $"widget-{broadcasterId}-{widgetId}";

    /// <summary>Maps a widget group name back to its widget id; false for any other group or a non-Guid id.</summary>
    public static bool TryParseWidgetId(Guid broadcasterId, string groupName, out Guid widgetId)
    {
        string prefix = GroupName(broadcasterId, string.Empty);
        if (groupName.StartsWith(prefix, StringComparison.Ordinal))
            return Guid.TryParse(groupName.AsSpan(prefix.Length), out widgetId);
        widgetId = Guid.Empty;
        return false;
    }

    /// <summary>The broadcaster-wide group every overlay connection joins on connect (<c>OverlayHub.OnConnectedAsync</c>).</summary>
    public static string OverlayGroupName(Guid broadcasterId) => $"overlay-{broadcasterId}";

    /// <summary>The generic event feed: only connections that host no widget, so a widget never gets an event twice.</summary>
    public static string FeedGroupName(Guid broadcasterId) => $"overlay-feed-{broadcasterId}";

    public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) =>
        IsGroupAttached(GroupName(broadcasterId, widgetId.ToString()));

    public bool IsOverlayConnected(Guid broadcasterId) =>
        IsGroupAttached(OverlayGroupName(broadcasterId));

    private bool IsGroupAttached(string groupName)
    {
        foreach (ConcurrentDictionary<string, byte> groups in _connectionWidgets.Values)
            if (groups.ContainsKey(groupName))
                return true;
        return false;
    }
}
