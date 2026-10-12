// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

public class OsSecureStoreKeyVaultTests
{
    private const string ConfigKey = "dGVzdC1lbmNyeXB0aW9uLWtleS0zMi1ieXRlcy1sb25nIQ==";

    private readonly byte[] _osKek = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public async Task A_dek_wrapped_by_the_os_store_still_unwraps_after_a_config_key_is_set()
    {
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        WrappedKey wrapped = (await Vault(configKey: null).WrapAsync(dek)).Value;

        Result<byte[]> unwrapped = await Vault(ConfigKey)
            .UnwrapAsync(wrapped.WrappedKeyMaterial, wrapped.KekReference);

        wrapped.KekReference.Should().Be("os-secure-store:dpapi");
        unwrapped.IsSuccess.Should().BeTrue(unwrapped.ErrorMessage);
        unwrapped.Value.Should().Equal(dek);
    }

    [Fact]
    public async Task A_new_dek_is_wrapped_under_the_config_key_when_one_is_set()
    {
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        WrappedKey wrapped = (await Vault(ConfigKey).WrapAsync(dek)).Value;

        Result<byte[]> unwrapped = await Vault(ConfigKey)
            .UnwrapAsync(wrapped.WrappedKeyMaterial, wrapped.KekReference);

        wrapped.KekReference.Should().Be("config:hkdf");
        unwrapped.Value.Should().Equal(dek);
    }

    [Fact]
    public async Task A_dek_wrapped_under_a_config_key_fails_cleanly_when_the_key_is_gone()
    {
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        WrappedKey wrapped = (await Vault(ConfigKey).WrapAsync(dek)).Value;

        Result<byte[]> unwrapped = await Vault(configKey: null)
            .UnwrapAsync(wrapped.WrappedKeyMaterial, wrapped.KekReference);

        unwrapped.IsFailure.Should().BeTrue();
        unwrapped.ErrorCode.Should().Be("UNWRAP_FAILED");
    }

    private OsSecureStoreKeyVault Vault(string? configKey) =>
        new(
            new EncryptionOptions { Key = configKey ?? string.Empty },
            () => _osKek,
            NullLogger.Instance
        );
}
