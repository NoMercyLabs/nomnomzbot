// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Ipc;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Services.Ipc;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Ipc;

/// <summary>
/// Behavior of the IPC dev-mode key registry (stream-admin.md §3.3): the plaintext leaves the
/// service exactly once, storage is hash-only, revocation tombstones, and connection auth fails
/// closed for wrong/revoked/expired keys and on the SaaS profile.
/// </summary>
public sealed class IpcDevModeServiceTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();

    private static IpcDevModeService NewService(
        AuthDbContext db,
        DeploymentMode mode,
        FakeTimeProvider clock
    )
    {
        IDeploymentProfileService profile = Substitute.For<IDeploymentProfileService>();
        profile.Current.Returns(
            new DeploymentProfileSnapshot(
                Guid.NewGuid(),
                mode,
                false,
                default,
                default,
                default,
                default,
                default,
                default,
                false,
                default
            )
        );
        return new IpcDevModeService(db, profile, clock);
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public async Task Create_returns_the_plaintext_once_and_stores_only_the_hash()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new();
        IpcDevModeService service = NewService(db, DeploymentMode.SelfHostLite, clock);

        Result<IpcDevModeKeyDto> created = await service.CreateKeyAsync(
            Actor,
            new CreateIpcKeyRequest("dev laptop", ExpiresAt: null)
        );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        string plaintext = created.Value!.PlaintextKey!;
        plaintext.Should().StartWith("nnzb_ipc_", "the marker makes a leaked key recognizable");
        created.Value.Label.Should().Be("dev laptop");
        created.Value.IsEnabled.Should().BeTrue();

        IpcDevModeKey stored = db.IpcDevModeKeys.Single();
        stored.KeyHash.Should().NotBe(plaintext, "the plaintext must never be persisted");
        stored.KeyHash.Should().Be(Sha256Hex(plaintext), "storage is the SHA-256 lowercase hex");
        stored.KeyHash.Should().HaveLength(64);
        stored.CreatedByUserId.Should().Be(Actor);

        Result<IReadOnlyList<IpcDevModeKeyDto>> listed = await service.ListKeysAsync();
        listed.IsSuccess.Should().BeTrue();
        listed.Value!.Single().Id.Should().Be(created.Value.Id);
        listed
            .Value.Single()
            .PlaintextKey.Should()
            .BeNull("the plaintext is retrievable exactly once, in the create response");
    }

    [Fact]
    public void The_dto_shape_carries_no_key_material_field()
    {
        // Shape guard: the DTO exposes metadata + the one-shot plaintext slot — no hash property
        // exists to leak through list/get serialization.
        IReadOnlyList<string> propertyNames =
        [
            .. typeof(IpcDevModeKeyDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name),
        ];
        propertyNames
            .Should()
            .BeEquivalentTo(["Id", "Label", "IsEnabled", "ExpiresAt", "CreatedAt", "PlaintextKey"]);
    }

    [Fact]
    public async Task Authenticate_accepts_the_minted_key_and_rejects_a_wrong_one()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new();
        IpcDevModeService service = NewService(db, DeploymentMode.SelfHostLite, clock);
        Result<IpcDevModeKeyDto> created = await service.CreateKeyAsync(
            Actor,
            new CreateIpcKeyRequest(null, null)
        );

        Result accepted = await service.AuthenticateConnectionAsync(created.Value!.PlaintextKey!);
        accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);

        Result rejected = await service.AuthenticateConnectionAsync(
            "nnzb_ipc_" + new string('0', 64)
        );
        rejected.IsFailure.Should().BeTrue();
        rejected.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Revoke_tombstones_the_row_and_the_key_stops_authenticating()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new();
        IpcDevModeService service = NewService(db, DeploymentMode.SelfHostLite, clock);
        Result<IpcDevModeKeyDto> created = await service.CreateKeyAsync(
            Actor,
            new CreateIpcKeyRequest("to revoke", null)
        );

        Result revoked = await service.RevokeKeyAsync(created.Value!.Id);
        revoked.IsSuccess.Should().BeTrue(revoked.ErrorMessage);

        // Soft delete: the row survives as a tombstone, disabled and stamped — never hard-deleted.
        IpcDevModeKey tombstone = db.IpcDevModeKeys.Single();
        tombstone.DeletedAt.Should().NotBeNull();
        tombstone.IsEnabled.Should().BeFalse();

        Result<IReadOnlyList<IpcDevModeKeyDto>> listed = await service.ListKeysAsync();
        listed.Value.Should().BeEmpty("a revoked key leaves the registry surface");

        Result rejected = await service.AuthenticateConnectionAsync(created.Value.PlaintextKey!);
        rejected.IsFailure.Should().BeTrue();
        rejected.ErrorCode.Should().Be("FORBIDDEN");

        Result again = await service.RevokeKeyAsync(created.Value.Id);
        again.IsFailure.Should().BeTrue("a tombstoned key is gone from the registry surface");
        again.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Revoke_of_an_unknown_key_is_not_found()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IpcDevModeService service = NewService(
            db,
            DeploymentMode.SelfHostLite,
            new FakeTimeProvider()
        );

        Result result = await service.RevokeKeyAsync(Guid.CreateVersion7());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task An_expired_key_neither_enables_dev_mode_nor_authenticates()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new();
        IpcDevModeService service = NewService(db, DeploymentMode.SelfHostLite, clock);
        DateTime expiry = clock.GetUtcNow().UtcDateTime.AddHours(1);
        Result<IpcDevModeKeyDto> created = await service.CreateKeyAsync(
            Actor,
            new CreateIpcKeyRequest("short-lived", expiry)
        );

        (await service.IsEnabledAsync()).Value.Should().BeTrue("the key is still live");
        (await service.AuthenticateConnectionAsync(created.Value!.PlaintextKey!))
            .IsSuccess.Should()
            .BeTrue();

        clock.Advance(TimeSpan.FromHours(2));

        (await service.IsEnabledAsync()).Value.Should().BeFalse("the only key expired");
        Result rejected = await service.AuthenticateConnectionAsync(created.Value.PlaintextKey!);
        rejected.IsFailure.Should().BeTrue();
        rejected.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Create_rejects_an_expiry_in_the_past()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new();
        IpcDevModeService service = NewService(db, DeploymentMode.SelfHostLite, clock);

        Result<IpcDevModeKeyDto> result = await service.CreateKeyAsync(
            Actor,
            new CreateIpcKeyRequest(null, clock.GetUtcNow().UtcDateTime.AddMinutes(-1))
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task The_saas_profile_refuses_everything_even_with_a_valid_key()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        FakeTimeProvider clock = new();
        IpcDevModeService selfHost = NewService(db, DeploymentMode.SelfHostFull, clock);
        Result<IpcDevModeKeyDto> created = await selfHost.CreateKeyAsync(
            Actor,
            new CreateIpcKeyRequest(null, null)
        );

        IpcDevModeService saas = NewService(db, DeploymentMode.Saas, clock);

        (await saas.IsEnabledAsync()).Value.Should().BeFalse("dev mode is never on for SaaS");

        Result rejected = await saas.AuthenticateConnectionAsync(created.Value!.PlaintextKey!);
        rejected.IsFailure.Should().BeTrue("a valid key must not authenticate on SaaS");
        rejected.ErrorCode.Should().Be("FORBIDDEN");

        Result<IpcDevModeKeyDto> refusedCreate = await saas.CreateKeyAsync(
            Actor,
            new CreateIpcKeyRequest(null, null)
        );
        refusedCreate.IsFailure.Should().BeTrue();
        refusedCreate.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }
}
