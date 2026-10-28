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

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.ChannelOps;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.ChannelOps;

/// <summary>
/// Proves the Twitch half of the channel-ops seam preserves the pre-seam behavior it absorbed from
/// <c>StreamController</c>: a category NAME resolves through search — the exact-name match beats the
/// first fuzzy hit, an unresolvable name keeps the user's string and sends NO game id — and the change
/// that actually goes to Helix carries exactly the resolved id + title + tags; a Helix failure surfaces
/// with its code, never as fake success.
/// </summary>
public sealed class TwitchPlatformApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");

    private static (
        TwitchPlatformApi Api,
        ITwitchChannelsApi Channels,
        ITwitchSearchApi Search
    ) Build(params TwitchSearchCategory[] categories)
    {
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .ModifyChannelInformationAsync(
                Arg.Any<Guid>(),
                Arg.Any<ModifyChannelInformationRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchSearchApi search = Substitute.For<ITwitchSearchApi>();
        search
            .SearchCategoriesAsync(
                Arg.Any<string>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new TwitchPage<TwitchSearchCategory>(categories, null, 0)));

        return (new(channels, search), channels, search);
    }

    [Fact]
    public async Task An_exact_category_name_match_beats_the_first_fuzzy_hit()
    {
        (TwitchPlatformApi api, ITwitchChannelsApi channels, _) = Build(
            new TwitchSearchCategory("111", "Rust: Console Edition", ""),
            new TwitchSearchCategory("222", "Rust", "")
        );

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new(CategoryName: "rust")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CategoryName.Should().Be("Rust", "the catalogue spelling is canonical");
        await channels
            .Received(1)
            .ModifyChannelInformationAsync(
                Tenant,
                Arg.Is<ModifyChannelInformationRequest>(r => r.GameId == "222"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unresolvable_category_keeps_the_users_string_and_sends_no_game_id()
    {
        (TwitchPlatformApi api, ITwitchChannelsApi channels, _) = Build();

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new(Title: "new title", CategoryName: "no such game")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CategoryName.Should().Be("no such game");
        await channels
            .Received(1)
            .ModifyChannelInformationAsync(
                Tenant,
                Arg.Is<ModifyChannelInformationRequest>(r =>
                    r.GameId == null && r.Title == "new title"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_title_and_tags_only_update_never_touches_search()
    {
        (TwitchPlatformApi api, ITwitchChannelsApi channels, ITwitchSearchApi search) = Build();
        List<string> tags = ["chill", "nl"];

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new(Title: "t", Tags: tags)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new PlatformStreamInfoApplied("t", null, tags));
        await search
            .DidNotReceiveWithAnyArgs()
            .SearchCategoriesAsync(default!, default!, Arg.Any<CancellationToken>());
        await channels
            .Received(1)
            .ModifyChannelInformationAsync(
                Tenant,
                Arg.Is<ModifyChannelInformationRequest>(r =>
                    r.Title == "t" && r.Tags == tags && r.GameId == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Content_labels_go_to_helix_as_one_toggle_per_editable_label_and_the_category_label_is_never_sent()
    {
        (TwitchPlatformApi api, ITwitchChannelsApi channels, _) = Build();

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new(ContentLabels: ["Gambling", "MatureGame"], IsBrandedContent: true)
        );

        result.IsSuccess.Should().BeTrue();
        await channels
            .Received(1)
            .ModifyChannelInformationAsync(
                Tenant,
                Arg.Is<ModifyChannelInformationRequest>(r =>
                    r.IsBrandedContent == true
                    && r.Title == null
                    && r.ContentClassificationLabels != null
                    && r.ContentClassificationLabels.Count == 6
                    && r.ContentClassificationLabels.All(l => l.Id != "MatureGame")
                    && r.ContentClassificationLabels.Single(l => l.Id == "Gambling").IsEnabled
                    && r.ContentClassificationLabels.Count(l => l.IsEnabled) == 1
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_empty_label_list_turns_every_editable_label_off()
    {
        (TwitchPlatformApi api, ITwitchChannelsApi channels, _) = Build();

        await api.UpdateStreamInfoAsync(Tenant, new(ContentLabels: []));

        await channels
            .Received(1)
            .ModifyChannelInformationAsync(
                Tenant,
                Arg.Is<ModifyChannelInformationRequest>(r =>
                    r.ContentClassificationLabels != null
                    && r.ContentClassificationLabels.Count == 6
                    && r.ContentClassificationLabels.All(l => !l.IsEnabled)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_title_only_update_sends_no_content_labels()
    {
        (TwitchPlatformApi api, ITwitchChannelsApi channels, _) = Build();

        await api.UpdateStreamInfoAsync(Tenant, new(Title: "t"));

        await channels
            .Received(1)
            .ModifyChannelInformationAsync(
                Tenant,
                Arg.Is<ModifyChannelInformationRequest>(r =>
                    r.ContentClassificationLabels == null && r.IsBrandedContent == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_helix_failure_surfaces_with_its_code()
    {
        (TwitchPlatformApi api, ITwitchChannelsApi channels, _) = Build();
        channels
            .ModifyChannelInformationAsync(
                Arg.Any<Guid>(),
                Arg.Any<ModifyChannelInformationRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("missing scope", TwitchErrorCodes.MissingScope));

        Result<PlatformStreamInfoApplied> result = await api.UpdateStreamInfoAsync(
            Tenant,
            new(Title: "t")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
    }
}
