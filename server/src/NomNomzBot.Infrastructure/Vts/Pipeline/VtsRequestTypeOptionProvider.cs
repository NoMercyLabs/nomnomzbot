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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Vts.Pipeline;

/// <summary>
/// Supplies the request names of the VTube Studio API for the <c>vts_request_type</c> picker kind. The names are fixed
/// by the VTube Studio API, so they are a constant list, read from the API README
/// (https://github.com/DenchiSoft/VTubeStudio, checked 2026-11-03). Every request the README documents is listed,
/// whether or not the bot has a dedicated action for it. The Events page (<c>EventSubscriptionRequest</c>) is not
/// part of the README and is not listed.
/// </summary>
internal sealed class VtsRequestTypeOptionProvider : IPipelineOptionProvider
{
    private static readonly string[] RequestTypes =
    [
        "APIStateRequest",
        "AuthenticationTokenRequest",
        "AuthenticationRequest",
        "StatisticsRequest",
        "VTSFolderInfoRequest",
        "CurrentModelRequest",
        "AvailableModelsRequest",
        "ModelLoadRequest",
        "MoveModelRequest",
        "HotkeysInCurrentModelRequest",
        "HotkeyTriggerRequest",
        "ExpressionStateRequest",
        "ExpressionActivationRequest",
        "ArtMeshListRequest",
        "ArtMeshAtPositionRequest",
        "ColorTintRequest",
        "SceneColorOverlayInfoRequest",
        "FaceFoundRequest",
        "InputParameterListRequest",
        "ParameterValueRequest",
        "Live2DParameterListRequest",
        "ParameterCreationRequest",
        "ParameterDeletionRequest",
        "InjectParameterDataRequest",
        "GetCurrentModelPhysicsRequest",
        "SetCurrentModelPhysicsRequest",
        "NDIConfigRequest",
        "ItemListRequest",
        "ItemLoadRequest",
        "ItemUnloadRequest",
        "ItemAnimationControlRequest",
        "ItemMoveRequest",
        "ItemSortRequest",
        "ArtMeshSelectionRequest",
        "ItemPinRequest",
        "PostProcessingListRequest",
        "PostProcessingUpdateRequest",
    ];

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.VtsRequestType;

    public Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            PipelineOptionPage.Of(
                RequestTypes.Select(t => PipelineOptionPage.Option(t, t)),
                request,
                inSourceOrder: true
            )
        );
}
