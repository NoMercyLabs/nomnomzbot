// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NomNomzBot.Infrastructure.PickLists;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// The <c>pick_list</c> platform-template kind, end to end: author and publish, then install through the real
/// <see cref="PickListService"/>. The list name is the <c>{list.pick.name}</c> key, so install keeps it
/// verbatim and refuses a name the channel already uses rather than inventing a different key.
/// </summary>
public sealed class PlatformTemplatePickListTests : IAsyncDisposable
{
    private const string GreetingsTemplate =
        """{"name":"greetings","description":"Warm hellos","items":["Hey {user}!","Welcome in, {user}!","  "]}""";

    private readonly PlatformTemplateHarness _h = new();
    private readonly PickListTemplateInstaller _installer;

    public PlatformTemplatePickListTests()
    {
        PickListService pickLists = new(
            _h.Db,
            Substitute.For<IEventBus>(),
            Substitute.For<IPipelineStepReferenceScanner>()
        );
        _installer = new(_h.Db, pickLists);
    }

    [Fact]
    public async Task Install_adds_the_list_with_the_template_entries_and_provenance()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "greetings",
            GreetingsTemplate
        );

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        PickList row = await _h.Db.PickLists.SingleAsync();
        row.Id.Should().Be(result.Value.EntityId);
        row.BroadcasterId.Should().Be(channel.Id);
        row.Name.Should().Be("greetings");
        row.Description.Should().Be("Warm hellos");
        row.Items.Should().Equal("Hey {user}!", "Welcome in, {user}!");
        row.PlatformSourceDefinitionId.Should().Be(definitionId);
        row.PlatformSourceVersion.Should().Be(1);
        row.PlatformSourceHash.Should().Be(PickListTemplatePayload.FromEntity(row).ComputeHash());
    }

    [Fact]
    public async Task A_taken_name_is_refused_and_neither_channels_list_changes()
    {
        Channel channelA = await _h.AddChannelAsync("streamer-a");
        Channel channelB = await _h.AddChannelAsync("streamer-b");
        _h.Db.PickLists.Add(
            new PickList
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelB.Id,
                Name = "greetings",
                Items = ["B's own hello"],
            }
        );
        await _h.Db.SaveChangesAsync();
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "greetings",
            GreetingsTemplate
        );
        PlatformTemplateCatalogService catalog = _h.Catalog(_installer);

        Result<InstalledPlatformTemplateDto> intoB = await catalog.InstallAsync(
            _h.CallerUserId,
            channelB.Id,
            definitionId,
            new(null)
        );
        Result<InstalledPlatformTemplateDto> intoA = await catalog.InstallAsync(
            _h.CallerUserId,
            channelA.Id,
            definitionId,
            new(null)
        );

        intoB.ErrorCode.Should().Be("ALREADY_EXISTS");
        intoA.IsSuccess.Should().BeTrue(intoA.ErrorMessage);
        List<PickList> rows = await _h.Db.PickLists.AsNoTracking().ToListAsync();
        PickList ownOfB = rows.Single(p => p.BroadcasterId == channelB.Id);
        ownOfB.Items.Should().Equal("B's own hello");
        ownOfB.PlatformSourceDefinitionId.Should().BeNull();
        rows.Single(p => p.BroadcasterId == channelA.Id)
            .PlatformSourceDefinitionId.Should()
            .Be(definitionId);
    }

    [Theory]
    [InlineData("""{"name":"","items":["a"]}""")]
    [InlineData("""{"name":"has space","items":["a"]}""")]
    [InlineData("""{"name":"ok","items":[]}""")]
    [InlineData("""{"name":"ok","items":["  "]}""")]
    [InlineData("""{"name":"{list}","items":["a"]}""")]
    public async Task Authoring_rejects_a_bad_payload_and_stores_nothing(string payload)
    {
        Result<PlatformContentDefinitionDto> created = await _h.AdminService(_installer)
            .CreateDefinitionAsync(
                _h.ActingPrincipalId,
                new(PlatformContentKinds.PickList, "bad", "Bad", null, payload)
            );

        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await _h.Db.PlatformContentDefinitions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Install_is_denied_without_the_pick_list_write_action()
    {
        Channel channel = await _h.AddChannelAsync("streamer-b");
        Guid definitionId = await _h.PublishTemplateAsync(
            _installer,
            "greetings",
            GreetingsTemplate
        );
        _h.AllowChannelAction(false);

        Result<InstalledPlatformTemplateDto> result = await _h.Catalog(_installer)
            .InstallAsync(_h.CallerUserId, channel.Id, definitionId, new(null));

        result.ErrorCode.Should().Be("FORBIDDEN");
        await _h
            .Authorization.Received()
            .AuthorizeActionAsync(
                _h.CallerUserId,
                channel.Id,
                "picklists:write",
                Arg.Any<CancellationToken>()
            );
        (await _h.Db.PickLists.CountAsync()).Should().Be(0);
    }

    public ValueTask DisposeAsync() => _h.DisposeAsync();
}
