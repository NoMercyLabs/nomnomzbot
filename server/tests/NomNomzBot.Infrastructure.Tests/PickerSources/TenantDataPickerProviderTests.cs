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
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Infrastructure.Giveaways.Pipeline;
using NomNomzBot.Infrastructure.Quotes.Pipeline;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;
using NomNomzBot.Infrastructure.Webhooks.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// The pickers over data one channel owns: its giveaways, its quotes, its outbound webhook endpoints. Each test
/// puts a second channel's rows in the same source and proves they never reach the first channel's list.
/// </summary>
public sealed class TenantDataPickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private static readonly Guid OtherBroadcaster = Guid.NewGuid();

    // ── giveaway ─────────────────────────────────────────────────────────────

    private static GiveawayDto Giveaway(Guid id, string title, string status) =>
        new(
            id,
            title,
            "keyword",
            null,
            null,
            1,
            null,
            null,
            1,
            false,
            null,
            "none",
            null,
            false,
            null,
            null,
            null,
            false,
            status,
            null,
            null,
            null,
            null,
            0,
            DateTime.UtcNow
        );

    private static IGiveawayService Giveaways(Guid owner, params GiveawayDto[] items)
    {
        IGiveawayService service = Substitute.For<IGiveawayService>();
        service
            .ListAsync(
                owner,
                Arg.Any<GiveawayFilter>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                PaginationParams page = call.Arg<PaginationParams>();
                GiveawayDto[] window =
                [
                    .. items.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize),
                ];
                return Result.Success(
                    new PagedList<GiveawayDto>(window, page.Page, page.PageSize, items.Length)
                );
            });
        return service;
    }

    [Fact]
    public async Task Giveaway_lists_the_channels_giveaways_with_the_id_saved_and_the_status_shown()
    {
        Guid summer = Guid.NewGuid();
        Guid winter = Guid.NewGuid();
        GiveawayOptionProvider provider = new(
            Giveaways(
                Broadcaster,
                Giveaway(summer, "Summer", "open"),
                Giveaway(winter, "Winter", "draft")
            )
        );

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "win")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: OwnedIdCodec.Encode(summer))
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );

        Assert.Equal(PipelineActionFieldKind.Giveaway, provider.Kind);
        Assert.Equal([summer.ToString(), winter.ToString()], PickerAsk.Values(all));
        Assert.Equal("open", all.Value.Items[0].SecondaryText);
        Assert.Equal([winter.ToString()], PickerAsk.Values(searched));
        Assert.Equal([summer.ToString()], PickerAsk.Values(looked));
        Assert.Equal([winter.ToString()], PickerAsk.Values(second));
        Assert.Equal(2, second.Value.TotalCount);
    }

    [Fact]
    public async Task Giveaway_never_shows_another_channels_giveaways_and_says_why_when_unreadable()
    {
        IGiveawayService service = Giveaways(Broadcaster, Giveaway(Guid.NewGuid(), "Mine", "open"));
        service
            .ListAsync(
                OtherBroadcaster,
                Arg.Any<GiveawayFilter>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new PagedList<GiveawayDto>(
                        [Giveaway(Guid.NewGuid(), "Theirs", "open")],
                        1,
                        100,
                        1
                    )
                )
            );
        IGiveawayService broken = Substitute.For<IGiveawayService>();
        broken
            .ListAsync(
                Broadcaster,
                Arg.Any<GiveawayFilter>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<PagedList<GiveawayDto>>("Giveaways are offline.", "X"));

        Result<PipelineOptionListResult> result = await new GiveawayOptionProvider(
            service
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["Mine"], result.Value.Items.Select(i => i.Label));
        PickerAsk.AssertUnavailable(
            await new GiveawayOptionProvider(broken).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            "Giveaways are offline."
        );
    }

    // ── quote ────────────────────────────────────────────────────────────────

    private static QuoteDto Quote(int number, string text, string? who = null) =>
        new(Guid.NewGuid(), number, text, who, null, null, DateTime.UtcNow);

    private static IQuoteService Quotes(Guid owner, params QuoteDto[] items)
    {
        IQuoteService service = Substitute.For<IQuoteService>();
        service
            .ListAsync(
                owner,
                Arg.Any<QuoteSearch>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                QuoteSearch search = call.Arg<QuoteSearch>();
                PaginationParams page = call.Arg<PaginationParams>();
                QuoteDto[] matches =
                [
                    .. items
                        .Where(q =>
                            search.Term is null
                            || q.Text.Contains(search.Term, StringComparison.OrdinalIgnoreCase)
                        )
                        .OrderByDescending(q => q.Number),
                ];
                QuoteDto[] window =
                [
                    .. matches.Skip((page.Page - 1) * page.PageSize).Take(page.PageSize),
                ];
                return Result.Success(
                    new PagedList<QuoteDto>(window, page.Page, page.PageSize, matches.Length)
                );
            });
        return service;
    }

    [Fact]
    public async Task Quote_lists_the_channels_quotes_newest_first_with_the_number_saved_and_the_text_shown()
    {
        QuoteOptionProvider provider = new(
            Quotes(
                Broadcaster,
                Quote(1, "first words", "Ann"),
                Quote(2, "second words"),
                Quote(3, "third words")
            )
        );

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "second")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "1")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 2))
        );

        Assert.Equal(PipelineActionFieldKind.Quote, provider.Kind);
        Assert.Equal(["3", "2", "1"], PickerAsk.Values(all));
        Assert.Equal("#3", all.Value.Items[0].Label);
        Assert.Equal("third words", all.Value.Items[0].SecondaryText);
        Assert.Equal(["2"], PickerAsk.Values(searched));
        Assert.Equal(["1"], PickerAsk.Values(looked));
        Assert.Equal("first words", looked.Value.Items[0].SecondaryText);
        Assert.Equal(["1"], PickerAsk.Values(second));
        Assert.Equal(3, second.Value.TotalCount);
    }

    [Fact]
    public async Task Quote_value_lookup_finds_a_quote_past_the_first_service_page()
    {
        QuoteDto[] many = [.. Enumerable.Range(1, 150).Select(i => Quote(i, $"line {i}"))];

        Result<PipelineOptionListResult> result = await new QuoteOptionProvider(
            Quotes(Broadcaster, many)
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of(value: "3"));

        Assert.Equal(["3"], PickerAsk.Values(result));
    }

    [Fact]
    public async Task Quote_never_shows_another_channels_quotes_and_says_why_when_unreadable()
    {
        IQuoteService service = Quotes(Broadcaster, Quote(1, "mine"));
        service
            .ListAsync(
                OtherBroadcaster,
                Arg.Any<QuoteSearch>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<QuoteDto>([Quote(9, "theirs")], 1, 100, 1)));
        IQuoteService broken = Substitute.For<IQuoteService>();
        broken
            .ListAsync(
                Broadcaster,
                Arg.Any<QuoteSearch>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<PagedList<QuoteDto>>("Quotes are offline.", "X"));

        Result<PipelineOptionListResult> result = await new QuoteOptionProvider(
            service
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["1"], PickerAsk.Values(result));
        PickerAsk.AssertUnavailable(
            await new QuoteOptionProvider(broken).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            "Quotes are offline."
        );
    }

    // ── webhook endpoint ─────────────────────────────────────────────────────

    private static OutboundWebhookEndpoint Endpoint(Guid owner, string name, string fqdn) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = owner,
            Name = name,
            Fqdn = fqdn,
            SigningSecretEnvelope = "sealed",
            IsEnabled = true,
        };

    [Fact]
    public async Task Webhook_endpoint_lists_only_the_channels_endpoints_with_the_id_saved_and_the_host_shown()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        OutboundWebhookEndpoint mine = Endpoint(Broadcaster, "Discord relay", "hooks.example.com");
        OutboundWebhookEndpoint theirs = Endpoint(OtherBroadcaster, "Theirs", "other.example.com");
        OutboundWebhookEndpoint deleted = Endpoint(Broadcaster, "Old", "old.example.com");
        deleted.DeletedAt = DateTime.UtcNow;
        db.OutboundWebhookEndpoints.AddRange(mine, theirs, deleted);
        await db.SaveChangesAsync();
        WebhookEndpointOptionProvider provider = new(db);

        Result<PipelineOptionListResult> all = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of()
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: OwnedIdCodec.Encode(mine.Id))
        );
        Result<PipelineOptionListResult> foreign = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: theirs.Id.ToString())
        );

        Assert.Equal(PipelineActionFieldKind.WebhookEndpoint, provider.Kind);
        Assert.Equal([mine.Id.ToString()], PickerAsk.Values(all));
        Assert.Equal("Discord relay", all.Value.Items[0].Label);
        Assert.Equal("hooks.example.com", all.Value.Items[0].SecondaryText);
        Assert.Equal([mine.Id.ToString()], PickerAsk.Values(looked));
        Assert.Empty(foreign.Value.Items);
        Assert.True(foreign.Value.SourceAvailable);
    }

    [Fact]
    public async Task Webhook_endpoint_search_and_paging_narrow_the_list()
    {
        using PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.OutboundWebhookEndpoints.AddRange(
            Endpoint(Broadcaster, "Alpha", "a.example.com"),
            Endpoint(Broadcaster, "Beta", "b.example.com")
        );
        await db.SaveChangesAsync();
        WebhookEndpointOptionProvider provider = new(db);

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "BET")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );

        Assert.Equal(["Beta"], searched.Value.Items.Select(i => i.Label));
        Assert.Equal(["Beta"], second.Value.Items.Select(i => i.Label));
        Assert.Equal(2, second.Value.TotalCount);
    }
}
