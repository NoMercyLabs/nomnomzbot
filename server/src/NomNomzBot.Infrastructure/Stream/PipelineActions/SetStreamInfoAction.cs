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

    private static readonly string[] BrandedOptions = ["on", "off"];

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
            new(
                "language",
                PipelineActionFieldKind.StreamLanguage,
                Description: new("pipeline.set_stream_info.language.help")
            ),
            new(
                "labels_on",
                PipelineActionFieldKind.Enum,
                Repeatable: true,
                Options: TwitchContentLabels.Editable,
                Description: new("pipeline.set_stream_info.labels_on.help")
            ),
            new(
                "labels_off",
                PipelineActionFieldKind.Enum,
                Repeatable: true,
                Options: TwitchContentLabels.Editable,
                Description: new("pipeline.set_stream_info.labels_off.help")
            ),
            new(
                "branded_content",
                PipelineActionFieldKind.Enum,
                Options: BrandedOptions,
                Description: new("pipeline.set_stream_info.branded_content.help")
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
        JsonElement tagsElement = default;
        action.Parameters?.TryGetValue("tags", out tagsElement);
        List<string> tags = ReadList(tagsElement);

        if (tags.Count > MaxTags)
            return ActionResult.Failure(
                $"set_stream_info takes at most {MaxTags} tags, but {tags.Count} were given"
            );

        string? tooLong = tags.Find(t => t.Length > MaxTagLength);
        if (tooLong is not null)
            return ActionResult.Failure(
                $"set_stream_info tags are at most {MaxTagLength} characters long, but '{tooLong}' is {tooLong.Length}"
            );

        string language = (action.GetString("language") ?? string.Empty).Trim().ToLowerInvariant();
        if (language.Length > 0 && !TwitchStreamLanguages.IsKnown(language))
            return ActionResult.Failure(
                $"set_stream_info language must be a language Twitch takes, such as 'en', 'asl' or 'other', but '{language}' was given"
            );

        JsonElement labelsOnElement = default;
        action.Parameters?.TryGetValue("labels_on", out labelsOnElement);
        JsonElement labelsOffElement = default;
        action.Parameters?.TryGetValue("labels_off", out labelsOffElement);
        List<string> labelsOn = ReadList(labelsOnElement);
        List<string> labelsOff = ReadList(labelsOffElement);
        string? badLabel = labelsOn
            .Concat(labelsOff)
            .FirstOrDefault(id => !TwitchContentLabels.Editable.Contains(id));
        if (badLabel is not null)
            return ActionResult.Failure(
                $"set_stream_info cannot set the content label '{badLabel}'; the labels that can be set are {string.Join(", ", TwitchContentLabels.Editable)}"
            );

        string? both = labelsOn.Find(labelsOff.Contains);
        if (both is not null)
            return ActionResult.Failure(
                $"set_stream_info content label '{both}' is in both the on list and the off list"
            );

        string branded = (action.GetString("branded_content") ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        if (branded.Length > 0 && !BrandedOptions.Contains(branded))
            return ActionResult.Failure(
                $"set_stream_info branded_content must be 'on' or 'off', but '{branded}' was given"
            );

        Dictionary<string, bool> labelChanges = [];
        foreach (string id in labelsOn)
            labelChanges[id] = true;
        foreach (string id in labelsOff)
            labelChanges[id] = false;

        PlatformStreamInfoUpdate update = new(
            Title: title.Length > 0 ? title : null,
            CategoryName: category.Length > 0 ? category : null,
            Tags: tags.Count > 0 ? tags : null,
            IsBrandedContent: branded.Length > 0 ? branded == "on" : null,
            Language: language.Length > 0 ? language : null,
            ContentLabelChanges: labelChanges.Count > 0 ? labelChanges : null
        );
        if (
            update.Title is null
            && update.CategoryName is null
            && update.Tags is null
            && update.IsBrandedContent is null
            && update.Language is null
            && update.ContentLabelChanges is null
        )
            return ActionResult.Failure(
                "set_stream_info needs at least one of a title, a category, tags, a language, content labels or branded content"
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

    // A saved list is a JSON array of text; blank entries are dropped so an empty list means "unchanged".
    // A missing parameter arrives as a default element, which is not an array.
    private static List<string> ReadList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
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
