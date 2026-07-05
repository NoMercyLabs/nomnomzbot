// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Discord.Gateway;

/// <summary>
/// The real Discord REST adapter (discord.md §3.5) — the ONLY thing that talks to Discord. A hand-rolled client
/// over the named <c>discord</c> <see cref="HttpClient"/> (resilience + 429 <c>Retry-After</c> honoring layered
/// in DI), calling <c>https://discord.com/api/v10</c>:
/// <list type="bullet">
///   <item><c>POST /channels/{id}/messages</c> for a message + optional embed + role ping;</item>
///   <item><c>PUT/DELETE /guilds/{g}/members/{u}/roles/{r}</c> for opt-in/out role enforcement.</item>
/// </list>
/// Every call resolves the tenant's decrypted bot token from <see cref="IIntegrationTokenVault"/> per call
/// (the discord <c>IntegrationConnection</c> → vault) and sends it as <c>Authorization: Bot {token}</c>; a
/// crypto-shredded DEK or a missing connection surfaces as <see cref="Result.Failure(string, string?, string?)"/>.
/// Nothing here is stubbed — this is a live HTTP client against the Discord REST API.
/// </summary>
public sealed class DiscordRestBotGateway : IDiscordBotGateway
{
    internal const string DiscordApiBase = "https://discord.com/api/v10";
    private const string Provider = "discord";

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly ILogger<DiscordRestBotGateway> _logger;

    public DiscordRestBotGateway(
        IHttpClientFactory httpClientFactory,
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        ILogger<DiscordRestBotGateway> logger
    )
    {
        _http = httpClientFactory.CreateClient("discord");
        _db = db;
        _vault = vault;
        _logger = logger;
    }

    public async Task<Result<string>> PostMessageAsync(
        Guid broadcasterId,
        string targetChannelId,
        DiscordOutboundMessage message,
        CancellationToken ct = default
    )
    {
        Result<string> token = await ResolveBotTokenAsync(broadcasterId, ct);
        if (token.IsFailure)
            return token;

        DiscordMessagePayload payload = new(
            BuildContent(message.Content, message.PingRoleId),
            message.Embed is null ? null : [ToWireEmbed(message.Embed)],
            BuildAllowedMentions(message.PingRoleId)
        );

        return await PostMessagePayloadAsync(token.Value, targetChannelId, payload, ct);
    }

    public async Task<Result<string>> PostButtonMessageAsync(
        Guid broadcasterId,
        string targetChannelId,
        DiscordOptInButton button,
        CancellationToken ct = default
    )
    {
        Result<string> token = await ResolveBotTokenAsync(broadcasterId, ct);
        if (token.IsFailure)
            return token;

        // A Discord message action row (type 1) carrying a single primary button (type 2). The custom_id
        // carries the notify-role id so the interaction handler knows which role to toggle.
        DiscordMessagePayload payload = new(
            button.MessageContent,
            Embeds: null,
            AllowedMentions: null,
            Components:
            [
                new DiscordComponent(
                    Type: 1,
                    Components:
                    [
                        new DiscordComponent(
                            Type: 2,
                            Style: 1,
                            Label: button.ButtonLabel,
                            CustomId: $"notify_optin:{button.NotificationRoleId:N}"
                        ),
                    ]
                ),
            ]
        );

        return await PostMessagePayloadAsync(token.Value, targetChannelId, payload, ct);
    }

    public async Task<Result> AddMemberRoleAsync(
        Guid broadcasterId,
        string guildId,
        string discordMemberId,
        string discordRoleId,
        CancellationToken ct = default
    ) =>
        await ModifyMemberRoleAsync(
            broadcasterId,
            HttpMethod.Put,
            guildId,
            discordMemberId,
            discordRoleId,
            ct
        );

    public async Task<Result> RemoveMemberRoleAsync(
        Guid broadcasterId,
        string guildId,
        string discordMemberId,
        string discordRoleId,
        CancellationToken ct = default
    ) =>
        await ModifyMemberRoleAsync(
            broadcasterId,
            HttpMethod.Delete,
            guildId,
            discordMemberId,
            discordRoleId,
            ct
        );

