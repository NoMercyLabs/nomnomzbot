// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Identifiers;

namespace NomNomzBot.Api.Tests.Identifiers;

/// <summary>
/// SignalR parity: a Guid carried in a hub invocation argument serializes to a ULID string on the hub wire, exactly
/// as REST does — proven through the real <see cref="JsonHubProtocol"/> configured the way Program.cs's
/// <c>AddJsonProtocol</c> does (the ULID converter on <c>PayloadSerializerOptions</c>). Today's hub DTOs carry ids
/// as strings, so this guards any future Guid-typed hub field against diverging from the REST contract.
/// </summary>
public sealed class UlidGuidHubProtocolTests
{
    private static readonly Guid KnownId = Guid.Parse("0192a000-0000-7000-8000-000000000f01");

    private sealed record HubIdPayload(Guid BroadcasterId, string Domain);

    [Fact]
    public void Hub_payload_encodes_a_guid_field_as_a_ulid()
    {
        JsonHubProtocolOptions hubOptions = new();
        hubOptions.PayloadSerializerOptions.Converters.Add(new UlidGuidJsonConverter());
        JsonHubProtocol protocol = new(Options.Create(hubOptions));

        InvocationMessage message = new(
            "ConfigChanged",
            new object?[] { new HubIdPayload(KnownId, "timers") }
        );
        string json = Encoding.UTF8.GetString(protocol.GetMessageBytes(message).ToArray());

        json.Should().Contain(GuidUlidCodec.Encode(KnownId));
        json.Should().NotContain(KnownId.ToString());
    }
}
