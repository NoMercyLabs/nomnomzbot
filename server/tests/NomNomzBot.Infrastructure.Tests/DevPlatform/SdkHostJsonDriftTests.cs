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
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The drift guard for what the editor's SDK result types SAY against what the host REALLY returns. A real
/// <see cref="ScriptHostBridge"/> produces the JSON a script's <c>JSON.parse</c> sees; each payload is held to its
/// parsed d.ts interface — every key declared, every required key present, null only where the type allows it,
/// value kinds equal, nested objects checked against their own interface, and every declared <c>| null</c>
/// observed null by at least one sample (a null the host can never send is a lie that makes scripts write dead
/// branches).
/// </summary>
public sealed partial class SdkHostJsonDriftTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d001");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-00000000d0a1");

    private sealed record Sample(string Label, string Interface, string? Json);

    private static ScriptHostBridge Build(
        IMusicService? music = null,
        ITtsDispatchService? tts = null,
        IRewardService? rewards = null,
        IViewerAnalyticsService? analytics = null,
        ITtsConfigService? ttsConfig = null,
        IApplicationDbContext? db = null,
        ISevenTvUserPaintResolver? paintResolver = null,
        IOwnerActionService? ownerActions = null
    ) =>
        new(
            Channel,
            Viewer.ToString(),
            null,
            Substitute.For<IChatProvider>(),
            Substitute.For<ICurrencyAccountService>(),
            music ?? Substitute.For<IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            Substitute.For<IScriptStorageService>(),
            tts ?? Substitute.For<ITtsDispatchService>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<IWidgetEventNotifier>(),
            rewards ?? Substitute.For<IRewardService>(),
            analytics ?? Substitute.For<IViewerAnalyticsService>(),
            ttsConfig ?? Substitute.For<ITtsConfigService>(),
            Substitute.For<IScheduledPipelineService>(),
            db ?? AuthTestBuilder.NewContext(),
            paintResolver ?? Substitute.For<ISevenTvUserPaintResolver>(),
            ownerActions ?? Substitute.For<IOwnerActionService>()
        );

    private static string? Call(ScriptHostBridge bridge, string key, params string[] args) =>
        bridge.Resolve(key)(key, args, CancellationToken.None);

    private static async Task<AuthDbContext> SeedViewerAsync(
        string login,
        string twitchId,
        string? avatar
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = Guid.NewGuid(),
                Username = login,
                UsernameNormalized = login.ToUpperInvariant(),
                DisplayName = login,
                TwitchUserId = twitchId,
                ProfileImageUrl = avatar,
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static NowPlaying Track(string? album, string? requestedBy) =>
        new(
            TrackName: "Money for Nothing",
            Artist: "Dire Straits",
            Album: album,
            ImageUrl: null,
            DurationMs: 502000,
            ProgressMs: 61000,
            IsPlaying: true,
            Volume: 70,
            RequestedBy: requestedBy,
            Provider: "spotify"
        );

    private static string? NowPlayingJson(NowPlaying track)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music.GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>()).Returns(track);
        return Call(Build(music: music), "music.nowPlaying");
    }

    private static string? RewardJson(string? prompt)
    {
        RewardDetail reward = new(
            Id: Guid.NewGuid().ToString(),
            Title: "Lucky Feather",
            Prompt: prompt,
            Response: null,
            Cost: 500,
            IsEnabled: true,
            IsManageable: true,
            IsUserInputRequired: false,
            IsPaused: false,
            IsMigrationPending: false,
            BackgroundColor: null,
            ImageUrl: null,
            MaxPerStream: null,
            MaxPerUserPerStream: null,
            GlobalCooldownSeconds: null,
            TimerDurationSeconds: null,
            PipelineId: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .ListAsync(
                Channel.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<RewardDetail>([reward], 1, 100, 1)));
        return Call(Build(rewards: rewards), "reward.get", "lucky feather");
    }

    private static async Task<string?> UserJsonAsync(string login, string? avatar, ChatPaint? paint)
    {
        AuthDbContext db = await SeedViewerAsync(login, "555" + login.Length, avatar);
        ISevenTvUserPaintResolver resolver = Substitute.For<ISevenTvUserPaintResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(paint);
        return Call(Build(db: db, paintResolver: resolver), "user.get", "@" + login);
    }

    private static string? ActionJson(WidgetActionOutcome outcome)
    {
        IOwnerActionService owner = Substitute.For<IOwnerActionService>();
        owner
            .RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(outcome));
        return Call(Build(ownerActions: owner), "actions.invoke:tts_synthesize");
    }

    private static string? LastErrorJson()
    {
        ScriptHostBridge bridge = Build();
        Call(bridge, "chat.send", " ");
        return Call(bridge, "last.error");
    }

    private static async Task<string?> StatsJsonAsync(bool knownViewer)
    {
        if (!knownViewer)
            return Call(Build(), "stats.viewer", "nobody");

        Guid viewerId = Guid.NewGuid();
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = viewerId,
                Username = "bamo",
                UsernameNormalized = "BAMO",
                DisplayName = "bamo",
                TwitchUserId = "555001",
            }
        );
        await db.SaveChangesAsync();
        IViewerAnalyticsService analytics = Substitute.For<IViewerAnalyticsService>();
        analytics
            .GetProfileAsync(Channel, viewerId, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ViewerProfileDto(
                        ViewerUserId: viewerId,
                        ViewerTwitchUserId: "555001",
                        DisplayName: "bamo",
                        FirstSeenAt: new DateTime(2026, 1, 5),
                        LastSeenAt: new DateTime(2026, 7, 1),
                        TotalWatchSeconds: 7200,
                        TotalMessages: 420,
                        TotalCommandsUsed: 12,
                        TotalRedemptions: 3,
                        TotalSongRequests: 9,
                        IsFollower: true,
                        IsSubscriber: false,
                        SubTier: null,
                        IsAnalyticsOptedOut: false
                    )
                )
            );
        return Call(Build(analytics: analytics, db: db), "stats.viewer", "bamo");
    }

    private static async Task<string?> TtsVoiceJsonAsync()
    {
        AuthDbContext db = await SeedViewerAsync("bamo", "555003", null);
        ITtsConfigService ttsConfig = Substitute.For<ITtsConfigService>();
        ttsConfig
            .GetUserVoiceAsync(Channel, "555003", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserTtsVoiceDto("555003", "en-GB-Sonia")));
        ttsConfig
            .SearchVoicesAsync(Arg.Any<TtsVoiceQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<TtsVoiceDto>(
                        [
                            new TtsVoiceDto(
                                Id: "en-GB-Sonia",
                                Name: "en-GB-Sonia",
                                DisplayName: "Sonia (British)",
                                Locale: "en-GB",
                                Gender: "Female",
                                Provider: "azure",
                                IsDefault: false,
                                Accent: "british",
                                Age: null,
                                Styles: [],
                                Tags: [],
                                Description: null,
                                PreviewUrl: null
                            ),
                        ],
                        1,
                        10,
                        1
                    )
                )
            );
        return Call(Build(ttsConfig: ttsConfig, db: db), "tts.voice.get", "@bamo");
    }

    private static string? SpeakJson()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        VoiceId: "en-US-Aria",
                        Provider: "azure",
                        CharacterCount: 12,
                        DurationMs: 900,
                        PlaybackUrl: null
                    )
                )
            );
        return Call(Build(tts: tts), "tts.speak", "hello stream", "en-US-Aria");
    }

    private static async Task<List<Sample>> SamplesAsync()
    {
        ChatPaint fullPaint = new()
        {
            Id = "01JEY00EDNVW20AWX2NPG4HTNF",
            Name = "Image Paint",
            BackgroundImage = "url(https://cdn.7tv.app/paint/a.png)",
            Color = "#ff66aa",
            TextShadow = "0 0 4px #ff66aa",
            IsImageOnly = true,
        };
        ChatPaint colorOnlyPaint = new()
        {
            Id = "01JEY00EDNVW20AWX2NPG4HTNG",
            Name = "Color Paint",
            Color = "#00ff00",
            IsImageOnly = false,
        };
        return
        [
            new(
                "user with avatar and full paint",
                "NnzApiUser",
                await UserJsonAsync("sora", "https://cdn/a.png", fullPaint)
            ),
            new(
                "user with a color-only paint",
                "NnzApiUser",
                await UserJsonAsync("rikuu", "https://cdn/b.png", colorOnlyPaint)
            ),
            new(
                "user without avatar or paint",
                "NnzApiUser",
                await UserJsonAsync("plain", null, null)
            ),
            new("tts.speak", "NnzApiTtsResult", SpeakJson()),
            new(
                "music with album and requester",
                "NnzApiTrack",
                NowPlayingJson(Track("Brothers in Arms", "viewer42"))
            ),
            new(
                "music without album or requester",
                "NnzApiTrack",
                NowPlayingJson(Track(null, null))
            ),
            new("stats of a known viewer", "NnzApiViewerStats", await StatsJsonAsync(true)),
            new("stats of an unknown viewer", "NnzApiViewerStats", await StatsJsonAsync(false)),
            new("reward with prompt", "NnzApiReward", RewardJson("Steal the feather")),
            new("reward without prompt", "NnzApiReward", RewardJson(null)),
            new("tts voice", "NnzApiTtsVoice", await TtsVoiceJsonAsync()),
            new("last error", "NnzApiError", LastErrorJson()),
            new(
                "action success",
                "NnzApiActionResult",
                ActionJson(
                    new(true, "spoken", null, new Dictionary<string, string> { ["a"] = "b" })
                )
            ),
            new(
                "action failure",
                "NnzApiActionResult",
                ActionJson(new(false, null, "boom", new Dictionary<string, string>()))
            ),
        ];
    }

    [Fact]
    public async Task Every_host_result_has_exactly_the_shape_its_sdk_type_declares()
    {
        Dictionary<string, List<string>> blocks = SdkScriptSurfaceDriftTests.TypeBlocks(
            SdkScriptSurfaceDriftTests.ScriptDts()
        );
        List<string> mismatches = [];
        HashSet<string> observedNull = new(StringComparer.Ordinal);

        foreach (Sample sample in await SamplesAsync())
        {
            if (sample.Json is null)
            {
                mismatches.Add($"{sample.Label}: the host returned null, no sample was produced");
                continue;
            }
            CheckObject(
                JObject.Parse(sample.Json),
                sample.Interface,
                sample.Label,
                blocks,
                observedNull,
                mismatches
            );
        }

        foreach (string nullable in NullableMembers(blocks))
            if (!observedNull.Contains(nullable))
                mismatches.Add(
                    $"{nullable}: declared '| null' but no sample ever returned null for it"
                );

        mismatches
            .Should()
            .BeEmpty(
                "the editor's types must say exactly what the host returns:\n"
                    + string.Join("\n", mismatches)
            );
    }

    private static List<string> Interfaces() =>
        [
            "NnzApiUser",
            "NnzApiPaint",
            "NnzApiTrack",
            "NnzApiTtsResult",
            "NnzApiReward",
            "NnzApiViewerStats",
            "NnzApiTtsVoice",
            "NnzApiError",
            "NnzApiActionResult",
        ];

    // Every "Interface.member" whose declared type includes null.
    private static IEnumerable<string> NullableMembers(Dictionary<string, List<string>> blocks) =>
        Interfaces()
            .Where(blocks.ContainsKey)
            .SelectMany(name =>
                Members(blocks[name])
                    .Where(m => m.Types.Contains("null"))
                    .Select(m => $"{name}.{m.Name}")
            );

    private sealed record Member(string Name, bool Optional, List<string> Types);

    private static IEnumerable<Member> Members(List<string> lines)
    {
        foreach (string line in lines)
        {
            Match match = MemberLine().Match(line);
            if (!match.Success)
                continue;
            yield return new(
                match.Groups[1].Value,
                match.Groups[2].Success,
                [.. SplitUnion(match.Groups[3].Value)]
            );
        }
    }

    // Splits a union at top level only, so Record<string, string | number> stays whole.
    private static IEnumerable<string> SplitUnion(string type)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < type.Length; i++)
        {
            if (type[i] is '<' or '(' or '{' or '[')
                depth++;
            else if (type[i] is '>' or ')' or '}' or ']')
                depth--;
            else if (type[i] == '|' && depth == 0)
            {
                yield return type[start..i].Trim();
                start = i + 1;
            }
        }
        yield return type[start..].Trim();
    }

    private static void CheckObject(
        JObject json,
        string interfaceName,
        string path,
        Dictionary<string, List<string>> blocks,
        HashSet<string> observedNull,
        List<string> mismatches
    )
    {
        if (!blocks.TryGetValue(interfaceName, out List<string>? lines))
        {
            mismatches.Add($"{path}: the d.ts declares no interface {interfaceName}");
            return;
        }

        Dictionary<string, Member> declared = Members(lines).ToDictionary(m => m.Name);
        foreach (JProperty property in json.Properties())
        {
            string where = $"{path}.{property.Name}";
            if (!declared.TryGetValue(property.Name, out Member? member))
            {
                mismatches.Add(
                    $"{where}: the host sends it but {interfaceName} does not declare it"
                );
                continue;
            }
            if (property.Value.Type == JTokenType.Null)
                observedNull.Add($"{interfaceName}.{property.Name}");
            CheckValue(property.Value, member.Types, where, blocks, observedNull, mismatches);
        }

        foreach (Member member in declared.Values.Where(m => !m.Optional))
            if (!json.ContainsKey(member.Name))
                mismatches.Add(
                    $"{path}.{member.Name}: required by {interfaceName} but the host omits it"
                );
    }

    private static void CheckValue(
        JToken value,
        List<string> types,
        string path,
        Dictionary<string, List<string>> blocks,
        HashSet<string> observedNull,
        List<string> mismatches
    )
    {
        string kind = value.Type switch
        {
            JTokenType.Null => "null",
            JTokenType.String => "string",
            JTokenType.Integer or JTokenType.Float => "number",
            JTokenType.Boolean => "boolean",
            JTokenType.Object => "object",
            _ => value.Type.ToString(),
        };

        bool accepted = kind switch
        {
            "null" => types.Contains("null"),
            "string" => types.Contains("string") || types.Contains($"'{value.Value<string>()}'"),
            "number" or "boolean" => types.Contains(kind),
            _ => false,
        };

        if (kind == "object")
        {
            string? interfaceName = types.FirstOrDefault(blocks.ContainsKey);
            string? record = types.FirstOrDefault(t =>
                t.StartsWith("Record<", StringComparison.Ordinal)
            );
            if (interfaceName is not null)
            {
                CheckObject((JObject)value, interfaceName, path, blocks, observedNull, mismatches);
                return;
            }
            if (record is not null)
            {
                List<string> valueTypes = [.. SplitUnion(record[(record.IndexOf(',') + 1)..^1])];
                foreach (JProperty entry in ((JObject)value).Properties())
                    CheckValue(
                        entry.Value,
                        valueTypes,
                        $"{path}.{entry.Name}",
                        blocks,
                        observedNull,
                        mismatches
                    );
                return;
            }
        }

        if (!accepted)
            mismatches.Add(
                $"{path}: host sends {kind} ({value.ToString(Newtonsoft.Json.Formatting.None)}) but the type is '{string.Join(" | ", types)}'"
            );
    }

    [GeneratedRegex(@"^  ([A-Za-z_$][A-Za-z0-9_$]*)(\?)?:\s*(.+);$")]
    private static partial Regex MemberLine();
}
