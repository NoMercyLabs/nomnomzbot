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

namespace NomNomzBot.Application.Games;

/// <summary>
/// The drop-in game contract (live-games.md §4). A game is PURE LOGIC: it reads the engine-supplied
/// <see cref="LiveGameState"/>, mutates only its own <c>Data</c> bag, and returns a transition. It never
/// touches the DB, currency, chat, tokens, or SignalR — the engine brokers all of that. Adding a game is a
/// new implementor (auto-discovered) plus its overlay widget plus a default config — zero engine edits.
/// </summary>
public interface ILiveGame
{
    /// <summary>Unique key, e.g. <c>drop_game</c> — equals the <c>GameConfig.GameType</c> that configures it.</summary>
    string GameKey { get; }

    LiveGameManifest Manifest { get; }

    Task<LiveGameTransition> OnStartAsync(LiveGameState state, CancellationToken ct);

    Task<LiveGameTransition> OnInputAsync(
        LiveGameState state,
        LiveGameInput input,
        CancellationToken ct
    );

    /// <summary>Called every <see cref="LiveGameManifest.TickInterval"/>; no-op allowed for non-timed games.</summary>
    Task<LiveGameTransition> OnTickAsync(LiveGameState state, CancellationToken ct);

    Task<LiveGameResolution> OnResolveAsync(LiveGameState state, CancellationToken ct);
}

/// <summary>
/// How the engine runs a game: its join keywords, overlay widget, player bounds, and timing. An
/// <paramref name="AutoStartOnInput"/> game needs no start: the first join keyword opens a round on a
/// channel that shows the game's overlay widget, and the round closes on its own after the lobby window.
/// </summary>
public sealed record LiveGameManifest(
    string DisplayName,
    IReadOnlyList<string> InputKeywords,
    string OverlayWidgetKey,
    int MinPlayers,
    int MaxPlayers,
    TimeSpan LobbyWindow,
    TimeSpan? TickInterval,
    bool RequiresEntryFee,
    bool AutoStartOnInput = false
);

/// <summary>
/// The per-channel timing a streamer may set on any live game (<c>GameConfig.ConfigJson</c>):
/// <c>round_s</c> replaces the manifest's lobby window, <c>restart_cooldown_s</c> is the wait after a round
/// ends before a join keyword may open the next one.
/// </summary>
public static class LiveGameTiming
{
    public const string RoundSecondsKey = "round_s";
    public const string RestartCooldownSecondsKey = "restart_cooldown_s";

    /// <summary>The default wait between two self-started rounds when the channel has set none.</summary>
    public static readonly TimeSpan DefaultRestartCooldown = TimeSpan.FromSeconds(30);

    public static TimeSpan LobbyWindow(
        LiveGameManifest manifest,
        IReadOnlyDictionary<string, object?>? config
    ) => Seconds(config, RoundSecondsKey, minimum: 1) ?? manifest.LobbyWindow;

    public static TimeSpan RestartCooldown(IReadOnlyDictionary<string, object?>? config) =>
        Seconds(config, RestartCooldownSecondsKey, minimum: 0) ?? DefaultRestartCooldown;

    private static TimeSpan? Seconds(
        IReadOnlyDictionary<string, object?>? config,
        string key,
        int minimum
    ) =>
        config is not null
        && config.TryGetValue(key, out object? raw)
        && int.TryParse(
            raw?.ToString(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int seconds
        )
        && seconds >= minimum
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
