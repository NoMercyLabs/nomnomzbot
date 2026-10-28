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
