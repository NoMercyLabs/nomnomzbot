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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>
/// <c>args.&lt;n&gt;.user.&lt;key&gt;</c> (catalog section 7.7): the n-th word of the command is read as a viewer
/// (a leading <c>@</c> is dropped, the same lookup as <c>target</c>), then any viewer row answers for that person.
/// It owns no row logic: the rows are the ones of <see cref="PeopleRowIndex"/>. The person is looked up once per
/// argument and render, in <see cref="PrepareAsync"/>; a missing word or an empty mention renders nothing.
/// </summary>
internal sealed class ArgumentUserHelpers
{
    private const string Prefix = "args.";
    private const string Marker = ".user.";
    private const string ItemPrefix = "args.user.";

    private readonly IServiceScopeFactory _scopes;
    private readonly IViewerDirectory _directory;
    private readonly PeopleRowIndex _index;

    public ArgumentUserHelpers(IServiceScopeFactory scopes, IViewerDirectory directory)
    {
        _scopes = scopes;
        _directory = directory;
        _index = new PeopleRowIndex(ViewerSides.User, ViewerSides.Target, [FollowAgeRow]);
        Entries = [.. _index.Rows.Select(ToEntry)];
    }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>True for every key shaped <c>args.&lt;n&gt;.user.&lt;something&gt;</c>.</summary>
    public static bool Claims(string key) => TryParse(key, out _, out _);

    public async ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    )
    {
        if (
            scope.BroadcasterId is not { } broadcasterId
            || !CommandWords.TryRead(scope, out CommandWords? words)
        )
            return;

        foreach (int n in Positions(nodes))
        {
            string word = words.At(n)?.TrimStart('@') ?? string.Empty;
            if (word.Length == 0 || scope.Items.ContainsKey(ItemKey(n)))
                continue;

            ViewerRef? who = await ResolveAsync(scope, word, cancellationToken);
            if (who is not null)
                scope.Items[ItemKey(n)] = new People.ViewerData(_scopes, broadcasterId, who);
        }
    }

    public async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (!TryParse(node.Key, out int n, out string suffix))
            return null;

        (PeopleRow? row, string tail) = _index.Find(suffix);
        if (row is null)
            return null;

        if (
            !scope.Items.TryGetValue(ItemKey(n), out object? stored)
            || stored is not People.ViewerData data
        )
            return string.Empty;

        return await row.Render(
            new PeopleCall(data, node.Arguments, tail, scope, cancellationToken)
        );
    }

    private async Task<ViewerRef?> ResolveAsync(
        TemplateRenderScope scope,
        string word,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, string> seeds = new(scope.Variables, StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = word,
        };
        using IServiceScope services = _scopes.CreateScope();
        return await _directory.ResolveAsync(
            services.ServiceProvider,
            ViewerSide.Target,
            seeds,
            cancellationToken
        );
    }

    private static IEnumerable<int> Positions(IEnumerable<HelperNode> nodes) =>
        nodes
            .Select(node => TryParse(node.Key, out int n, out _) ? n : 0)
            .Where(n => n > 0)
            .Distinct();

    private static string ItemKey(int n) => ItemPrefix + n.ToString(CultureInfo.InvariantCulture);

    private static bool TryParse(string key, out int n, out string suffix)
    {
        n = 0;
        suffix = string.Empty;
        if (!key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        int at = key.IndexOf(Marker, Prefix.Length, StringComparison.OrdinalIgnoreCase);
        if (at < 0 || at + Marker.Length >= key.Length)
            return false;

        if (
            !int.TryParse(
                key.AsSpan(Prefix.Length, at - Prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out n
            )
            || n < 1
        )
            return false;

        suffix = key[(at + Marker.Length)..];
        return true;
    }

    private static TemplateHelperEntry ToEntry(PeopleRow row) =>
        new(
            Prefix + "<n>" + Marker + row.Suffix + (row.IsPrefix ? "<key>" : string.Empty),
            [TemplateHelperContext.Command, TemplateHelperContext.Pipeline],
            new LocalizedText("template.helper.args.user." + row.Suffix.TrimEnd('.')),
            Prefix
        )
        {
            Sample = row.TargetSample ?? row.Sample,
            Source = row.Source,
            Freshness = row.Freshness,
            Scope = TemplateHelperScope.Global,
        };

    /// <summary>
    /// How long the person has followed, in the words of the old <c>!followage</c>. The <c>user.followAge</c> token
    /// stays with the as-built helper; only this argument form reads the follow record.
    /// </summary>
    private static PeopleRow FollowAgeRow { get; } =
        PeopleRow.Live(
            "followAge",
            ViewerSides.Both,
            "9 months and 24 days",
            "follow",
            PeopleRow.Map(
                d => d.FollowAsync(),
                (follow, call) =>
                    follow switch
                    {
                        { State: FollowState.Following, FollowedAt: { } at } =>
                            LegacyAgeText.FollowAge(call.Now - at),
                        { State: FollowState.NotFollowing } => "not following",
                        _ => "unknown",
                    }
            )
        );
}
