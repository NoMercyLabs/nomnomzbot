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

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>The three kinds of principal that can change a channel's configuration.</summary>
public static class JournalActorKind
{
    /// <summary>A signed-in person (a dashboard session, or a support operator acting as someone).</summary>
    public const string Person = "person";

    /// <summary>A channel API key (automation token). It has no user id; <see cref="JournalActor.TokenId"/> names it.</summary>
    public const string ApiKey = "api_key";

    /// <summary>The bot itself, working from stored configuration with no person present.</summary>
    public const string System = "system";
}

/// <summary>
/// Who made a change: the kind of principal, the user id when a person acted, and the id (never the secret) of
/// the API key when a key acted.
/// </summary>
public sealed record JournalActor(string Kind, Guid? UserId = null, string? TokenId = null)
{
    public static JournalActor Person(Guid userId) => new(JournalActorKind.Person, userId);

    public static JournalActor ApiKey(string tokenId) =>
        new(JournalActorKind.ApiKey, TokenId: tokenId);

    public static readonly JournalActor System = new(JournalActorKind.System);
}

/// <summary>
/// Reads the acting principal of the current logical operation: the request principal, else the background-work
/// sanction, else the system. Singleton and cheap, so the event bus can capture it on the publishing flow before
/// it hands the event to another task.
/// </summary>
public interface IJournalActorAccessor
{
    JournalActor Current { get; }
}

/// <summary>Writes and reads the actor kind and key id in a journal row's metadata JSON (no schema column).</summary>
public static class JournalActorMetadata
{
    private const string KindKey = "actorKind";
    private const string TokenIdKey = "actorTokenId";

    public static string ToJson(JournalActor actor) =>
        actor.TokenId is null
            ? JsonSerializer.Serialize(new Dictionary<string, string> { [KindKey] = actor.Kind })
            : JsonSerializer.Serialize(
                new Dictionary<string, string>
                {
                    [KindKey] = actor.Kind,
                    [TokenIdKey] = actor.TokenId,
                }
            );

    public static string? ReadKind(string? metadataJson) => Read(metadataJson, KindKey);

    public static string? ReadTokenId(string? metadataJson) => Read(metadataJson, TokenIdKey);

    private static string? Read(string? metadataJson, string key)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            Dictionary<string, JsonElement>? values = JsonSerializer.Deserialize<
                Dictionary<string, JsonElement>
            >(metadataJson);
            return
                values is not null
                && values.TryGetValue(key, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
