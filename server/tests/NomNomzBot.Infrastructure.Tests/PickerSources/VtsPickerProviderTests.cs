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
using NomNomzBot.Application.Vts.Dtos;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Infrastructure.Vts.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// The VTube Studio pickers (model, hotkey, expression) list what the channel's connected VTube Studio reports,
/// say why when it is not connected, and keep the VTS id as the saved value.
/// </summary>
public sealed class VtsPickerProviderTests
{
    private static readonly Guid Broadcaster = Guid.NewGuid();
    private const string NotConnected = "VTube Studio is not connected.";

    private static IVtsControlService Connected()
    {
        IVtsControlService vts = Substitute.For<IVtsControlService>();
        vts.GetInventoryAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new VtsModelInventory(
                        [new("m-2", "Zed", false), new("m-1", "Akari", true)],
                        [
                            new("h-1", "Wave", "TriggerAnimation"),
                            new("h-2", "Blush", "ToggleExpression"),
                        ],
                        ["smile.exp3.json", "angry.exp3.json"],
                        ["Hair", "Cheeks", "Accessory"]
                    )
                )
            );
        return vts;
    }

    private static IVtsControlService Disconnected()
    {
        IVtsControlService vts = Substitute.For<IVtsControlService>();
        vts.GetInventoryAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<VtsModelInventory>(NotConnected, "VTS_NOT_CONNECTED"));
        return vts;
    }

    [Fact]
    public void Each_provider_serves_its_own_kind()
    {
        IVtsControlService vts = Substitute.For<IVtsControlService>();

        Assert.Equal(PipelineActionFieldKind.VtsModel, new VtsModelOptionProvider(vts).Kind);
        Assert.Equal(PipelineActionFieldKind.VtsHotkey, new VtsHotkeyOptionProvider(vts).Kind);
        Assert.Equal(
            PipelineActionFieldKind.VtsExpression,
            new VtsExpressionOptionProvider(vts).Kind
        );
    }

    [Fact]
    public async Task Model_lists_models_by_name_with_the_id_saved_and_the_loaded_one_marked()
    {
        Result<PipelineOptionListResult> result = await new VtsModelOptionProvider(
            Connected()
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["m-1", "m-2"], PickerAsk.Values(result));
        PipelineOption loaded = result.Value.Items[0];
        Assert.Equal("Akari", loaded.Label);
        Assert.Equal("loaded", loaded.SecondaryText);
        Assert.Null(result.Value.Items[1].SecondaryText);
        Assert.Equal(PipelineOptionState.Selectable, loaded.State);
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task Hotkey_lists_hotkeys_with_the_id_saved_and_the_type_as_secondary_text()
    {
        Result<PipelineOptionListResult> result = await new VtsHotkeyOptionProvider(
            Connected()
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["h-2", "h-1"], PickerAsk.Values(result));
        Assert.Equal("Blush", result.Value.Items[0].Label);
        Assert.Equal("ToggleExpression", result.Value.Items[0].SecondaryText);
    }

    [Fact]
    public async Task Expression_lists_expression_files_and_the_file_name_is_the_saved_value()
    {
        Result<PipelineOptionListResult> result = await new VtsExpressionOptionProvider(
            Connected()
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.Equal(["angry.exp3.json", "smile.exp3.json"], PickerAsk.Values(result));
        Assert.All(result.Value.Items, o => Assert.Equal(o.Value, o.Label));
    }

    [Fact]
    public async Task Search_value_lookup_and_paging_narrow_the_list()
    {
        VtsHotkeyOptionProvider provider = new(Connected());

        Result<PipelineOptionListResult> searched = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "wav")
        );
        Result<PipelineOptionListResult> looked = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(search: "ignored", value: "h-2")
        );
        Result<PipelineOptionListResult> second = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(page: new PaginationParams(Page: 2, PageSize: 1))
        );
        Result<PipelineOptionListResult> gone = await provider.GetOptionsAsync(
            Broadcaster,
            PickerAsk.Of(value: "h-removed")
        );

        Assert.Equal(["h-1"], PickerAsk.Values(searched));
        Assert.Equal(["h-2"], PickerAsk.Values(looked));
        Assert.Equal(["h-1"], PickerAsk.Values(second));
        Assert.Equal(2, second.Value.TotalCount);
        Assert.True(gone.Value.SourceAvailable);
        Assert.Empty(gone.Value.Items);
    }

    [Fact]
    public async Task A_disconnected_vts_is_unavailable_with_its_reason_for_every_kind()
    {
        IVtsControlService vts = Disconnected();

        PickerAsk.AssertUnavailable(
            await new VtsModelOptionProvider(vts).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            NotConnected
        );
        PickerAsk.AssertUnavailable(
            await new VtsHotkeyOptionProvider(vts).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            NotConnected
        );
        PickerAsk.AssertUnavailable(
            await new VtsExpressionOptionProvider(vts).GetOptionsAsync(Broadcaster, PickerAsk.Of()),
            NotConnected
        );
    }

    [Fact]
    public async Task The_inventory_is_read_for_the_asking_channel_only()
    {
        Guid other = Guid.NewGuid();
        IVtsControlService vts = Connected();
        vts.GetInventoryAsync(other, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new VtsModelInventory([new("other-model", "Other", false)], [], [], [])
                )
            );

        Result<PipelineOptionListResult> result = await new VtsModelOptionProvider(
            vts
        ).GetOptionsAsync(Broadcaster, PickerAsk.Of());

        Assert.DoesNotContain("other-model", PickerAsk.Values(result));
    }
}
