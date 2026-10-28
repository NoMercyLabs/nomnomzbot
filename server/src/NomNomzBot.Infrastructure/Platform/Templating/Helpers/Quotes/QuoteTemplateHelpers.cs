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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Composing;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Quotes;

/// <summary>
/// The quote rows (catalog section 7.5): <c>quote:n</c>, its parts (<c>quote.text</c>, <c>.author</c>, <c>.game</c>,
/// <c>.date</c>), <c>quote.count</c>, <c>quote.latest</c> and <c>random.quote</c>, read from the channel's
/// <see cref="NomNomzBot.Application.Quotes.Services.IQuoteService"/>. <c>random.quote</c> sits in this provider
/// because the <c>random</c> namespace already has a provider and a namespace holds one; the registry keys rows,
/// not namespaces. A quote that does not exist, or a render without a channel, prints the token as written.
/// </summary>
public sealed class QuoteTemplateHelpers(IServiceScopeFactory scopes) : ITemplateHelperProvider
{
    private const string Source = "quote";

    public string Namespace => "quote";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        Row("quote", "#12 \"that was close\""),
        Row("quote.text", "that was close"),
        Row("quote.author", "ViewerTwo"),
        Row("quote.game", "Just Chatting"),
        Row("quote.date", "2026-03-01"),
        Row("quote.count", "48"),
        Row("quote.latest", "#48 \"chat is wild\""),
        Row("random.quote", "#12 \"that was close\" (ViewerTwo, 2026-03-01)"),
    ];

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        QuoteReader reader = new(scopes, broadcasterId);
        return node.Key.ToLowerInvariant() switch
        {
            "quote.count" when node.Arguments.Count == 0 => (
                await reader.CountAsync(cancellationToken)
            )?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "quote.latest" when node.Arguments.Count == 0 => Format(
                await reader.LatestAsync(cancellationToken),
                QuoteText.Line
            ),
            "random.quote" when node.Arguments.Count == 0 => Format(
                await reader.RandomAsync(cancellationToken),
                quote => QuoteText.LineWithCredit(quote, scope.Timezone)
            ),
            "quote" or "quote.text" or "quote.author" or "quote.game" or "quote.date" =>
                await NumberedAsync(scope, node, reader, cancellationToken),
            _ => null,
        };
    }

    /// <summary>The rows that take a quote number: one whole number of at least 1.</summary>
    private static async ValueTask<string?> NumberedAsync(
        TemplateRenderScope scope,
        HelperNode node,
        QuoteReader reader,
        CancellationToken cancellationToken
    )
    {
        if (
            node.Arguments.Count != 1
            || !ComposingRows.TryPosition(node.Arguments[0], out int number)
        )
            return null;

        QuoteDto? quote = await reader.GetAsync(number, cancellationToken);
        return Format(quote, found => Part(node.Key.ToLowerInvariant(), found, scope.Timezone));
    }

    private static string Part(string key, QuoteDto quote, string? timezone) =>
        key switch
        {
            "quote.text" => quote.Text,
            "quote.author" => quote.QuotedDisplayName ?? string.Empty,
            "quote.game" => quote.ContextGame ?? string.Empty,
            "quote.date" => QuoteText.Date(quote, timezone),
            _ => QuoteText.Line(quote),
        };

    private static string? Format(QuoteDto? quote, Func<QuoteDto, string> print) =>
        quote is null ? null : print(quote);

    private static TemplateHelperEntry Row(string key, string sample) =>
        ComposingRows.Row(key, sample, TemplateHelperFreshness.Live, Source);
}
