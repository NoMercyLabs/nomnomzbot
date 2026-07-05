// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Api.Identifiers;

namespace NomNomzBot.Api.Tests.Identifiers;

/// <summary>
/// The owned-id boundary codec: a UUIDv7 Guid encodes to a 26-char Crockford ULID and back losslessly, and the
/// decoder tolerates BOTH the ULID wire form and a raw Guid string (so existing clients keep working).
/// </summary>
public sealed class GuidUlidCodecTests
{
    private static readonly Guid KnownId = Guid.Parse("0192a000-0000-7000-8000-000000000b01");

    [Fact]
    public void Encode_produces_a_26_char_crockford_ulid()
    {
        string encoded = GuidUlidCodec.Encode(KnownId);

        encoded.Should().HaveLength(26);
        // Crockford base32 alphabet (no I, L, O, U); ULIDs are upper-case.
        Regex.IsMatch(encoded, "^[0-9A-HJKMNP-TV-Z]{26}$").Should().BeTrue();
        encoded.Should().NotBe(KnownId.ToString());
        encoded.Should().Be(new Ulid(KnownId).ToString());
    }

    [Fact]
    public void Encode_then_decode_round_trips_to_the_same_guid()
    {
        string encoded = GuidUlidCodec.Encode(KnownId);

        GuidUlidCodec.TryDecode(encoded, out Guid decoded).Should().BeTrue();
        decoded.Should().Be(KnownId);
    }

    [Fact]
    public void Decode_accepts_a_raw_guid_string_unchanged()
    {
        GuidUlidCodec.TryDecode(KnownId.ToString(), out Guid decoded).Should().BeTrue();
        decoded.Should().Be(KnownId);
    }

    [Fact]
    public void Decode_accepts_a_braced_and_n_format_guid()
    {
        GuidUlidCodec.TryDecode(KnownId.ToString("N"), out Guid n).Should().BeTrue();
        n.Should().Be(KnownId);
        GuidUlidCodec.TryDecode(KnownId.ToString("B"), out Guid b).Should().BeTrue();
        b.Should().Be(KnownId);
    }

    // A wire id is either a 26-char ULID or a 32/36/38-char Guid; anything of another length (and not a Guid) is
    // rejected. (The underlying ULID parser only enforces the 26-char length, so a syntactically-plausible-but-
    // nonexistent id decodes and 404s downstream, exactly as a nonexistent raw Guid does today.)
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("0192a000-0000-7000-8000")] // truncated guid — neither 26 chars nor a valid Guid
    [InlineData("0192a000-0000-7000-8000-000000000b01-extra")]
    public void Decode_rejects_null_empty_and_malformed(string? value)
    {
        GuidUlidCodec.TryDecode(value, out Guid decoded).Should().BeFalse();
        decoded.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Empty_guid_round_trips_and_is_not_a_decode_failure()
    {
        // Guid.Empty is a valid value (the controller/service decides what it means), not a boundary failure —
        // it must survive the round-trip exactly as a raw empty Guid would today.
        string encoded = GuidUlidCodec.Encode(Guid.Empty);

        GuidUlidCodec.TryDecode(encoded, out Guid fromUlid).Should().BeTrue();
        fromUlid.Should().Be(Guid.Empty);
        GuidUlidCodec.TryDecode(Guid.Empty.ToString(), out Guid fromGuid).Should().BeTrue();
        fromGuid.Should().Be(Guid.Empty);
    }
}
