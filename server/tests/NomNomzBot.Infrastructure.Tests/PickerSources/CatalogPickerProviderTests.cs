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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Commands.PipelineOptions;
using NomNomzBot.Infrastructure.Games.Pipeline;
using NomNomzBot.Infrastructure.Identity.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// The pickers fed by a catalogue the platform itself owns: live game types, pronouns, and the pipeline action
/// types. None of them is channel data, so they hold no tenant filter, but each still searches, pages and finds a
/// saved value.
/// </summary>
public sealed class CatalogPickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();

    // ── live game type ───────────────────────────────────────────────────────

    private static LiveGameManifest Manifest(string name, int min, int max) =>
        new(name, ["join"], "widget", min, max, TimeSpan.FromSeconds(30), null, false);

    [Fact]
    public async Task Live_game_type_lists_every_discovered_game_with_the_key_saved_and_the_player_range_shown()
    {
        ILiveGameCatalog catalog = Substitute.For<ILiveGameCatalog>();
        catalog.All.Returns(
            new Dictionary<string, LiveGameManifest>
            {
                ["drop"] = Manifest("Drop Game", 2, 50),
                ["boss"] = Manifest("Boss Fight", 1, 1),
            }
        );
        LiveGameTypeOptionProvider provider = new(catalog);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "drop")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "boss")
        );

        Assert.Equal(PipelineActionFieldKind.LiveGameType, provider.Kind);
        Assert.Equal(["boss", "drop"], PickerAsk.Values(all));
        Assert.Equal("Boss Fight", all.Value.Items[0].Label);
        Assert.Equal("1 player", all.Value.Items[0].SecondaryText);
        Assert.Equal("2-50 players", all.Value.Items[1].SecondaryText);
        Assert.Equal(["drop"], PickerAsk.Values(searched));
        Assert.Equal(["boss"], PickerAsk.Values(looked));
    }

    // ── pronoun ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pronoun_lists_the_catalogue_names_and_the_clear_choice()
    {
        IPronounProvider pronouns = Substitute.For<IPronounProvider>();
        pronouns
            .GetCatalogAsync(Arg.Any<CancellationToken>())
            .Returns(
                new Dictionary<string, PronounCatalogEntry>
                {
                    ["hehim"] = new("hehim", "he", "him", true, "he/him"),
                    ["theythem"] = new("theythem", "they", "them", false, "they/them"),
                }
            );
        PronounOptionProvider provider = new(pronouns);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "the")
        );
        Result<PipelineOptionListResult> cleared = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "clear")
        );

        Assert.Equal(PipelineActionFieldKind.Pronoun, provider.Kind);
        Assert.Equal(["clear", "he/him", "they/them"], PickerAsk.Values(result));
        Assert.Equal(["they/them"], PickerAsk.Values(searched));
        Assert.Equal(["clear"], PickerAsk.Values(cleared));
    }

    [Fact]
    public async Task Pronoun_is_unavailable_with_a_reason_when_the_catalogue_cannot_be_read()
    {
        IPronounProvider pronouns = Substitute.For<IPronounProvider>();
        pronouns
            .GetCatalogAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<string, PronounCatalogEntry>?)null);

        PickerAsk.AssertUnavailable(
            await new PronounOptionProvider(pronouns).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            "The pronoun catalogue could not be loaded."
        );
    }

    // ── action type ──────────────────────────────────────────────────────────

    private static ICommandAction ActionNamed(string type)
    {
        ICommandAction action = Substitute.For<ICommandAction>();
        action.ActionType.Returns(type);
        return action;
    }

    [Fact]
    public async Task Action_type_lists_every_registered_action_with_the_type_saved()
    {
        ActionTypeOptionProvider provider = new([
            ActionNamed("send_message"),
            ActionNamed("ban"),
            ActionNamed("wait"),
        ]);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "SEND")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "wait")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(PipelineActionFieldKind.ActionType, provider.Kind);
        Assert.Equal(["ban", "send_message", "wait"], PickerAsk.Values(all));
        Assert.Equal(["send_message"], PickerAsk.Values(searched));
        Assert.Equal(["wait"], PickerAsk.Values(looked));
        Assert.Equal(["wait"], PickerAsk.Values(second));
        Assert.Equal(3, second.Value.TotalCount);
    }
}
