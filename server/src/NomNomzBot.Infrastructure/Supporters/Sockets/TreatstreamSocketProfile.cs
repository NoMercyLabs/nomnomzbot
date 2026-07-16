// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Supporters.Sockets;

/// <summary>
/// TreatStream's Socket.IO dialect (supporter-events.md §0 D3, verified against treatstream.com/api/details):
/// the runner hands this the VAULTED OAUTH ACCESS TOKEN (the connection links the OAuth connection instead
/// of carrying a secret), which <see cref="ResolveEndpointAsync"/> exchanges at
/// <c>POST /Oauth2/Authorize/socketToken</c> (client_id + access_token → <c>socket_token</c>) before
/// connecting <c>nodeapi.treatstream.com?token=…</c>. Treats arrive on <c>realTimeTreat</c>; every frame is
/// an ingestable treat (the event name itself is the filter).
/// </summary>
internal sealed class TreatstreamSocketProfile : ISocketIoProfile
{
    private const string SocketTokenUrl = "https://treatstream.com/Oauth2/Authorize/socketToken";
    private const string SocketBase = "https://nodeapi.treatstream.com/";

    private readonly ISystemCredentialsProvider _credentials;

    public TreatstreamSocketProfile(ISystemCredentialsProvider credentials) =>
        _credentials = credentials;

    public string SourceKey => "treatstream";

    public int EngineIoVersion => 3; // TreatStream's node API speaks the Socket.IO v2-era protocol

    public IReadOnlyList<string> EventNames { get; } = ["realTimeTreat"];

    /// <summary>
    /// Never usable directly — the socket endpoint requires the socket-token exchange in
    /// <see cref="ResolveEndpointAsync"/>. Only the Socket.IO transport connects this profile and it always
    /// resolves; the raw-WS transport never sees an <see cref="ISocketIoProfile"/>.
    /// </summary>
    public Uri BuildUri(string secret) => new(SocketBase);

    public async ValueTask<Uri> ResolveEndpointAsync(
        string secret,
        HttpClient http,
        CancellationToken ct
    )
    {
        string? clientId = await _credentials.GetClientIdAsync(
            AuthEnums.IntegrationProvider.Treatstream,
            ct
        );
        if (string.IsNullOrWhiteSpace(clientId))
            throw new HttpRequestException("TreatStream app credentials are not configured.");

        using FormUrlEncodedContent content = new(
            new Dictionary<string, string> { ["client_id"] = clientId, ["access_token"] = secret }
        );
        using HttpResponseMessage response = await http.PostAsync(SocketTokenUrl, content, ct);
        response.EnsureSuccessStatusCode();

        JObject parsed = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
        string? socketToken = parsed.Value<string>("socket_token");
        if (string.IsNullOrWhiteSpace(socketToken))
            throw new HttpRequestException("TreatStream returned no socket_token.");

        return new Uri($"{SocketBase}?token={Uri.EscapeDataString(socketToken)}");
    }

    public SocketIoEmit? BuildConnectEmit(string secret) => null;

    public IReadOnlyList<string> TranslateFrame(string frame)
    {
        JToken parsed;
        try
        {
            parsed = JToken.Parse(frame);
        }
        catch (JsonException)
        {
            return [];
        }

        // The frame is the Socket.IO args array — the treat object is its first element; the event name
        // (realTimeTreat) already filtered, so any object here IS a treat.
        JObject? treat = parsed switch
        {
            JArray { Count: > 0 } args => args[0] as JObject,
            JObject direct => direct,
            _ => null,
        };
        return treat is null ? [] : [treat.ToString(Formatting.None)];
    }
}
