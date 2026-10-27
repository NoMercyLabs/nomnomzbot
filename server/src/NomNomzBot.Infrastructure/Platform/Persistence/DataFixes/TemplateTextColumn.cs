// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Persistence.DataFixes;

/// <summary>How a template column is stored in PostgreSQL. SQLite stores every one of them as TEXT.</summary>
public enum TemplateTextColumnShape
{
    /// <summary>A text or varchar column (plain template text, or JSON kept as text).</summary>
    Text,

    /// <summary>A jsonb column.</summary>
    Jsonb,

    /// <summary>A text[] column, one template per element.</summary>
    TextArray,
}

/// <summary>
/// One stored column that holds template text. <paramref name="MaxLength"/> is the varchar limit PostgreSQL
/// enforces, or null when the column has none.
/// </summary>
public sealed record TemplateTextColumn(
    string Table,
    string Column,
    TemplateTextColumnShape Shape = TemplateTextColumnShape.Text,
    int? MaxLength = null
)
{
    /// <summary>
    /// Every stored column the template resolver reads: the <c>[TemplatedUserContent]</c> properties, the
    /// platform defaults, and the pipeline, binding, widget, webhook and Discord JSON that carry template text.
    /// </summary>
    public static IReadOnlyList<TemplateTextColumn> All { get; } =
    [
        new("Commands", "TemplateResponse", MaxLength: 2000),
        new("Commands", "TemplateResponses", TemplateTextColumnShape.Jsonb),
        new("Timers", "Messages", TemplateTextColumnShape.Jsonb),
        new("Timers", "TargetActionParamsJson"),
        new("EventResponses", "Message", MaxLength: 2000),
        new("EventResponses", "TargetActionParamsJson"),
        new("ChatTriggers", "Response", MaxLength: 500),
        new("ChatTriggers", "TargetActionParamsJson"),
        new("Rewards", "Response", MaxLength: 2000),
        new("Rewards", "PipelineJson", TemplateTextColumnShape.Jsonb),
        new("Rewards", "TargetActionParamsJson"),
        new("Channels", "ShoutoutTemplate", MaxLength: 450),
        new("ShoutoutOverrides", "MessageTemplate", MaxLength: 1000),
        new("DiscordNotificationConfigs", "MessageTemplate"),
        new("DiscordNotificationConfigs", "EmbedConfig"),
        new("OutboundWebhookEndpoints", "BodyTemplate"),
        new("OutboundWebhookEndpoints", "CustomHeadersJson"),
        new("PlatformEventResponseDefaults", "Message", MaxLength: 2000),
        new("PlatformBuiltinReplyDefaults", "Template", MaxLength: 500),
        new("PickLists", "Items", TemplateTextColumnShape.TextArray),
        new("PipelineSteps", "ConfigJson"),
        new("PipelineSteps", "BlockConfigJson"),
        new("Pipelines", "GraphJsonCache"),
        new("ScheduledPipelineTasks", "TargetActionParamsJson"),
        new("Giveaways", "TargetActionParamsJson"),
        new("CatalogItems", "TargetActionParamsJson"),
        new("Widgets", "Settings"),
        new("WidgetGalleryItems", "DefaultSettings"),
        new("PlatformContentVersions", "PayloadJson"),
    ];
}
