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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Commands.PipelineOptions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// The pickers over the channel's own automation: a pipeline by id, a pipeline by name, a command by name. They read
/// the asking channel's list only, and read past the first page of the service so a long list is never cut short.
/// </summary>
public sealed class CommandsPickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private static readonly string BroadcasterKey = Broadcaster.ToString();

    private static PipelineListItemDto Pipe(Guid id, string name, string? description = null) =>
        new(id, name, description, true, 0, null, DateTime.UtcNow, null);

    private static CommandListItem Command(string name, string tier, string? description = null) =>
        new(
            Guid.NewGuid(),
            name,
            tier,
            "everyone",
            true,
            "default",
            null,
            "exact",
            null,
            0,
            0,
            description,
            [],
            0,
            DateTime.UtcNow,
            null,
            null,
            null
        );

    private static IPipelineService Pipelines(
        string broadcasterKey,
        params PipelineListItemDto[] items
    )
    {
        IPipelineService service = Substitute.For<IPipelineService>();
        service
            .ListAsync(broadcasterKey, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                PaginationParams page = call.Arg<PaginationParams>();
                PipelineListItemDto[] window =
                [
                    .. items.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize),
                ];
                return Result.Success(
                    new PagedList<PipelineListItemDto>(
                        window,
                        page.Page,
                        page.PageSize,
                        items.Length
                    )
                );
            });
        return service;
    }

    [Fact]
    public void Each_provider_serves_its_own_kind()
    {
        IPipelineService pipelines = Substitute.For<IPipelineService>();

        Assert.Equal(
            PipelineActionFieldKind.Pipeline,
            new PipelineIdOptionProvider(pipelines).Kind
        );
        Assert.Equal(
            PipelineActionFieldKind.PipelineName,
            new PipelineNameOptionProvider(pipelines).Kind
        );
        Assert.Equal(
            PipelineActionFieldKind.CommandName,
            new CommandNameOptionProvider(Substitute.For<ICommandService>()).Kind
        );
    }

    [Fact]
    public async Task Pipeline_by_id_saves_the_id_and_by_name_saves_the_name_from_the_same_list()
    {
        Guid welcome = Guid.NewGuid();
        Guid raid = Guid.NewGuid();
        IPipelineService service = Pipelines(
            BroadcasterKey,
            Pipe(welcome, "welcome", "Greets a new chatter"),
            Pipe(raid, "raid")
        );

        Result<PipelineOptionListResult> byId = await new PipelineIdOptionProvider(
            service
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());
        Result<PipelineOptionListResult> byName = await new PipelineNameOptionProvider(
            service
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal([raid.ToString(), welcome.ToString()], PickerAsk.Values(byId));
        Assert.Equal(["raid", "welcome"], PickerAsk.Values(byName));
        Assert.Equal("Greets a new chatter", byId.Value.Items[1].SecondaryText);
        Assert.Null(byId.Value.Items[0].SecondaryText);
    }

    [Fact]
    public async Task Pipeline_by_id_finds_a_saved_ulid_and_a_removed_pipeline_is_an_empty_available_list()
    {
        Guid id = Guid.NewGuid();
        PipelineIdOptionProvider provider = new(Pipelines(BroadcasterKey, Pipe(id, "welcome")));

        Result<PipelineOptionListResult> found = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: OwnedIdCodec.Encode(id))
        );
        Result<PipelineOptionListResult> gone = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: Guid.NewGuid().ToString())
        );

        Assert.Equal([id.ToString()], PickerAsk.Values(found));
        Assert.True(gone.Value.SourceAvailable);
        Assert.Empty(gone.Value.Items);
    }

    [Fact]
    public async Task Pipeline_by_name_search_value_lookup_and_paging_narrow_the_list()
    {
        PipelineNameOptionProvider provider = new(
            Pipelines(
                BroadcasterKey,
                Pipe(Guid.NewGuid(), "welcome"),
                Pipe(Guid.NewGuid(), "raid"),
                Pipe(Guid.NewGuid(), "welcome-back")
            )
        );

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "WELCOME")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "raid")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(["welcome", "welcome-back"], PickerAsk.Values(searched));
        Assert.Equal(["raid"], PickerAsk.Values(looked));
        Assert.Equal(["welcome-back"], PickerAsk.Values(second));
        Assert.Equal(3, second.Value.TotalCount);
    }

    [Fact]
    public async Task A_channel_with_more_pipelines_than_one_service_page_still_lists_them_all()
    {
        PipelineListItemDto[] many =
        [
            .. Enumerable.Range(1, 150).Select(i => Pipe(Guid.NewGuid(), $"flow-{i:000}")),
        ];

        Result<PipelineOptionListResult> result = await new PipelineNameOptionProvider(
            Pipelines(BroadcasterKey, many)
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of(value: "flow-150"));
        Result<PipelineOptionListResult> total = await new PipelineNameOptionProvider(
            Pipelines(BroadcasterKey, many)
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["flow-150"], PickerAsk.Values(result));
        Assert.Equal(150, total.Value.TotalCount);
    }

    [Fact]
    public async Task Another_channels_pipelines_never_appear()
    {
        string otherKey = Guid.NewGuid().ToString();
        IPipelineService service = Pipelines(BroadcasterKey, Pipe(Guid.NewGuid(), "mine"));
        service
            .ListAsync(otherKey, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<PipelineListItemDto>([Pipe(Guid.NewGuid(), "theirs")], 1, 100, 1)
                )
            );

        Result<PipelineOptionListResult> result = await new PipelineNameOptionProvider(
            service
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["mine"], PickerAsk.Values(result));
    }

    [Fact]
    public async Task An_unreadable_pipeline_list_is_unavailable_with_the_services_reason()
    {
        IPipelineService service = Substitute.For<IPipelineService>();
        service
            .ListAsync(BroadcasterKey, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PagedList<PipelineListItemDto>>("Pipelines are offline.", "X"));

        PickerAsk.AssertUnavailable(
            await new PipelineIdOptionProvider(service).GetOptionsAsync(
                Broadcaster,
                PickerAsk.Of()
            ),
            "Pipelines are offline."
        );
    }

    [Fact]
    public async Task Command_lists_the_channels_commands_by_name_with_the_description_or_tier_shown()
    {
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(BroadcasterKey, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<CommandListItem>(
                        [Command("hug", "pipeline", "Hugs someone"), Command("lurk", "simple")],
                        1,
                        100,
                        2
                    )
                )
            );
        CommandNameOptionProvider provider = new(commands);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "lur")
        );

        Assert.Equal(["hug", "lurk"], PickerAsk.Values(all));
        Assert.Equal("Hugs someone", all.Value.Items[0].SecondaryText);
        Assert.Equal("simple", all.Value.Items[1].SecondaryText);
        Assert.Equal(["lurk"], PickerAsk.Values(searched));
    }

    [Fact]
    public async Task Command_is_unavailable_with_a_reason_when_unreadable_and_never_shows_another_channels_commands()
    {
        string otherKey = Guid.NewGuid().ToString();
        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(BroadcasterKey, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PagedList<CommandListItem>>("Commands are offline.", "X"));
        commands
            .ListAsync(otherKey, Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new PagedList<CommandListItem>([Command("theirs", "simple")], 1, 100, 1)
                )
            );

        PickerAsk.AssertUnavailable(
            await new CommandNameOptionProvider(commands).GetOptionsAsync(
                Broadcaster,
                PickerAsk.Of()
            ),
            "Commands are offline."
        );
    }
}