    public async Task<Result<DiscordGuildInfoDto>> GetGuildAsync(
        Guid broadcasterId,
        string guildId,
        CancellationToken ct = default
    )
    {
        Result<DiscordGuildWire> guild = await GetAsync<DiscordGuildWire>(
            broadcasterId,
            $"guilds/{Uri.EscapeDataString(guildId)}",
            ct
        );
        if (guild.IsFailure)
            return Result.Failure<DiscordGuildInfoDto>(
                guild.ErrorMessage,
                guild.ErrorCode,
                guild.ErrorDetail
            );
        return Result.Success(
            new DiscordGuildInfoDto(
                guild.Value.Id,
                guild.Value.Name,
                guild.Value.Icon,
                guild.Value.Description
            )
        );
    }

    public async Task<Result<IReadOnlyList<DiscordGuildRoleDto>>> GetGuildRolesAsync(
        Guid broadcasterId,
        string guildId,
        CancellationToken ct = default
    )
    {
        Result<List<DiscordGuildRoleWire>> roles = await GetAsync<List<DiscordGuildRoleWire>>(
            broadcasterId,
            $"guilds/{Uri.EscapeDataString(guildId)}/roles",
            ct
        );
        if (roles.IsFailure)
            return Result.Failure<IReadOnlyList<DiscordGuildRoleDto>>(
                roles.ErrorMessage,
                roles.ErrorCode,
                roles.ErrorDetail
            );
        IReadOnlyList<DiscordGuildRoleDto> dtos =
        [
            .. roles.Value.Select(r => new DiscordGuildRoleDto(
                r.Id,
                r.Name,
                r.Color,
                r.Position,
                r.Managed
            )),
        ];
        return Result.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<DiscordGuildChannelDto>>> GetGuildChannelsAsync(
        Guid broadcasterId,
        string guildId,
        CancellationToken ct = default
    )
    {
        Result<List<DiscordGuildChannelWire>> channels = await GetAsync<
            List<DiscordGuildChannelWire>
        >(broadcasterId, $"guilds/{Uri.EscapeDataString(guildId)}/channels", ct);
        if (channels.IsFailure)
            return Result.Failure<IReadOnlyList<DiscordGuildChannelDto>>(
                channels.ErrorMessage,
                channels.ErrorCode,
                channels.ErrorDetail
            );
        IReadOnlyList<DiscordGuildChannelDto> dtos =
        [
            .. channels.Value.Select(c => new DiscordGuildChannelDto(
                c.Id,
                c.Name,
                c.Type,
                c.ParentId,
                c.Position
            )),
        ];
        return Result.Success(dtos);
    }

    // ─── Core HTTP ───────────────────────────────────────────────────────────

    /// <summary>Authenticated GET against the Discord REST API, deserialized to the wire shape.</summary>
    private async Task<Result<T>> GetAsync<T>(Guid broadcasterId, string path, CancellationToken ct)
    {
        Result<string> token = await ResolveBotTokenAsync(broadcasterId, ct);
        if (token.IsFailure)
            return Result.Failure<T>(token.ErrorMessage, token.ErrorCode);

        using HttpRequestMessage request = new(HttpMethod.Get, $"{DiscordApiBase}/{path}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bot {token.Value}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Discord read failed (transport).");
            return Result.Failure<T>("Discord request failed.", "DISCORD_TRANSPORT");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                Result<string> error = await MapPostErrorAsync(response, ct);
                return Result.Failure<T>(error.ErrorMessage, error.ErrorCode, error.ErrorDetail);
            }

            try
            {
                T? body = await response.Content.ReadFromJsonAsync<T>(WireJson, ct);
                return body is null
                    ? Result.Failure<T>("Discord returned an empty body.", "DISCORD_ERROR")
                    : Result.Success(body);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Malformed Discord read response.");
                return Result.Failure<T>("Malformed Discord response.", "DISCORD_TRANSPORT");
            }
        }
    }

    private async Task<Result<string>> PostMessagePayloadAsync(
        string botToken,
        string targetChannelId,
        DiscordMessagePayload payload,
        CancellationToken ct
    )
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"{DiscordApiBase}/channels/{Uri.EscapeDataString(targetChannelId)}/messages"
        )
        {
            Content = JsonContent.Create(payload, options: WireJson),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bot {botToken}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Discord post to channel failed (transport).");
            return Result.Failure<string>("Discord request failed.", "DISCORD_TRANSPORT");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return await MapPostErrorAsync(response, ct);

            try
            {
                DiscordMessageResponse? body =
                    await response.Content.ReadFromJsonAsync<DiscordMessageResponse>(WireJson, ct);
                return body is null || string.IsNullOrEmpty(body.Id)
                    ? Result.Failure<string>("Discord returned no message id.", "DISCORD_ERROR")
                    : Result.Success(body.Id);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Malformed Discord message response.");
                return Result.Failure<string>("Malformed Discord response.", "DISCORD_TRANSPORT");
            }
        }
    }

    private async Task<Result> ModifyMemberRoleAsync(
        Guid broadcasterId,
        HttpMethod method,
        string guildId,
        string discordMemberId,
        string discordRoleId,
        CancellationToken ct
    )
    {
        Result<string> token = await ResolveBotTokenAsync(broadcasterId, ct);
        if (token.IsFailure)
            return token;

        string url =
            $"{DiscordApiBase}/guilds/{Uri.EscapeDataString(guildId)}"
            + $"/members/{Uri.EscapeDataString(discordMemberId)}"
            + $"/roles/{Uri.EscapeDataString(discordRoleId)}";

        using HttpRequestMessage request = new(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bot {token.Value}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Discord member-role change failed (transport).");
            return Result.Failure("Discord request failed.", "DISCORD_TRANSPORT");
        }

        using (response)
        {
            return response.IsSuccessStatusCode
                ? Result.Success()
                : await MapPostErrorAsync(response, ct);
        }
    }

    private async Task<Result<string>> MapPostErrorAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        string code = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "DISCORD_UNAUTHORIZED",
            HttpStatusCode.NotFound => "DISCORD_NOT_FOUND",
            HttpStatusCode.TooManyRequests => "DISCORD_RATE_LIMITED",
            _ => "DISCORD_ERROR",
        };
        string? detail = await SafeReadBodyAsync(response, ct);
        _logger.LogWarning(
            "Discord request failed: {Status} ({Code})",
            (int)response.StatusCode,
            code
        );
        return Result.Failure<string>(
            $"Discord request failed ({(int)response.StatusCode}).",
            code,
            detail
        );
    }

    private static async Task<string?> SafeReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        try
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            // Discord error bodies are small JSON; cap the captured detail to avoid log bloat.
            return body.Length > 500 ? body[..500] : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the tenant's decrypted Discord bot token: finds the <c>(BroadcasterId, Provider="discord")</c>
    /// connection, then decrypts its access token through the vault. Never a cached/plaintext token; a
    /// shredded DEK or a missing connection fails closed.
    /// </summary>
    private async Task<Result<string>> ResolveBotTokenAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        IntegrationConnection? connection = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Provider == Provider
                    && c.DeletedAt == null,
                ct
            );
        if (connection is null)
            return Result.Failure<string>(
                "No connected Discord bot for this channel.",
                "DISCORD_NOT_CONNECTED"
            );

        Result<DecryptedTokenDto> token = await _vault.GetAccessTokenAsync(connection.Id, ct);
        if (token.IsFailure)
            return Result.Failure<string>(token.ErrorMessage, token.ErrorCode);

        return Result.Success(token.Value.Value);
    }

    // ─── Payload shaping ─────────────────────────────────────────────────────

    /// <summary>Prefixes the content with the role mention so the ping actually renders in the message body.</summary>
    private static string BuildContent(string content, string? pingRoleId) =>
        string.IsNullOrEmpty(pingRoleId) ? content : $"<@&{pingRoleId}> {content}";

    /// <summary>Restricts mentions to only the one ping role (never @everyone / @here / arbitrary users).</summary>
    private static DiscordAllowedMentions? BuildAllowedMentions(string? pingRoleId) =>
        string.IsNullOrEmpty(pingRoleId)
            ? new DiscordAllowedMentions([], [])
            : new DiscordAllowedMentions([], [pingRoleId]);

    private static DiscordEmbedPayload ToWireEmbed(DiscordEmbedDto embed) =>
        new(
            embed.Title,
            embed.Description,
            ParseColor(embed.Color),
            embed.ThumbnailUrl is null ? null : new DiscordEmbedMedia(embed.ThumbnailUrl),
            embed.ImageUrl is null ? null : new DiscordEmbedMedia(embed.ImageUrl),
            embed.FooterText is null ? null : new DiscordEmbedFooter(embed.FooterText),
            embed.Fields is null
                ? null
                :
                [
                    .. embed.Fields.Select(f => new DiscordEmbedFieldPayload(
                        f.Name,
                        f.Value,
                        f.Inline
                    )),
                ]
        );

    /// <summary>Parses a <c>#rrggbb</c> / <c>rrggbb</c> hex color into Discord's integer color, else null.</summary>
    private static int? ParseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;
        string hex = color.TrimStart('#');
        return int.TryParse(
            hex,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out int value
        )
            ? value
            : null;
    }

    // ─── Discord wire DTOs (System.Text.Json — Discord's own snake/camel wire) ─

    private sealed record DiscordMessagePayload(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("embeds")] IReadOnlyList<DiscordEmbedPayload>? Embeds,
        [property: JsonPropertyName("allowed_mentions")] DiscordAllowedMentions? AllowedMentions,
        [property: JsonPropertyName("components")]
            IReadOnlyList<DiscordComponent>? Components = null
    );

    private sealed record DiscordAllowedMentions(
        [property: JsonPropertyName("parse")] IReadOnlyList<string> Parse,
        [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles
    );

    private sealed record DiscordComponent(
        [property: JsonPropertyName("type")] int Type,
        [property: JsonPropertyName("components")]
            IReadOnlyList<DiscordComponent>? Components = null,
        [property: JsonPropertyName("style")] int? Style = null,
        [property: JsonPropertyName("label")] string? Label = null,
        [property: JsonPropertyName("custom_id")] string? CustomId = null
    );

    private sealed record DiscordEmbedPayload(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("color")] int? Color,
        [property: JsonPropertyName("thumbnail")] DiscordEmbedMedia? Thumbnail,
        [property: JsonPropertyName("image")] DiscordEmbedMedia? Image,
        [property: JsonPropertyName("footer")] DiscordEmbedFooter? Footer,
        [property: JsonPropertyName("fields")] IReadOnlyList<DiscordEmbedFieldPayload>? Fields
    );

    private sealed record DiscordEmbedMedia([property: JsonPropertyName("url")] string Url);

    private sealed record DiscordEmbedFooter([property: JsonPropertyName("text")] string Text);

    private sealed record DiscordEmbedFieldPayload(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("inline")] bool Inline
    );

    private sealed record DiscordMessageResponse([property: JsonPropertyName("id")] string Id);

    private sealed record DiscordGuildWire(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("icon")] string? Icon = null,
        [property: JsonPropertyName("description")] string? Description = null
    );

    private sealed record DiscordGuildRoleWire(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("color")] int Color,
        [property: JsonPropertyName("position")] int Position,
        [property: JsonPropertyName("managed")] bool Managed
    );

    private sealed record DiscordGuildChannelWire(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("type")] int Type,
        [property: JsonPropertyName("parent_id")] string? ParentId = null,
        [property: JsonPropertyName("position")] int Position = 0
    );
}
