// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System;
using System.Linq;
using FluentAssertions;
using Makaretu.Dns;
using NomNomzBot.Infrastructure.Platform.Deployment;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Deployment;

/// <summary>
/// Proves the mDNS advertisement is constructed with the right shape (deployment-distribution §6) WITHOUT a real
/// network: the service type is <c>_nomnomz._tcp</c>, the bound port is the actual port (so the UI finds the bot
/// regardless of which port smart resolution settled on), the instance name is carried, and the TXT records carry
/// the de-dupe instance id + scheme/path the native connection switcher reads to build a baseUrl.
/// </summary>
public sealed class MdnsServiceProfileFactoryTests
{
    private static readonly Guid InstanceId = Guid.Parse("0190a0c0-0000-7000-8000-000000000abc");

    [Fact]
    public void Advertises_the_nomnomz_tcp_service_type()
    {
        ServiceProfile profile = MdnsServiceProfileFactory.Build(InstanceId, "Stoney's Bot", 5080);

        profile.ServiceName.ToString().Should().Be("_nomnomz._tcp");
    }

    [Fact]
    public void Advertises_the_actual_bound_port()
    {
        // A non-default port (smart resolution stepped aside) must be what is advertised — the whole point of §6.
        ServiceProfile profile = MdnsServiceProfileFactory.Build(InstanceId, "Stoney's Bot", 51234);

        SRVRecord srv = profile.Resources.OfType<SRVRecord>().Single();
        srv.Port.Should().Be(51234);
    }

    [Fact]
    public void Carries_the_instance_display_name()
    {
        ServiceProfile profile = MdnsServiceProfileFactory.Build(InstanceId, "Stoney's Bot", 5080);

        // DomainName.ToString() DNS-escapes spaces (e.g. "\032"); the unescaped label is the real instance name.
        profile.InstanceName.Labels.Should().ContainSingle().Which.Should().Be("Stoney's Bot");
    }

    [Fact]
    public void Blank_display_name_falls_back_to_the_machine_name()
    {
        ServiceProfile profile = MdnsServiceProfileFactory.Build(InstanceId, "   ", 5080);

        profile
            .InstanceName.Labels.Should()
            .ContainSingle()
            .Which.Should()
            .Be(Environment.MachineName);
    }

    [Fact]
    public void Txt_records_carry_the_instance_id_scheme_and_path_for_the_native_switcher()
    {
        ServiceProfile profile = MdnsServiceProfileFactory.Build(InstanceId, "Stoney's Bot", 5080);

        string[] txt = profile.Resources.OfType<TXTRecord>().SelectMany(r => r.Strings).ToArray();

        txt.Should().Contain($"instance={InstanceId}");
        txt.Should().Contain("scheme=http");
        txt.Should().Contain("path=/");
        txt.Should().Contain(s => s.StartsWith("version=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Rejects_an_out_of_range_port(int port)
    {
        Action act = () => MdnsServiceProfileFactory.Build(InstanceId, "Bot", port);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
