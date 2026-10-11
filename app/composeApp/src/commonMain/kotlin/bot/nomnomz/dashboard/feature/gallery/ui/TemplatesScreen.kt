// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.gallery.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.FieldPair
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography
import bot.nomnomz.dashboard.core.network.ImportPolicy
import bot.nomnomz.dashboard.core.network.MarketplaceItem
import bot.nomnomz.dashboard.feature.bundles.state.BundlesController
import bot.nomnomz.dashboard.feature.bundles.state.BundlesUiState
import bot.nomnomz.dashboard.feature.bundles.ui.ToggleChip
import bot.nomnomz.dashboard.feature.bundles.ui.policyLabel
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.templates_browse
import nomnomzbot.composeapp.generated.resources.templates_by
import nomnomzbot.composeapp.generated.resources.templates_empty
import nomnomzbot.composeapp.generated.resources.templates_error
import nomnomzbot.composeapp.generated.resources.templates_install_button
import nomnomzbot.composeapp.generated.resources.templates_installs
import nomnomzbot.composeapp.generated.resources.templates_loading
import nomnomzbot.composeapp.generated.resources.templates_rating
import nomnomzbot.composeapp.generated.resources.templates_retry
import nomnomzbot.composeapp.generated.resources.templates_row_type
import nomnomzbot.composeapp.generated.resources.templates_search_label
import nomnomzbot.composeapp.generated.resources.templates_subtitle
import nomnomzbot.composeapp.generated.resources.templates_title
import nomnomzbot.composeapp.generated.resources.templates_type_label
import nomnomzbot.composeapp.generated.resources.templates_unavailable
import org.jetbrains.compose.resources.stringResource

// The Templates tab of the Gallery page (frontend-ia.md, Build group): browse the hosted catalogue and install an
// item into the channel. It shares the [BundlesController] with the Bundles tab — the catalogue state and the
// install call live there, and a finished install refreshes the Bundles tab's installed list. The catalogue can be
// unavailable; the screen then shows an honest empty-state card instead of an error.
@Composable
fun TemplatesScreen(controller: BundlesController, role: ManagementRole?) {
    val state: BundlesUiState by controller.state.collectAsStateWithLifecycle()
    val scope: CoroutineScope = rememberCoroutineScope()
    val spacing: Spacing = LocalSpacing.current
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.GalleryTemplates)

    LaunchedEffect(Unit) {
        controller.load()
        controller.browseMarketplace(null, null)
    }

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: BundlesUiState = state) {
            is BundlesUiState.Loading -> CenteredMessage(stringResource(Res.string.templates_loading))
            is BundlesUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is BundlesUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s6),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.templates_title),
                        subtitle = stringResource(Res.string.templates_subtitle),
                    )
                    current.notice?.let { NoticeText(it) }
                    CatalogueSection(
                        available = current.marketplaceAvailable,
                        items = current.marketplace,
                        manage = manage,
                        onBrowse = { q, type -> scope.launch { controller.browseMarketplace(q, type) } },
                        onInstall = { itemId, policy ->
                            scope.launch { controller.installFromMarketplace(itemId, policy) }
                        },
                    )
                }
        }
    }
}

@Composable
private fun CatalogueSection(
    available: Boolean,
    items: List<MarketplaceItem>,
    manage: ManageDecision,
    onBrowse: (q: String?, type: String?) -> Unit,
    onInstall: (itemId: String, policy: String) -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current

    var query: String by remember { mutableStateOf("") }
    var type: String by remember { mutableStateOf("") }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        FieldPair(
            verticalAlignment = Alignment.Bottom,
            first = { fieldModifier ->
                AppTextField(
                    value = query,
                    onValueChange = { query = it },
                    label = stringResource(Res.string.templates_search_label),
                    modifier = fieldModifier,
                )
            },
            second = { fieldModifier ->
                AppTextField(
                    value = type,
                    onValueChange = { type = it },
                    label = stringResource(Res.string.templates_type_label),
                    modifier = fieldModifier,
                )
            },
            action = {
                Button(
                    onClick = { onBrowse(query.trim().ifBlank { null }, type.trim().ifBlank { null }) }
                ) {
                    Text(text = stringResource(Res.string.templates_browse))
                }
            },
        )

        if (!available) {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(modifier = Modifier.fillMaxWidth().padding(spacing.s6)) {
                    CenteredMessage(stringResource(Res.string.templates_unavailable))
                }
            }
        } else if (items.isEmpty()) {
            CenteredMessage(stringResource(Res.string.templates_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    items.forEachIndexed { index, item ->
                        TemplateRow(item = item, manage = manage, onInstall = onInstall)
                        if (index < items.lastIndex) Separator()
                    }
                }
            }
        }
    }
}

@Composable
private fun TemplateRow(
    item: MarketplaceItem,
    manage: ManageDecision,
    onInstall: (itemId: String, policy: String) -> Unit,
) {
    val spacing: Spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current

    var policy: String by remember { mutableStateOf(ImportPolicy.Rename) }

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            val displayName: String =
                resolveRowLabel(
                    primary = item.name,
                    secondary = item.author,
                    typeLabel = stringResource(Res.string.templates_row_type),
                    discriminatorSource = item.itemId,
                )
            Text(
                text = displayName,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = stringResource(Res.string.templates_rating, item.rating.toString()),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                Text(
                    text = stringResource(Res.string.templates_installs, item.installs.toString()),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
        Text(
            text = stringResource(Res.string.templates_by, item.author),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        if (item.summary.isNotBlank()) {
            Text(text = item.summary, style = typography.sm, color = tokens.cardForeground)
        }
        if (item.capabilities.isNotEmpty()) {
            FlowRow(
                horizontalArrangement = Arrangement.spacedBy(spacing.s1),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                item.capabilities.forEach { capability ->
                    Badge(variant = BadgeVariant.Secondary) { Text(text = capability, style = typography.xs) }
                }
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ImportPolicy.all.forEach { option ->
                    ToggleChip(
                        label = stringResource(policyLabel(option)),
                        selected = option == policy,
                        onClick = { policy = option },
                    )
                }
            }
            // One primary action sits in the search group (Browse); a row's Install is outline so a long list
            // does not become a column of equal-weight primary buttons.
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { onInstall(item.itemId, policy) },
                    variant = ButtonVariant.Outline,
                    enabled = enabled,
                ) {
                    Text(text = stringResource(Res.string.templates_install_button))
                }
            }
        }
    }
}

@Composable
private fun NoticeText(text: String) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    Text(text = text, style = typography.sm, color = tokens.mutedForeground, modifier = Modifier.fillMaxWidth())
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.templates_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.templates_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens: Tokens = LocalTokens.current
    val typography: Typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
