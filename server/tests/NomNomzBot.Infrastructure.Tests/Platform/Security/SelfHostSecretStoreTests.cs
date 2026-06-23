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
using NomNomzBot.Infrastructure.Platform.Security;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

/// <summary>
/// Proves the self-host single-exe can stand up its own JWT signing secret on first run (so the operator never
/// sets one) and that the same secret is reloaded on later boots (so issued tokens survive restarts).
/// </summary>
public sealed class SelfHostSecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nnz-secret-test-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void First_run_generates_a_strong_secret_and_persists_it()
    {
        string secret = SelfHostSecretStore.LoadOrCreateJwtSecret(_dir);

        // Strong enough to clear the production guard (>= 32 chars) and is real base64 entropy, not a default.
        secret.Length.Should().BeGreaterThanOrEqualTo(32);
        Convert
            .FromBase64String(secret)
            .Length.Should()
            .BeGreaterThanOrEqualTo(32, "the secret is 48 random bytes, base64-encoded");
        File.Exists(Path.Combine(_dir, "jwt-secret.bin"))
            .Should()
            .BeTrue("the secret is persisted for later boots");
    }

    [Fact]
    public void Subsequent_runs_reload_the_same_secret()
    {
        string first = SelfHostSecretStore.LoadOrCreateJwtSecret(_dir);
        string second = SelfHostSecretStore.LoadOrCreateJwtSecret(_dir);

        second.Should().Be(first, "tokens signed before a restart must still validate after it");
    }

    [Fact]
    public void Each_fresh_install_gets_a_distinct_secret()
    {
        string a = SelfHostSecretStore.LoadOrCreateJwtSecret(_dir);
        string otherDir = _dir + "-b";
        try
        {
            string b = SelfHostSecretStore.LoadOrCreateJwtSecret(otherDir);
            b.Should().NotBe(a);
        }
        finally
        {
            if (Directory.Exists(otherDir))
                Directory.Delete(otherDir, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
