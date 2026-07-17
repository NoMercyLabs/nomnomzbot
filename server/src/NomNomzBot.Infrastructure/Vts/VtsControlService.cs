// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;

namespace NomNomzBot.Infrastructure.Vts;

/// <summary>
/// Typed VTS ops (vtube-studio.md §3) — each builds the exact VTS API request payload and rides
/// <see cref="IVtsTransport"/>. The inventory read aggregates the three picker requests (models,
/// current-model hotkeys, expression states).
/// </summary>
public class VtsControlService : IVtsControlService
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IVtsTransport _transport;

    public VtsControlService(IVtsTransport transport)
    {
        _transport = transport;
    }

    public async Task<Result<VtsRequestResult>> SendAsync(
        Guid broadcasterId,
        string requestType,
        string? payloadJson,
        CancellationToken ct = default
    )
    {
        Result<string> response = await _transport.RequestAsync(
            broadcasterId,
            requestType,
            payloadJson,
            ct
        );
        if (response.IsFailure && response.ErrorCode == "VTS_ERROR")
            return Result.Success(new VtsRequestResult(false, null, response.ErrorMessage));
        if (response.IsFailure)
            return Result.Failure<VtsRequestResult>(response.ErrorMessage!, response.ErrorCode!);
        return Result.Success(new VtsRequestResult(true, response.Value, null));
    }

    public Task<Result> LoadModelAsync(
        Guid broadcasterId,
        string modelId,
        CancellationToken ct = default
    ) =>
        StatusAsync(
            broadcasterId,
            "ModelLoadRequest",
            JsonSerializer.Serialize(new { modelID = modelId }, WireJson),
            ct
        );

    public Task<Result> TriggerHotkeyAsync(
        Guid broadcasterId,
        string hotkeyId,
        CancellationToken ct = default
    ) =>
        StatusAsync(
            broadcasterId,
            "HotkeyTriggerRequest",
            JsonSerializer.Serialize(new { hotkeyID = hotkeyId }, WireJson),
            ct
        );

    public Task<Result> SetExpressionAsync(
        Guid broadcasterId,
        string expressionFile,
        bool active,
        CancellationToken ct = default
    ) =>
        StatusAsync(
            broadcasterId,
            "ExpressionActivationRequest",
            JsonSerializer.Serialize(new { expressionFile, active }, WireJson),
            ct
        );

    public Task<Result> MoveModelAsync(
        Guid broadcasterId,
        VtsMove move,
        CancellationToken ct = default
    ) =>
        StatusAsync(
            broadcasterId,
            "MoveModelRequest",
            JsonSerializer.Serialize(
                new
                {
                    timeInSeconds = move.TimeSeconds,
                    valuesAreRelativeToModel = move.Relative,
                    positionX = move.X,
                    positionY = move.Y,
                    rotation = move.Rotation,
                    size = move.Size,
                },
                WireJson
            ),
            ct
        );

    public Task<Result> ColorTintAsync(
        Guid broadcasterId,
        VtsColorTint tint,
        CancellationToken ct = default
    )
    {
        object matcher = tint.MatchArtMeshTag is null
            ? new { tintAll = true }
            : new { tintAll = false, nameContains = new[] { tint.MatchArtMeshTag } };
        return StatusAsync(
            broadcasterId,
            "ColorTintRequest",
            JsonSerializer.Serialize(
                new
                {
                    colorTint = new
                    {
                        colorR = tint.R,
                        colorG = tint.G,
                        colorB = tint.B,
                        colorA = tint.A,
                    },
                    artMeshMatcher = matcher,
                },
                WireJson
            ),
            ct
        );
    }

    public async Task<Result<VtsModelInventory>> GetInventoryAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> models = await _transport.RequestAsync(
            broadcasterId,
            "AvailableModelsRequest",
            null,
            ct
        );
        if (models.IsFailure)
            return Result.Failure<VtsModelInventory>(models.ErrorMessage!, models.ErrorCode!);

        // Hotkeys/expressions depend on a loaded model — their failure degrades to empty lists.
        Result<string> hotkeys = await _transport.RequestAsync(
            broadcasterId,
            "HotkeysInCurrentModelRequest",
            null,
            ct
        );
        Result<string> expressions = await _transport.RequestAsync(
            broadcasterId,
            "ExpressionStateRequest",
            null,
            ct
        );

        return Result.Success(
            new VtsModelInventory(
                ParseModels(models.Value),
                hotkeys.IsSuccess ? ParseHotkeys(hotkeys.Value) : [],
                expressions.IsSuccess ? ParseExpressions(expressions.Value) : []
            )
        );
    }

    private static IReadOnlyList<VtsModelRef> ParseModels(string dataJson)
    {
        List<VtsModelRef> models = [];
        using JsonDocument doc = JsonDocument.Parse(dataJson);
        if (
            doc.RootElement.TryGetProperty("availableModels", out JsonElement items)
            && items.ValueKind == JsonValueKind.Array
        )
            foreach (JsonElement item in items.EnumerateArray())
                models.Add(
                    new VtsModelRef(
                        item.TryGetProperty("modelID", out JsonElement id)
                            ? id.GetString() ?? ""
                            : "",
                        item.TryGetProperty("modelName", out JsonElement name)
                            ? name.GetString() ?? ""
                            : "",
                        item.TryGetProperty("modelLoaded", out JsonElement loaded)
                            && loaded.GetBoolean()
                    )
                );
        return models;
    }

    private static IReadOnlyList<VtsHotkeyRef> ParseHotkeys(string dataJson)
    {
        List<VtsHotkeyRef> hotkeys = [];
        using JsonDocument doc = JsonDocument.Parse(dataJson);
        if (
            doc.RootElement.TryGetProperty("availableHotkeys", out JsonElement items)
            && items.ValueKind == JsonValueKind.Array
        )
            foreach (JsonElement item in items.EnumerateArray())
                hotkeys.Add(
                    new VtsHotkeyRef(
                        item.TryGetProperty("hotkeyID", out JsonElement id)
                            ? id.GetString() ?? ""
                            : "",
                        item.TryGetProperty("name", out JsonElement name)
                            ? name.GetString() ?? ""
                            : "",
                        item.TryGetProperty("type", out JsonElement type)
                            ? type.GetString() ?? ""
                            : ""
                    )
                );
        return hotkeys;
    }

    private static IReadOnlyList<string> ParseExpressions(string dataJson)
    {
        List<string> expressions = [];
        using JsonDocument doc = JsonDocument.Parse(dataJson);
        if (
            doc.RootElement.TryGetProperty("expressions", out JsonElement items)
            && items.ValueKind == JsonValueKind.Array
        )
            foreach (JsonElement item in items.EnumerateArray())
                if (item.TryGetProperty("file", out JsonElement file))
                    expressions.Add(file.GetString() ?? "");
        return expressions;
    }

    private async Task<Result> StatusAsync(
        Guid broadcasterId,
        string requestType,
        string dataJson,
        CancellationToken ct
    )
    {
        Result<string> response = await _transport.RequestAsync(
            broadcasterId,
            requestType,
            dataJson,
            ct
        );
        return response.IsFailure
            ? Result.Failure(response.ErrorMessage!, response.ErrorCode!)
            : Result.Success();
    }
}
