// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

public sealed class DataProtectionRegistrationTests : IDisposable
{
    private readonly string _keysDirectory = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_dp_keys_{Guid.NewGuid():N}"
    );

    public void Dispose()
    {
        if (Directory.Exists(_keysDirectory))
            Directory.Delete(_keysDirectory, recursive: true);
    }

    private IDataProtector NewColour()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddSharedDataProtection(_keysDirectory)
            .BuildServiceProvider();
        return provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test");
    }

    [Fact]
    public void A_payload_protected_by_one_colour_is_readable_by_the_other_on_the_same_data_directory()
    {
        // Blue and green are two processes; the only thing they share is the data volume.
        string sealedByBlue = NewColour().Protect("session-state");

        string readByGreen = NewColour().Unprotect(sealedByBlue);

        readByGreen.Should().Be("session-state");
        Directory
            .GetFiles(_keysDirectory, "key-*.xml")
            .Should()
            .ContainSingle("both colours use the one key ring on the shared volume");
    }
}
