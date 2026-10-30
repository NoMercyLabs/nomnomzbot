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

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The pipeline editor reads the action catalogue from <c>GET pipelines/actions</c>, and a script reads the SDK
/// typings the emitter writes; both come from the registered <see cref="ICommandAction"/> set. This holds the
/// REAL registered set (the same assembly scan the app boots with): <c>send_whisper</c> must be in it, with its
/// two required, templated text fields and their help text, and the script typings must type its params.
/// </summary>
public sealed class PipelinesControllerCatalogueSendWhisperTests
{
    private static List<ICommandAction> RegisteredActions()
    {
        ServiceCollection services = new();
        services.AddImplementationsOf<ICommandAction>(
            typeof(SdkTypeEmitter).Assembly,
            ServiceLifetime.Transient,
            typeof(CapturingCommandAction)
        );
        return
        [
            .. services.Select(d =>
                (ICommandAction)RuntimeHelpers.GetUninitializedObject(d.ImplementationType!)
            ),
        ];
    }

    [Fact]
    public void The_catalogue_lists_send_whisper_with_a_recipient_and_a_message_field()
    {
        PipelinesController controller = new(
            Substitute.For<IPipelineService>(),
            Substitute.For<IPipelineTestRunService>(),
            Substitute.For<ICommandConfigValidator>(),
            RegisteredActions(),
            [],
            Substitute.For<IPlatformDefaultRestoreService>()
        );

        PipelineCatalogueDto data = ((OkObjectResult)controller.ListActionCatalogue("chan"))
            .Value.Should()
            .BeOfType<StatusResponseDto<PipelineCatalogueDto>>()
            .Subject.Data!;

        PipelineActionDescriptorDto whisper = data.Actions.Single(a => a.Type == "send_whisper");
        whisper.Category.Key.Should().Be("pipeline.category.chat");
        whisper.Description.Key.Should().Be("pipeline.send_whisper.description");
        whisper.Fields.Select(f => f.Name).Should().Equal("recipient", "message");
        foreach (PipelineActionFieldDto field in whisper.Fields)
        {
            field.Kind.Should().Be("text");
            field.Required.Should().BeTrue();
            field.Templated.Should().BeTrue();
            field.Description!.Key.Should().Be($"pipeline.send_whisper.{field.Name}.help");
        }
    }

    [Fact]
    public void The_script_typings_type_the_send_whisper_params_with_both_fields_required()
    {
        string declaration = new SdkTypeEmitter(
            new EventCatalog(),
            null,
            RegisteredActions()
        ).EmitTypeScript(SdkContext.Script);

        int start = declaration.IndexOf("  'send_whisper': {", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "NnzActionParams must have a member for 'send_whisper'");
        string member = declaration[
            start..declaration.IndexOf("  };", start, StringComparison.Ordinal)
        ];

        member.Should().Contain("recipient: string;");
        member.Should().Contain("message: string;");
    }
}
