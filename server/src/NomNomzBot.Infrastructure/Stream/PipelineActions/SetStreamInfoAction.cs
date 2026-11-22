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

using System.Text.Json;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Stream.PipelineActions;

/// <summary>
/// Pipeline action that sets the stream's title, category and tags on the channel's own platform, through
/// <see cref="IPlatformChannelApi"/> so every platform that implements the call gets it. A platform that
/// cannot take the update reports it in the failure text; it is never dropped silently.
///
/// Parameters (each one optional, but at least one needs a value):
///   title    — The new stream title. Supports variables, e.g. "{args}" for a "!title" command.
///   category — The category (game) name, picked by search. The platform resolves it to its catalogue spelling.
///   tags     — The new tag list; it replaces the channel's tags.
///
/// An empty value means "leave unchanged": a blank title, a blank category and an empty tag list become a null
/// member in the update, never an empty string or list sent through (Twitch would refuse the empty title and
/// clear every tag). A step with no value at all fails without a call.
///
/// Usage example:
///   { "type": "set_stream_info", "title": "{args}", "category": "Just Chatting", "tags": ["english", "chill"] }
/// </summary>
public sealed class SetStreamInfoAction : ICommandAction
{
    // Twitch Modify Channel Information: "A channel may specify a maximum of 10 tags" and "Each tag is
    // limited to a maximum of 25 characters".
    internal const int MaxTags = 10;
    internal const int MaxTagLength = 25;

    private readonly IPlatformChannelApi _platform;
    private readonly ITemplateResolver _resolver;

    public string ActionType => "set_stream_info";

    public LocalizedText Category => new("pipeline.category.stream");

    public LocalizedText Description => new("pipeline.set_stream_info.description");

    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "title",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.set_stream_info.title.help")
            ),
            new(
                "category",
                PipelineActionFieldKind.StreamCategory,
                Description: new("pipeline.set_stream_info.category.help")
            ),
            new(
                "tags",
                PipelineActionFieldKind.Text,
                Repeatable: true,
                Description: new("pipeline.set_stream_info.tags.help"),
                MaxItems: MaxTags
            ),
        ];

    public SetStreamInfoAction(IPlatformChannelApi platform, ITemplateResolver resolver)
    {
        _platform = platform;
        _resolver = resolver;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string rawTitle = action.GetString("title") ?? string.Empty;
        string title = (
            await _resolver.ResolveAsync(
                rawTitle,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            )
        ).Trim();
        string category = (action.GetString("category") ?? string.Empty).Trim();
        List<string> tags = ReadTags(action);

        if (tags.Count > MaxTags)
            return ActionResult.Failure(
                $"set_stream_info takes at most {MaxTags} tags, but {tags.Count} were given"
            );

        string? tooLong = tags.Find(t => t.Length > MaxTagLength);
        if (tooLong is not null)
            return ActionResult.Failure(
                $"set_stream_info tags are at most {MaxTagLength} characters long, but '{tooLong}' is {tooLong.Length}"
            );

        PlatformStreamInfoUpdate update = new(
            Title: title.Length > 0 ? title : null,
            CategoryName: category.Length > 0 ? category : null,
            Tags: tags.Count > 0 ? tags : null
        );
        if (update.Title is null && update.CategoryName is null && update.Tags is null)
            return ActionResult.Failure(
                "set_stream_info needs at least one of a title, a category or tags"
            );

        Result<PlatformStreamInfoApplied> applied = await _platform.UpdateStreamInfoAsync(
            ctx.BroadcasterId,
            update,
            ctx.CancellationToken
        );
        if (applied.IsSuccess)
            return ActionResult.Success("stream info updated");

        return applied.ErrorCode == TwitchErrorCodes.MissingScope
            ? ActionResult.Failure(
                $"set_stream_info is missing a permission ({applied.ErrorMessage}); re-grant it from the dashboard's connection status",
                applied.ErrorCode
            )
            : ActionResult.Failure(
                applied.ErrorMessage ?? "set_stream_info failed on the platform",
                applied.ErrorCode
            );
    }

    // The saved tags are a JSON array of text; blank entries are dropped so an empty list means "unchanged".
    private static List<string> ReadTags(ActionDefinition action)
    {
        if (
            action.Parameters is null
            || !action.Parameters.TryGetValue("tags", out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
            return [];

        return
        [
            .. element
                .EnumerateArray()
                .Select(item =>
                    (item.ValueKind == JsonValueKind.String ? item.GetString() : null)?.Trim()
                )
                .OfType<string>()
                .Where(tag => tag.Length > 0),
        ];
    }
}
