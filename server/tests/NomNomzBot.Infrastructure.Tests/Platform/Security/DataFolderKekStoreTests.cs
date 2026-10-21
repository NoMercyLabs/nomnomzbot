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

public sealed class DataFolderKekStoreTests : IDisposable
{
    private const string ConfigKey = "dGVzdC1lbmNyeXB0aW9uLWtleS0zMi1ieXRlcy1sb25nIQ==";

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nnz-kek-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void The_first_load_saves_a_32_byte_key_and_a_restart_reads_the_same_one()
    {
        byte[] first = DataFolderKekStore.LoadOrCreate(_dir, NullLogger.Instance);
        byte[] second = DataFolderKekStore.LoadOrCreate(_dir, NullLogger.Instance);

        string file = Path.Combine(_dir, DataFolderKekStore.FileName);
        first.Should().HaveCount(32);
        File.ReadAllBytes(file).Should().Equal(first);
        second.Should().Equal(first);
    }

    [Fact]
    public void The_key_file_is_readable_and_writable_by_the_owner_only_on_unix()
    {
        if (OperatingSystem.IsWindows())
            return;

        DataFolderKekStore.LoadOrCreate(_dir, NullLogger.Instance);

        File.GetUnixFileMode(Path.Combine(_dir, DataFolderKekStore.FileName))
            .Should()
            .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void A_key_file_of_the_wrong_size_is_refused_not_replaced()
    {
        Directory.CreateDirectory(_dir);
        string file = Path.Combine(_dir, DataFolderKekStore.FileName);
        File.WriteAllBytes(file, new byte[5]);

        Action load = () => DataFolderKekStore.LoadOrCreate(_dir, NullLogger.Instance);

        load.Should().Throw<InvalidOperationException>();
        File.ReadAllBytes(file).Should().HaveCount(5);
    }

    [Fact]
    public void A_host_with_no_os_store_and_no_config_key_falls_back_to_the_key_file()
    {
        byte[] kek = OsSecureStoreKeyVault.LoadLocalKek(
            _dir,
            isWindows: false,
            NullLogger.Instance
        );

        kek.Should().Equal(File.ReadAllBytes(Path.Combine(_dir, DataFolderKekStore.FileName)));
    }

    [Fact]
    public async Task A_dek_wrapped_before_a_restart_unwraps_after_it()
    {
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        WrappedKey wrapped = (await Vault(configKey: null).WrapAsync(dek)).Value;

        Result<byte[]> unwrapped = await Vault(configKey: null)
            .UnwrapAsync(wrapped.WrappedKeyMaterial, wrapped.KekReference);

        unwrapped.IsSuccess.Should().BeTrue(unwrapped.ErrorMessage);
        unwrapped.Value.Should().Equal(dek);
    }

    [Fact]
    public async Task A_configured_key_wins_and_no_key_file_is_created()
    {
        byte[] dek = RandomNumberGenerator.GetBytes(32);

        WrappedKey wrapped = (await Vault(ConfigKey).WrapAsync(dek)).Value;

        wrapped.KekReference.Should().Be("config:hkdf");
        File.Exists(Path.Combine(_dir, DataFolderKekStore.FileName)).Should().BeFalse();
    }

    [Fact]
    public async Task A_dek_wrapped_by_the_key_file_still_unwraps_after_a_config_key_is_set()
    {
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        WrappedKey wrapped = (await Vault(configKey: null).WrapAsync(dek)).Value;

        Result<byte[]> unwrapped = await Vault(ConfigKey)
            .UnwrapAsync(wrapped.WrappedKeyMaterial, wrapped.KekReference);

        unwrapped.IsSuccess.Should().BeTrue(unwrapped.ErrorMessage);
        unwrapped.Value.Should().Equal(dek);
    }

    private OsSecureStoreKeyVault Vault(string? configKey) =>
        new(
            new EncryptionOptions { Key = configKey ?? string.Empty },
            () => DataFolderKekStore.LoadOrCreate(_dir, NullLogger.Instance),
            NullLogger.Instance
        );
}
