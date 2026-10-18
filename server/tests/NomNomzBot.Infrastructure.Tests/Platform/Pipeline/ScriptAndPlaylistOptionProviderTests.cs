// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.CustomCode.Pipeline;
using NomNomzBot.Infrastructure.CustomCode.PipelineActions;
using NomNomzBot.Infrastructure.Music.Pipeline;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;
using MusicPlaylistDto = NomNomzBot.Application.Contracts.Music.MusicPlaylistDto;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// Field-content tests for the Run script and playlist step pickers: what a script row and a provider playlist
/// map to, how the saved id is found again, and that the step descriptors really expose the picker kinds.
/// </summary>
public sealed class ScriptAndPlaylistOptionProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private static readonly PaginationParams DefaultPage = new(Page: 1, PageSize: 25);

    private static PipelineOptionQuery Search(string? term) => new(term, null, DefaultPage);

    // ── script ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Script_option_maps_id_name_and_description_and_is_tenant_scoped()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        CodeScript script = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "welcome-viewer",
            Description = "Greets a new viewer",
            IsEnabled = true,
        };
        db.CodeScripts.Add(script);
        db.CodeScripts.Add(
            new CodeScript
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Guid.NewGuid(),
                Name = "someone-elses",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        ScriptOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            Search(null)
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.SourceAvailable);
        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal(script.Id.ToString(), option.Value);
        Assert.Equal("welcome-viewer", option.Label);
        Assert.Equal("Greets a new viewer", option.SecondaryText);
        Assert.Equal(PipelineOptionState.Selectable, option.State);
    }

    [Fact]
    public async Task Script_search_filters_by_name_ignoring_case()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.CodeScripts.AddRange(
            new CodeScript
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "Welcome-Viewer",
                IsEnabled = true,
            },
            new CodeScript
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "raid-thanks",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        ScriptOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            Search("welcome")
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("Welcome-Viewer", option.Label);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task Script_value_lookup_finds_the_script_by_its_ulid_wire_form()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        CodeScript script = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Broadcaster,
            Name = "saved-script",
            IsEnabled = true,
        };
        db.CodeScripts.Add(script);
        await db.SaveChangesAsync();
        string ulid = OwnedIdCodec.Encode(script.Id);

        ScriptOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: ulid, DefaultPage)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal(script.Id.ToString(), option.Value);
        Assert.Equal("saved-script", option.Label);
    }

    [Fact]
    public async Task Script_value_lookup_of_an_unknown_value_returns_nothing()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.CodeScripts.Add(
            new CodeScript
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "other",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        ScriptOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: "not-an-id", DefaultPage)
        );

        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task Disabled_script_is_unavailable_with_a_reason()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.CodeScripts.Add(
            new CodeScript
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Broadcaster,
                Name = "switched-off",
                IsEnabled = false,
            }
        );
        await db.SaveChangesAsync();

        ScriptOptionProvider provider = new(db);
        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            Search(null)
        );

        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal(PipelineOptionState.Unavailable, option.State);
        Assert.Equal("Script is disabled.", option.Reason);
    }

    // ── playlist ──────────────────────────────────────────────────────────────

    private static MusicPlaylistDto Playlist(string id, string name, int tracks) =>
        new(id, name, null, true, tracks, "https://img.example/" + id, "spotify");

    private static (
        MusicPlaylistOptionProvider Provider,
        IMusicProviderManageApi Manage
    ) NewPlaylistProvider(string? activeProvider)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetActiveProviderKeyAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(activeProvider);
        IMusicProviderManageApi manage = Substitute.For<IMusicProviderManageApi>();
        return (new MusicPlaylistOptionProvider(music, manage), manage);
    }

    [Fact]
    public async Task Playlist_option_maps_provider_id_name_track_count_and_image()
    {
        (MusicPlaylistOptionProvider provider, IMusicProviderManageApi manage) =
            NewPlaylistProvider("spotify");
        manage
            .ListPlaylistsAsync(Broadcaster, "spotify", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<MusicPlaylistDto>>([
                    Playlist("6GaEADx2wNUWA6DJsMVcct", "Banger", 42),
                ])
            );

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            Search(null)
        );

        Assert.True(result.Value.SourceAvailable);
        PipelineOption option = Assert.Single(result.Value.Items);
        Assert.Equal("6GaEADx2wNUWA6DJsMVcct", option.Value);
        Assert.Equal("Banger", option.Label);
        Assert.Equal("42 tracks · spotify", option.SecondaryText);
        Assert.Equal("https://img.example/6GaEADx2wNUWA6DJsMVcct", option.ImageUrl);
        Assert.Equal(PipelineOptionState.Selectable, option.State);
    }

    [Fact]
    public async Task Playlist_search_filters_by_name_and_value_lookup_finds_the_saved_id()
    {
        (MusicPlaylistOptionProvider provider, IMusicProviderManageApi manage) =
            NewPlaylistProvider("spotify");
        manage
            .ListPlaylistsAsync(Broadcaster, "spotify", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<MusicPlaylistDto>>([
                    Playlist("a1", "Chill beats", 3),
                    Playlist("b2", "Banger", 9),
                ])
            );

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            Search("bang")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            new PipelineOptionQuery(Search: null, Value: "a1", DefaultPage)
        );

        Assert.Equal("b2", Assert.Single(searched.Value.Items).Value);
        Assert.Equal("Chill beats", Assert.Single(looked.Value.Items).Label);
    }

    [Fact]
    public async Task Playlist_source_is_unavailable_with_a_reason_when_no_provider_is_connected()
    {
        (MusicPlaylistOptionProvider provider, IMusicProviderManageApi manage) =
            NewPlaylistProvider(null);

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            Search(null)
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.SourceAvailable);
        Assert.Equal("No music provider is connected.", result.Value.UnavailableReason);
        Assert.Empty(result.Value.Items);
        await manage.DidNotReceiveWithAnyArgs().ListPlaylistsAsync(default, default!, default);
    }

    [Fact]
    public async Task Playlist_source_is_unavailable_with_the_failure_message_when_the_read_fails()
    {
        (MusicPlaylistOptionProvider provider, IMusicProviderManageApi manage) =
            NewPlaylistProvider("spotify");
        manage
            .ListPlaylistsAsync(Broadcaster, "spotify", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<MusicPlaylistDto>>(
                    "Reconnect Spotify.",
                    "MUSIC_REAUTH"
                )
            );

        Result<PipelineOptionListResult> result = await provider.GetOptionsAsync(
            Broadcaster,
            Search(null)
        );

        Assert.False(result.Value.SourceAvailable);
        Assert.Equal("Reconnect Spotify.", result.Value.UnavailableReason);
    }

    // ── step descriptors ──────────────────────────────────────────────────────

    [Fact]
    public void Run_code_step_exposes_the_script_id_as_a_script_picker()
    {
        RunCodeAction action = new(Substitute.For<IScriptRunner>());

        Assert.Equal(PipelineActionFieldKind.Script, FieldOf(action.Fields, "code_script_id").Kind);
    }

    [Fact]
    public void Playlist_add_step_exposes_a_playlist_picker_and_a_templated_track()
    {
        PlaylistAddAction action = new(
            Substitute.For<IMusicService>(),
            Substitute.For<IMusicProviderManageApi>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IBuiltinResponseComposer>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<PlaylistAddAction>.Instance
        );

        Assert.Equal(
            PipelineActionFieldKind.MusicPlaylist,
            FieldOf(action.Fields, "playlist_id").Kind
        );
        Assert.True(FieldOf(action.Fields, "track_uri").Templated);
    }

    private static PipelineActionFieldDescriptor FieldOf(
        IReadOnlyList<PipelineActionFieldDescriptor> fields,
        string key
    ) => fields.Single(f => f.Name == key);
}
