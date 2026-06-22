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
using FluentAssertions;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the production boot gate: a default/short JWT secret or the bundled encryption key must abort
/// startup outside development, while strong secrets (and any value in development) boot cleanly.
/// </summary>
public sealed class StartupSecretGuardTests
{
    private const string DevJwt = "dev-secret-key-at-least-32-characters-long!!";
    private const string DevEncKey = "ZGV2LWVuY3J5cHRpb24ta2V5LWZvci1sb2NhbC1kZXY=";
    private const string StrongJwt = "Zr7Q2mK9pL4xV8nB3wF6tH1sJ0cD5eA7gN2yU4iO9kP=";

    [Fact]
    public void Throws_in_production_on_the_default_jwt_secret()
    {
        Action act = () => StartupSecretGuard.Validate(DevJwt, "a-real-encryption-key", false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Secret*");
    }

    [Fact]
    public void Throws_in_production_on_a_too_short_jwt_secret()
    {
        Action act = () => StartupSecretGuard.Validate("short", "a-real-encryption-key", false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*32 chars*");
    }

    [Fact]
    public void Throws_in_production_on_the_bundled_encryption_key()
    {
        Action act = () => StartupSecretGuard.Validate(StrongJwt, DevEncKey, false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Encryption:Key*");
    }

    [Fact]
    public void Passes_in_production_with_strong_secrets()
    {
        Action act = () => StartupSecretGuard.Validate(StrongJwt, "a-real-encryption-key", false);

        act.Should().NotThrow();
    }

    [Fact]
    public void Passes_in_production_when_encryption_key_is_absent_os_kek_custody()
    {
        // Windows/prod relies on DPAPI KEK custody; Encryption:Key is then null and must not block boot.
        Action act = () => StartupSecretGuard.Validate(StrongJwt, null, false);

        act.Should().NotThrow();
    }

    [Fact]
    public void Passes_in_development_even_with_defaults()
    {
        Action act = () => StartupSecretGuard.Validate(DevJwt, DevEncKey, true);

        act.Should().NotThrow();
    }
}
