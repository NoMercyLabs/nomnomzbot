// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PlatformContent.Entities;
using NomNomzBot.Infrastructure.Content.PlatformContent;
using NomNomzBot.Infrastructure.Content.PlatformContent.Templates;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Content.PlatformContent;

/// <summary>
/// Shared wiring for the platform-template kind tests: the real admin <see cref="PlatformContentService"/>
/// (author → publish) and the real <see cref="PlatformTemplateCatalogService"/> (channel install) over one
/// relational SQLite database. IAM and Gate-2 are substitutes a test flips to prove the gates.
/// </summary>
internal sealed class PlatformTemplateHarness : IAsyncDisposable
{
    public PlatformContentTestDbContext Db { get; } = PlatformContentTestDbContext.New();
    public IPlatformIamService Iam { get; } = Substitute.For<IPlatformIamService>();
    public IActionAuthorizationService Authorization { get; } =
        Substitute.For<IActionAuthorizationService>();
    public Guid ActingPrincipalId { get; } = Guid.NewGuid();
    public Guid CallerUserId { get; } = Guid.NewGuid();

    public PlatformTemplateHarness()
    {
        AllowPlatform(true);
        AllowChannelAction(true);
    }

    public void AllowPlatform(bool allowed) =>
        Iam.AuthorizePlatformAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Success(allowed));

    public void AllowChannelAction(bool allowed) =>
        Authorization
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(allowed));

    public PlatformContentService AdminService(IPlatformTemplateInstaller installer) =>
        new(
            Db,
            Iam,
            new TestUnitOfWork(Db),
            Substitute.For<IVueSfcCompiler>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<Application.Commands.Services.IPipelineService>(),
            Substitute.For<IScriptExecutor>(),
            [installer]
        );

    public PlatformTemplateCatalogService Catalog(IPlatformTemplateInstaller installer) =>
        new(Db, new TestUnitOfWork(Db), Authorization, [installer]);

    /// <summary>Authors a definition and publishes v1 through the real admin flow; returns the definition id.</summary>
    public async Task<Guid> PublishTemplateAsync(
        IPlatformTemplateInstaller installer,
        string key,
        string payloadJson
    )
    {
        PlatformContentService admin = AdminService(installer);
        Result<PlatformContentDefinitionDto> created = await admin.CreateDefinitionAsync(
            ActingPrincipalId,
            new(installer.Kind, key, key, null, payloadJson)
        );
        if (created.IsFailure)
            throw new InvalidOperationException(created.ErrorMessage);

        Result<PlatformContentPublishJobDto> published = await admin.PublishAsync(
            ActingPrincipalId,
            created.Value.Id,
            created.Value.LatestDraftVersionId!.Value,
            new(PlatformContentPublishModes.PublishAsNew, null, 0)
        );
        if (published.IsFailure)
            throw new InvalidOperationException(published.ErrorMessage);

        return created.Value.Id;
    }

    public async Task<Channel> AddChannelAsync(string name)
    {
        Channel channel = new()
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            NameNormalized = name.ToLowerInvariant(),
        };
        Db.Channels.Add(channel);
        await Db.SaveChangesAsync();
        return channel;
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
