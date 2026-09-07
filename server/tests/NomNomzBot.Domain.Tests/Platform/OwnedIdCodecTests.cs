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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Tests.Platform;

/// <summary>
/// The Domain-layer owned-id codec: a UUIDv7 Guid encodes to a 26-char Crockford ULID and back losslessly,
/// and the decoder tolerates BOTH the ULID wire form and a raw Guid string. Lives in Domain (rather than only
/// at the Api boundary) so Infrastructure-layer pipeline actions and the save-time validator can decode a
/// ConfigJson resource-id field without an inward-only layering violation — see the run_code bug this closes:
/// a pipeline step's code_script_id stored as the dashboard's ULID wire form instead of a raw Guid, so
/// RunCodeAction's Guid.TryParse always failed the step (silently, with no error surfaced anywhere).
/// </summary>
public sealed class OwnedIdCodecTests
{
    private static readonly Guid KnownId = Guid.Parse("0192a000-0000-7000-8000-000000000b01");

    [Fact]
    public void Encode_produces_a_26_char_crockford_ulid()
    {
        string encoded = OwnedIdCodec.Encode(KnownId);

        encoded.Should().HaveLength(26);
        Regex.IsMatch(encoded, "^[0-9A-HJKMNP-TV-Z]{26}$").Should().BeTrue();
        encoded.Should().NotBe(KnownId.ToString());
    }

    [Fact]
    public void Encode_then_decode_round_trips_to_the_same_guid()
    {
        string encoded = OwnedIdCodec.Encode(KnownId);

        OwnedIdCodec.TryDecode(encoded, out Guid decoded).Should().BeTrue();
        decoded.Should().Be(KnownId);
    }

    [Fact]
    public void Decode_accepts_a_raw_guid_string_unchanged()
    {
        OwnedIdCodec.TryDecode(KnownId.ToString(), out Guid decoded).Should().BeTrue();
        decoded.Should().Be(KnownId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("0192a000-0000-7000-8000")]
    public void Decode_rejects_null_empty_and_malformed(string? value)
    {
        OwnedIdCodec.TryDecode(value, out Guid decoded).Should().BeFalse();
        decoded.Should().Be(Guid.Empty);
    }
}
