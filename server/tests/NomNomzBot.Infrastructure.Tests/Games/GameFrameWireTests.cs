// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Games;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Games.Catalog;
using NomNomzBot.Infrastructure.Games.Frames;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Holds the typed frame records (what the editor shows a widget author for <c>game.lobby</c>, <c>game.running</c>
/// and <c>game.resolved</c>) to the frames the four games really push. Each game is driven through a whole round;
/// every frame it emits must name a known <c>kind</c>, carry only keys its record has, and carry every key its
/// record does not mark nullable. A game that adds, renames or drops a key fails here, so the types cannot drift.
/// </summary>
public sealed class GameFrameWireTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000b1");

    private static readonly Type[] AllFrames =
    [
        typeof(GameRoundOpenFrame),
        typeof(GameJoinFrame),
        typeof(GameMultiplierFrame),
        typeof(GameCashoutFrame),
        typeof(GameDropFrame),
        typeof(GameResultsFrame),
        typeof(GameCancelledFrame),
    ];

    private sealed class Sequence(params double[] values) : IGameRandom
    {
        private readonly Queue<double> _values = new(values);
        private double _last = values[^1];

        public double NextDouble()
        {
            if (_values.Count > 0)
                _last = _values.Dequeue();
            return _last;
        }

        public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);

        public bool Roll(double percent) => NextDouble() * 100.0 < percent;
    }

    private static LiveGameParticipant Player(string name, long stake) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), name, stake);

    private static LiveGameState State(
        IGameRandom random,
        List<LiveGameParticipant> participants,
        LiveGamePhase phase,
        IDictionary<string, object?> data
    ) =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Config = new(10, 1000, null, null),
            Participants = participants,
            Phase = phase,
            Data = data,
            Random = random,
        };

    private static LiveGameInput Input(LiveGameParticipant player) =>
        new(player, "!play", [], "!play");

    // Runs one game through its whole round and returns every frame it pushed, in order.
    private static async Task<List<Dictionary<string, object?>>> DriveAsync(
        ILiveGame game,
        IGameRandom random,
        int ticksBefore,
        int ticksAfter,
        bool lateJoinAndCashOut
    )
    {
        LiveGameParticipant alice = Player("Alice", 40);
        LiveGameParticipant bob = Player("Bob", 20);
        LiveGameParticipant carol = Player("Carol", 30);
        List<LiveGameParticipant> roster = [];
        LiveGameState state = State(
            random,
            roster,
            LiveGamePhase.Lobby,
            new Dictionary<string, object?>()
        );
        List<Dictionary<string, object?>> frames = [];

        void Collect(LiveGameTransition transition)
        {
            if (transition.OverlayPayload is Dictionary<string, object?> frame)
                frames.Add(frame);
        }

        Collect(await game.OnStartAsync(state, default));

        roster.Add(alice);
        Collect(await game.OnInputAsync(state, Input(alice), default));
        roster.Add(bob);
        Collect(await game.OnInputAsync(state, Input(bob), default));

        state = State(random, roster, LiveGamePhase.Running, state.Data);
        for (int i = 0; i < ticksBefore; i++)
            Collect(await game.OnTickAsync(state, default));

        if (lateJoinAndCashOut)
        {
            roster.Add(carol);
            Collect(await game.OnInputAsync(state, Input(carol), default));
            Collect(await game.OnInputAsync(state, Input(alice), default));
        }

        for (int i = 0; i < ticksAfter; i++)
            Collect(await game.OnTickAsync(state, default));

        LiveGameResolution resolution = await game.OnResolveAsync(state, default);
        resolution.FinalOverlayPayload.Should().BeOfType<Dictionary<string, object?>>();
        frames.Add((Dictionary<string, object?>)resolution.FinalOverlayPayload!);
        return frames;
    }

    private static readonly string[] GameNames =
    [
        "crash-cashout-and-bust",
        "crash-cap",
        "drop",
        "heist",
        "raffle",
    ];

    public static TheoryData<string> Games => [.. GameNames];

    private static Task<List<Dictionary<string, object?>>> DriveByName(string name) =>
        name switch
        {
            // First draw misses the opening bust; the second survives nothing: bust on the first climb.
            "crash-cashout-and-bust" => DriveAsync(
                new CrashGame(),
                new Sequence(0.99, 0.99),
                1,
                1,
                true
            ),
            // Every draw survives, so the climb runs to the cap.
            "crash-cap" => DriveAsync(new CrashGame(), new Sequence(0.99, 0.0), 80, 0, false),
            "drop" => DriveAsync(new DropGame(), new Sequence(0.3, 0.5, 0.9), 0, 0, false),
            "heist" => DriveAsync(new HeistGame(), new Sequence(0.1, 0.9), 0, 0, false),
            "raffle" => DriveAsync(new RaffleGame(), new Sequence(0.2, 0.7), 0, 0, false),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };

    private static Type FrameFor(string kind) =>
        AllFrames.Single(t =>
            t.GetProperty("Kind")!.GetCustomAttribute<WireLiteralAttribute>()!.Values.Contains(kind)
        );

    private static string WireName(PropertyInfo property) =>
        JsonNamingPolicy.CamelCase.ConvertName(property.Name);

    private static bool IsNullable(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null
        || new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;

    // Returns every way the frame differs from the record: unknown keys, missing required keys, wrong value kinds.
    private static List<string> Differences(
        Dictionary<string, object?> frame,
        Type record,
        string path
    )
    {
        List<string> problems = [];
        Dictionary<string, PropertyInfo> properties = record
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(WireName);

        foreach ((string key, object? value) in frame)
        {
            if (!properties.TryGetValue(key, out PropertyInfo? property))
            {
                problems.Add($"{path}.{key}: the wire has it, {record.Name} does not");
                continue;
            }
            if (value is null)
            {
                if (!IsNullable(property))
                    problems.Add($"{path}.{key}: null on the wire, not nullable in {record.Name}");
                continue;
            }
            problems.AddRange(CheckValue(value, property, $"{path}.{key}"));
        }

        foreach ((string key, PropertyInfo property) in properties)
            if (!frame.ContainsKey(key) && !IsNullable(property))
                problems.Add($"{path}.{key}: {record.Name} requires it, the wire omits it");

        return problems;
    }

    private static IEnumerable<string> CheckValue(object value, PropertyInfo property, string path)
    {
        Type declared = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (declared == typeof(string))
        {
            if (value is not string)
                yield return $"{path}: expected a string, got {value.GetType().Name}";
        }
        else if (declared == typeof(bool))
        {
            if (value is not bool)
                yield return $"{path}: expected a bool, got {value.GetType().Name}";
        }
        else if (declared == typeof(int) || declared == typeof(long) || declared == typeof(double))
        {
            if (value is not (int or long or double))
                yield return $"{path}: expected a number, got {value.GetType().Name}";
        }
        else if (declared.IsGenericType && value is IEnumerable rows)
        {
            Type rowType = declared.GetGenericArguments()[0];
            int index = 0;
            foreach (object? row in rows)
            {
                if (row is not Dictionary<string, object?> rowFrame)
                    yield return $"{path}[{index}]: expected an object";
                else
                    foreach (string problem in Differences(rowFrame, rowType, $"{path}[{index}]"))
                        yield return problem;
                index++;
            }
        }
        else
        {
            yield return $"{path}: no check for {declared.Name}";
        }
    }

    [Theory]
    [MemberData(nameof(Games))]
    public async Task Every_frame_a_game_pushes_matches_the_record_for_its_kind(string name)
    {
        List<Dictionary<string, object?>> frames = await DriveByName(name);

        List<string> problems = [];
        foreach (Dictionary<string, object?> frame in frames)
        {
            string kind = (string)frame["kind"]!;
            problems.AddRange(Differences(frame, FrameFor(kind), kind));
        }

        frames.Should().NotBeEmpty();
        problems.Should().BeEmpty(string.Join("; ", problems));
    }

    [Fact]
    public async Task The_drive_reaches_every_kind_the_games_send_except_cancelled()
    {
        HashSet<string> seen = [];
        foreach (string name in GameNames)
        foreach (Dictionary<string, object?> frame in await DriveByName(name))
            seen.Add((string)frame["kind"]!);

        seen.Should()
            .BeEquivalentTo(
                ["round_open", "join", "progress", "cashout", "bust", "cap", "drop", "results"],
                "a kind this drive never reaches is a kind the wire test cannot hold to its record"
            );
    }

    [Fact]
    public void The_cancel_frame_record_names_the_keys_the_engine_sends()
    {
        // The engine's cancel frame is { kind, cancelled, reason } (LiveGameEngine.CancelInternalAsync); the engine
        // test asserts the live frame, this holds the record to the same three keys.
        Dictionary<string, object?> frame = new()
        {
            ["kind"] = "cancelled",
            ["cancelled"] = true,
            ["reason"] = "min_players_unmet",
        };

        Differences(frame, FrameFor("cancelled"), "cancelled").Should().BeEmpty();
    }
}
