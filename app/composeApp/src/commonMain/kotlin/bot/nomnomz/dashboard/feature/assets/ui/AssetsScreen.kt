// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.assets.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyLinkButton
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChannelAsset
import bot.nomnomz.dashboard.feature.assets.state.AssetsController
import bot.nomnomz.dashboard.feature.assets.state.AssetsState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.assets_action_error
import nomnomzbot.composeapp.generated.resources.assets_copied
import nomnomzbot.composeapp.generated.resources.assets_copy_url
import nomnomzbot.composeapp.generated.resources.assets_delete_action
import nomnomzbot.composeapp.generated.resources.assets_delete_cancel
import nomnomzbot.composeapp.generated.resources.assets_delete_confirm
import nomnomzbot.composeapp.generated.resources.assets_delete_message
import nomnomzbot.composeapp.generated.resources.assets_delete_title
import nomnomzbot.composeapp.generated.resources.assets_empty
import nomnomzbot.composeapp.generated.resources.assets_error
import nomnomzbot.composeapp.generated.resources.assets_kind_audio
import nomnomzbot.composeapp.generated.resources.assets_kind_image
import nomnomzbot.composeapp.generated.resources.assets_loading
import nomnomzbot.composeapp.generated.resources.assets_retry
import nomnomzbot.composeapp.generated.resources.assets_size_kb
import nomnomzbot.composeapp.generated.resources.assets_size_mb
import nomnomzbot.composeapp.generated.resources.assets_upload_action
import nomnomzbot.composeapp.generated.resources.shell_nav_assets
import org.jetbrains.compose.resources.stringResource

// The Assets page (Sound Clips twin): the channel's uploaded media library for overlays and widgets.
// Lists real assets from the backend; brokers upload (native OS file picker → multipart POST — same
// name replaces the asset in place), copy of the anonymous absolute serving URL for OBS browser
// sources, and delete.
@Composable
fun AssetsScreen(controller: AssetsController, role: ManagementRole?) {
    val state: AssetsState by controller.state.collectAsStateWithLifecycle()
    val isUploading: Boolean by controller.isUploading.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Assets)

    var deleteTarget: ChannelAsset? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: AssetsState = state) {
            is AssetsState.Loading -> CenteredMessage(stringResource(Res.string.assets_loading))
            is AssetsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is AssetsState.Empty ->
                AssetList(
                    assets = emptyList(),
                    actionError = null,
                    manage = manage,
                    isUploading = isUploading,
                    publicUrl = controller::publicUrl,
                    onUpload = { scope.launch { controller.uploadAsset() } },
                    onDelete = { asset -> deleteTarget = asset },
                )
            is AssetsState.Ready ->
                AssetList(
                    assets = current.assets,
                    actionError = current.actionError,
                    manage = manage,
                    isUploading = isUploading,
                    publicUrl = controller::publicUrl,
                    onUpload = { scope.launch { controller.uploadAsset() } },
                    onDelete = { asset -> deleteTarget = asset },
                )
        }
    }

    deleteTarget?.let { asset ->
        ConfirmDialog(
            title = stringResource(Res.string.assets_delete_title),
            message = stringResource(Res.string.assets_delete_message, asset.displayName),
            confirmLabel = stringResource(Res.string.assets_delete_confirm),
            dismissLabel = stringResource(Res.string.assets_delete_cancel),
            destructive = true,
            onConfirm = {
                scope.launch { controller.deleteAsset(asset.id) }
                deleteTarget = null
            },
            onDismiss = { deleteTarget = null },
        )
    }
}

@Composable
private fun AssetList(
    assets: List<ChannelAsset>,
    actionError: String?,
    manage: ManageDecision,
    isUploading: Boolean,
    publicUrl: (String) -> String?,
    onUpload: () -> Unit,
    onDelete: (ChannelAsset) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_assets)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onUpload, enabled = enabled && !isUploading) {
                    Text(text = stringResource(Res.string.assets_upload_action))
                }
            }
        }

        actionError?.let { detail ->
            ActionErrorBanner(message = stringResource(Res.string.assets_action_error, detail))
        }

        // Single card table — all assets in one container, rows separated by hairlines.
        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (assets.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = stringResource(Res.string.assets_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    itemsIndexed(items = assets, key = { _, asset -> asset.id }) { index, asset ->
                        AssetRow(
                            asset = asset,
                            manage = manage,
                            publicUrl = publicUrl,
                            onDelete = { onDelete(asset) },
                        )
                        if (index < assets.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

// Asset row inside the shared card — no per-row background; dividers separate entries.
@Composable
private fun AssetRow(
    asset: ChannelAsset,
    manage: ManageDecision,
    publicUrl: (String) -> String?,
    onDelete: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    val deleteLabel: String = stringResource(Res.string.assets_delete_action, asset.displayName)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = asset.displayName,
                    style = typography.sm,
                    color = tokens.foreground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                // Kind badge — subtle, pill-shaped (same treatment as the sound "disabled" badge).
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(tokens.radius.sm))
                        .background(tokens.muted)
                        .padding(horizontal = spacing.s2, vertical = spacing.s0_5),
                ) {
                    Text(
                        text =
                            stringResource(
                                if (asset.kind == "image") Res.string.assets_kind_image
                                else Res.string.assets_kind_audio
                            ),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
            // Secondary row: slug name + human-readable size.
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = asset.name,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f, fill = false),
                )
                if (asset.sizeBytes > 0) {
                    Text(
                        text = humanReadableSize(asset.sizeBytes),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
        }

        // Copy-URL is a read-safe action (the URL is anonymous by design) — not gated on manage.
        CopyLinkButton(
            url = publicUrl(asset.url) ?: asset.url,
            copyLabel = stringResource(Res.string.assets_copy_url),
            copiedLabel = stringResource(Res.string.assets_copied),
        )

        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

// "512 KB" below a megabyte, "1.2 MB" from there up — the unit text lives in the string resources.
@Composable
private fun humanReadableSize(sizeBytes: Long): String {
    val kb: Long = sizeBytes / 1024
    return if (kb < 1024) {
        stringResource(Res.string.assets_size_kb, kb.toInt().coerceAtLeast(1))
    } else {
        val tenthsOfMb: Long = (sizeBytes * 10) / (1024 * 1024)
        stringResource(Res.string.assets_size_mb, "${tenthsOfMb / 10}.${tenthsOfMb % 10}")
    }
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.assets_error, detail),
                style = typography.base,
                color = tokens.destructive,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) {
                Text(
                    text = stringResource(Res.string.assets_retry),
                    color = tokens.primary,
                )
            }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
