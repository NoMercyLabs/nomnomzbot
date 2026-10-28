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
