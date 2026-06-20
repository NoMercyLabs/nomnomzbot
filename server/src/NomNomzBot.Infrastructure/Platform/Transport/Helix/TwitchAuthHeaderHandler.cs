// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// Injects the two headers every Helix call requires (twitch-helix.md §7): the <c>Client-Id</c> (the app's
/// Twitch client id) and the <c>Authorization: Bearer</c> token the transport resolved for this call and
/// stowed on <see cref="HttpRequestMessage.Options"/>. The bearer is a Twitch access token resolved from a
/// tenant <see cref="Guid"/> upstream — never a Guid — so this handler attaches it verbatim and never logs it.
/// </summary>
public sealed class TwitchAuthHeaderHandler(IOptions<TwitchOptions> options) : DelegatingHandler
{
    private readonly TwitchOptions _options = options.Value;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrEmpty(_options.ClientId))
        {
            request.Headers.Remove("Client-Id");
            request.Headers.Add("Client-Id", _options.ClientId);
        }

        if (request.Options.TryGetValue(HelixRequestOptions.AccessToken, out string? token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
