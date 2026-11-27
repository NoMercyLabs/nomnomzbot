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

using System.Globalization;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Games;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Games.Catalog;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Proves the reference DropGame's pure logic (live-games.md §4.1): a round opens on a random target; each
/// player drops exactly once (repeats are ignored, never a second roll); resolution pays
/// <c>PayoutMultiplier × stake</c> to landings inside the win radius and nothing outside it, honoring a
/// <c>win_radius</c> override from <c>ConfigJson</c>. Also proves the default-games seed now ships a
/// <c>drop_game</c> config (disabled, opt-in like every seed).
/// </summary>
public sealed class DropGameTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly Guid PlayerA = Guid.Parse("0192a000-0000-7000-8000-0000000000e2");
    private static readonly Guid PlayerB = Guid.Parse("0192a000-0000-7000-8000-0000000000e3");

    /// <summary>
    /// Deterministic randomness: the target draw is the unit-interval value, and each drop takes the next
    /// scripted 64-bit seed (the last one repeats). <see cref="SeedsDrawn"/> counts the drops that drew one.
    /// </summary>
    private sealed class SeedRandom(double targetDraw, params ulong[] seeds) : IGameRandom
    {
        private readonly Queue<ulong> _seeds = new(seeds);
        private ulong _last = seeds[^1];

        public int SeedsDrawn { get; private set; }

        public double NextDouble() => targetDraw;

        public int Next(int maxExclusive) => (int)(targetDraw * maxExclusive);

        public bool Roll(double percent) => targetDraw * 100.0 < percent;

        public ulong NextUInt64()
        {
            SeedsDrawn++;
            if (_seeds.Count > 0)
                _last = _seeds.Dequeue();
            return _last;
        }
    }

    // The default spread (half the track) and no luck: what DropGame uses when the config says nothing.
    private const double DefaultSpread = 50.0;

    /// <summary>
    /// Finds the first seed whose real flight lands where the test needs it, so a test states the landing it
    /// wants and the real generator proves the seed gives it.
    /// </summary>
    private static ulong SeedLandingAt(double target, double landing)
    {
        for (ulong seed = 1; seed < 2_000_000; seed++)
            if (DropFlight.Compute(seed, target, DefaultSpread, 0.0).LandingX == landing)
                return seed;
        throw new InvalidOperationException($"no seed lands at {landing} for target {target}");
    }

    private static LiveGameState State(
        IGameRandom random,
        List<LiveGameParticipant> participants,
        Dictionary<string, object?> data,
        decimal? multiplier = 2.5m,
        IReadOnlyDictionary<string, object?>? config = null
    ) =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Config = new(10, 100, multiplier, config),
            Participants = participants,
            Phase = LiveGamePhase.Lobby,
            Data = data,
            Random = random,
        };

    private static LiveGameParticipant Player(Guid id, string name, long stake) =>
        new(id, Guid.CreateVersion7(), name, stake);

    private static LiveGameInput Input(LiveGameParticipant player) =>
        new(player, "!drop", [], "!drop");

    [Fact]
    public async Task A_round_opens_on_a_random_target_and_each_player_drops_exactly_once()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        // 0.42 → target 42. Alice's seed is 777; a repeat would draw seed 888 — it must not.
        SeedRandom random = new(0.42, 777UL, 888UL);

        LiveGameTransition opened = await game.OnStartAsync(
            State(random, [alice], data),
            CancellationToken.None
        );
        opened.PushOverlay.Should().BeTrue();
        data["target"].Should().Be(42.0);

        LiveGameTransition drop = await game.OnInputAsync(
            State(random, [alice], data),
            Input(alice),
            CancellationToken.None
        );
        drop.PushOverlay.Should().BeTrue();
        double landing = DropFlight.Compute(777UL, 42.0, DefaultSpread, 0.0).LandingX;
        data[$"drop:{PlayerA}"].Should().Be(landing, "the landing comes from the seed");

        LiveGameTransition repeat = await game.OnInputAsync(
            State(random, [alice], data),
            Input(alice),
            CancellationToken.None
        );
        repeat.Should().Be(LiveGameTransition.Ignore(), "one drop per player");
        data[$"drop:{PlayerA}"].Should().Be(landing, "a repeat never re-rolls the landing");
        random.SeedsDrawn.Should().Be(1, "the repeat drew no second seed");
    }

    [Fact]
    public async Task Resolution_pays_multiplier_times_stake_inside_the_radius_and_nothing_outside()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        LiveGameParticipant bob = Player(PlayerB, "Bob", 20);
        // target 50; Alice lands 55 (distance 5 ≤ 10 → win); Bob lands 90 (distance 40 → lose).
        SeedRandom random = new(0.50, SeedLandingAt(50.0, 55.0), SeedLandingAt(50.0, 90.0));
        List<LiveGameParticipant> players = [alice, bob];

        await game.OnStartAsync(State(random, players, data), CancellationToken.None);
        await game.OnInputAsync(State(random, players, data), Input(alice), CancellationToken.None);
        await game.OnInputAsync(State(random, players, data), Input(bob), CancellationToken.None);

        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, players, data),
            CancellationToken.None
        );

        LiveGameAward aliceAward = resolution.Awards.Single(a => a.UserId == PlayerA);
        aliceAward.Outcome.Should().Be(GameOutcome.Win);
        aliceAward.Payout.Should().Be(100, "40 × the 2.5 multiplier");
        LiveGameAward bobAward = resolution.Awards.Single(a => a.UserId == PlayerB);
        bobAward.Outcome.Should().Be(GameOutcome.Lose);
        bobAward.Payout.Should().Be(0);
        resolution
            .FinalOverlayPayload.Should()
            .NotBeNull("the final scoreboard frame is the payload");
    }

    // Opens a round on target 50 (radius 10) and drops Alice with the given seed; returns the drop frame.
    private static async Task<Dictionary<string, object?>> DropWithSeed(
        Dictionary<string, object?> data,
        ulong seed
    )
    {
        DropGame game = new();
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        SeedRandom random = new(0.50, seed);
        await game.OnStartAsync(State(random, [alice], data), CancellationToken.None);
        LiveGameTransition drop = await game.OnInputAsync(
            State(random, [alice], data),
            Input(alice),
            CancellationToken.None
        );
        return (Dictionary<string, object?>)drop.OverlayPayload!;
    }

    private static Task<Dictionary<string, object?>> DropLandingAt(double landing) =>
        DropWithSeed([], SeedLandingAt(50.0, landing));

    [Fact]
    public async Task A_drop_on_the_target_center_scores_100()
    {
        Dictionary<string, object?> frame = await DropLandingAt(50.0);

        frame["score"].Should().Be(100);
        frame["hit"].Should().Be(true);
    }

    [Fact]
    public async Task A_drop_on_the_win_radius_edge_scores_0_but_still_hits()
    {
        Dictionary<string, object?> frame = await DropLandingAt(60.0);

        frame["distance"].Should().Be(10.0);
        frame["score"].Should().Be(0);
        frame["hit"].Should().Be(true, "the hit rule stays distance <= radius");
    }

    private const string Avatar =
        "https://static-cdn.jtvnw.net/jtv_user_pictures/alice-profile_image-300x300.png";
    private const string KappaUrl = "https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/3.0";

    private static readonly LiveGameInputMedia AliceMedia = new(
        Avatar,
        [new LiveGameInputEmote("Kappa", KappaUrl)]
    );

    // Drops Alice with the chat message as typed (keyword first) and the pictures it carried.
    private static async Task<Dictionary<string, object?>> DropSaying(
        string message,
        LiveGameInputMedia? media
    )
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        SeedRandom random = new(0.50, 777UL);
        await game.OnStartAsync(State(random, [alice], data), CancellationToken.None);
        string[] tokens = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        LiveGameTransition drop = await game.OnInputAsync(
            State(random, [alice], data),
            new(alice, tokens[0], [.. tokens.Skip(1)], message, media),
            CancellationToken.None
        );
        return (Dictionary<string, object?>)drop.OverlayPayload!;
    }

    private static Dictionary<string, object?> ImageOf(Dictionary<string, object?> frame) =>
        frame["image"].Should().BeOfType<Dictionary<string, object?>>().Subject;

    [Theory]
    [InlineData("!drop me")]
    [InlineData("!drop ME")]
    [InlineData("!drop 50 me")]
    public async Task Drop_me_drops_the_viewers_own_avatar_as_a_round_picture(string message)
    {
        Dictionary<string, object?> image = ImageOf(await DropSaying(message, AliceMedia));

        image["src"].Should().Be(Avatar);
        image["round"].Should().Be(true, "an avatar is drawn as a circle");
    }

    [Fact]
    public async Task Drop_with_an_emote_drops_that_emotes_platform_image()
    {
        Dictionary<string, object?> image = ImageOf(await DropSaying("!drop Kappa", AliceMedia));

        image["src"].Should().Be(KappaUrl);
        image["round"].Should().Be(false);
    }

    [Theory]
    [InlineData("!drop \U0001F331", "1f331")]
    [InlineData("!drop ❤️", "2764")]
    [InlineData("!drop \U0001F469‍\U0001F4BB", "1f469-200d-1f4bb")]
    [InlineData("!drop \U0001F44D\U0001F3FD", "1f44d-1f3fd")]
    [InlineData("!drop \U0001F1F3\U0001F1F1", "1f1f3-1f1f1")]
    public async Task Drop_with_an_emoji_drops_its_twemoji_picture(string message, string file)
    {
        Dictionary<string, object?> image = ImageOf(await DropSaying(message, AliceMedia));

        image["src"]
            .Should()
            .Be($"https://cdn.jsdelivr.net/gh/jdecked/twemoji@16.0.1/assets/svg/{file}.svg");
        image["round"].Should().Be(false);
    }

    [Theory]
    [InlineData("!drop")]
    [InlineData("!drop 50")]
    [InlineData("!drop hello")]
    [InlineData("!drop https://evil.example/x.png")]
    [InlineData("!drop abc\U0001F331")]
    public async Task A_drop_without_a_known_picture_falls_back_to_the_packs_own_art(string message)
    {
        Dictionary<string, object?> frame = await DropSaying(message, AliceMedia);

        frame.Should().ContainKey("image");
        frame["image"]
            .Should()
            .BeNull("only the viewer's avatar, a message emote or an emoji become a picture");
    }

    [Fact]
    public async Task Drop_me_without_a_known_avatar_falls_back_to_the_packs_own_art()
    {
        Dictionary<string, object?> frame = await DropSaying("!drop me", new(null, []));
        frame["image"].Should().BeNull();

        Dictionary<string, object?> noMedia = await DropSaying("!drop Kappa", null);
        noMedia["image"].Should().BeNull("an input without media carries no emote");
    }

    [Fact]
    public async Task A_drop_halfway_to_the_edge_scores_50_and_beyond_the_edge_scores_0()
    {
        Dictionary<string, object?> halfway = await DropLandingAt(45.0);
        Dictionary<string, object?> beyond = await DropLandingAt(90.0);

        halfway["score"].Should().Be(50);
        beyond["score"].Should().Be(0);
        beyond["hit"].Should().Be(false);
    }

    [Fact]
    public async Task The_drop_frame_carries_the_seed_the_spawn_and_the_server_landing()
    {
        Dictionary<string, object?> data = [];
        Dictionary<string, object?> frame = await DropWithSeed(data, 0UL);

        frame["kind"].Should().Be("drop");
        frame["dropId"].Should().Be(1, "the first drop of the round");
        frame["seed"].Should().Be("0", "the seed travels as a decimal string");
        frame["spawnX"].Should().Be(20.0, "the pinned vector for seed 0");
        frame["landingX"].Should().Be(78.6, "the pinned vector for seed 0 on target 50");
        frame["landed"].Should().Be(frame["landingX"], "the old field keeps its meaning");
        frame["player"].Should().Be("Alice");
        frame["distance"].Should().Be(28.6);
        frame["hit"].Should().Be(false);
        frame["score"].Should().Be(0);
    }

    [Fact]
    public async Task The_seed_is_persisted_with_the_drop_and_replays_to_the_same_landing()
    {
        Dictionary<string, object?> data = [];
        ulong seed = 0xDEADBEEFCAFEF00DUL;
        Dictionary<string, object?> frame = await DropWithSeed(data, seed);

        // The round state is what the engine writes to GameSession.StateJson after every drop.
        data[$"dropSeed:{PlayerA}"].Should().Be(seed.ToString(CultureInfo.InvariantCulture));
        ulong persistedSeed = ulong.Parse(
            (string)data[$"dropSeed:{PlayerA}"]!,
            CultureInfo.InvariantCulture
        );
        double persistedTarget = (double)data["target"]!;

        DropFlightPath replay = DropFlight.Compute(
            persistedSeed,
            persistedTarget,
            DefaultSpread,
            0.0
        );

        replay.LandingX.Should().Be((double)frame["landingX"]!);
        replay.SpawnX.Should().Be((double)frame["spawnX"]!);
        replay.LandingX.Should().Be((double)data[$"drop:{PlayerA}"]!);
    }

    [Fact]
    public async Task The_spread_setting_narrows_where_a_drop_can_land()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        // Seed 0 gives offset +0.5728: spread 10 lands 5.7 from the target, not 28.6.
        SeedRandom random = new(0.50, 0UL);
        Dictionary<string, object?> config = new() { ["spread"] = 10 };

        await game.OnStartAsync(
            State(random, [alice], data, config: config),
            CancellationToken.None
        );
        LiveGameTransition drop = await game.OnInputAsync(
            State(random, [alice], data, config: config),
            Input(alice),
            CancellationToken.None
        );

        Dictionary<string, object?> frame = (Dictionary<string, object?>)drop.OverlayPayload!;
        frame["landingX"].Should().Be(55.7);
    }

    [Fact]
    public async Task The_results_row_carries_the_score_and_null_for_a_player_who_never_dropped()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        LiveGameParticipant bob = Player(PlayerB, "Bob", 20);
        List<LiveGameParticipant> players = [alice, bob];
        // target 50; Alice lands 52 (distance 2 → score 80); Bob never drops.
        SeedRandom random = new(0.50, SeedLandingAt(50.0, 52.0), 12345UL);

        await game.OnStartAsync(State(random, players, data), CancellationToken.None);
        await game.OnInputAsync(State(random, players, data), Input(alice), CancellationToken.None);
        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, players, data),
            CancellationToken.None
        );

        Dictionary<string, object?> payload =
            (Dictionary<string, object?>)resolution.FinalOverlayPayload!;
        List<Dictionary<string, object?>> rows =
            (List<Dictionary<string, object?>>)payload["results"]!;
        rows.Single(r => (string)r["player"]! == "Alice")["score"].Should().Be(80);
        rows.Single(r => (string)r["player"]! == "Bob")["score"].Should().BeNull();
    }

    [Fact]
    public async Task The_win_radius_honors_a_ConfigJson_override()
    {
        DropGame game = new();
        Dictionary<string, object?> data = [];
        LiveGameParticipant alice = Player(PlayerA, "Alice", 40);
        // target 50, landing 56: distance 6 — a win at the default radius (10), a LOSS at radius 5.
        SeedRandom random = new(0.50, SeedLandingAt(50.0, 56.0));
        Dictionary<string, object?> config = new() { ["win_radius"] = 5 };

        await game.OnStartAsync(
            State(random, [alice], data, config: config),
            CancellationToken.None
        );
        await game.OnInputAsync(
            State(random, [alice], data, config: config),
            Input(alice),
            CancellationToken.None
        );
        LiveGameResolution resolution = await game.OnResolveAsync(
            State(random, [alice], data, config: config),
            CancellationToken.None
        );

        resolution.Awards.Single().Outcome.Should().Be(GameOutcome.Lose);
        resolution.Awards.Single().Payout.Should().Be(0);
    }

    [Fact]
    public async Task The_default_games_seed_ships_a_disabled_drop_game_config()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(new(2026, 7, 17, 15, 0, 0, TimeSpan.Zero));
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            bus,
            clock
        );
        GameService games = new(
            db,
            accounts,
            Substitute.For<IAgeConsentService>(),
            Substitute.For<IGameRandomizer>(),
            bus,
            clock
        );

        Result<IReadOnlyList<GameConfigDto>> listed = await games.ListGamesAsync(Channel);

        listed.IsSuccess.Should().BeTrue(listed.ErrorMessage);
        GameConfigDto drop = listed
            .Value.Should()
            .ContainSingle(g => g.GameType == "drop_game")
            .Subject;
        drop.Category.Should().Be(nameof(GameCategory.Minigame));
        drop.IsEnabled.Should().BeFalse("every seeded game is opt-in");
        drop.PayoutMultiplier.Should().Be(2m);
        (await Task.FromResult(db.GameConfigs.Count(g => g.BroadcasterId == Channel)))
            .Should()
            .Be(6, "the four instant games plus the two live games (drop_game, raffle)");
    }
}
