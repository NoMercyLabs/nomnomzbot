// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// The <c>event_response</c> template payload — the portable part of an <see cref="EventResponse"/> row. It
/// carries no pipeline id: a pipeline-type response binds one of the installing channel's own pipelines at
/// install time.
/// </summary>
public sealed record EventResponseTemplatePayload
{
    public const string ChatMessage = "chat_message";
    public const string Pipeline = "pipeline";

    public static IReadOnlyList<string> ResponseTypes { get; } =
    [ChatMessage, "overlay", Pipeline, "none"];

    public string EventType { get; init; } = string.Empty;
    public string ResponseType { get; init; } = ChatMessage;
    public string? Message { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
    public bool IsEnabled { get; init; } = true;

    public static EventResponseTemplatePayload FromEntity(EventResponse row) =>
        new()
        {
            EventType = row.EventType,
            ResponseType = row.ResponseType,
            Message = string.IsNullOrEmpty(row.Message) ? null : row.Message,
            Metadata = new Dictionary<string, string>(row.MetadataJson),
            IsEnabled = row.IsEnabled,
        };

    public string ComputeHash() => PlatformTemplateJson.Hash(this);
}
