// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.streaminfo.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.feature.settings.ui.CenteredMessage
import bot.nomnomz.dashboard.feature.settings.ui.ErrorContent
import bot.nomnomz.dashboard.feature.settings.ui.SaveBar
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoController
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.settings_label_category
import nomnomzbot.composeapp.generated.resources.settings_label_tags
import nomnomzbot.composeapp.generated.resources.settings_label_title
import nomnomzbot.composeapp.generated.resources.settings_loading
import nomnomzbot.composeapp.generated.resources.settings_status_live
import nomnomzbot.composeapp.generated.resources.settings_status_offline
import nomnomzbot.composeapp.generated.resources.settings_tags_hint
import nomnomzbot.composeapp.generated.resources.shell_nav_stream_info
import nomnomzbot.composeapp.generated.resources.settings_title_invalid
import org.jetbrains.compose.resources.stringResource

// The Stream page "Info and presets" tab: the broadcast title, category and tags editor plus the live status. It
// moved here from the Settings page. Editing is a Moderator write (the backend re-checks); below that floor the
// fields go read-only with the reason via the gated save bar. The page header and tab strip come from the shell.
@Composable
fun StreamInfoScreen(controller: StreamInfoController, role: ManagementRole?) {
    val state: StreamInfoState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val manage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Moderator)

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().padding(LocalSpacing.current.s6),
        verticalArrangement = Arrangement.spacedBy(LocalSpacing.current.s6),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_stream_info))
        Box(modifier = Modifier.fillMaxWidth().weight(1f)) {
            when (val current: StreamInfoState = state) {
                is StreamInfoState.Loading -> CenteredMessage(stringResource(Res.string.settings_loading))
                is StreamInfoState.Error ->
                    ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
                is StreamInfoState.Ready ->
                    ReadyContent(
                        state = current,
                        manage = manage,
                        onSave = { title, gameName, tags -> scope.launch { controller.save(title, gameName, tags) } },
                    )
            }
        }
    }
}

// Tags are edited as a single comma-separated field — the natural shape for a short list — and normalized to the
// list the API wants: split on commas, trim each, drop blanks. Round-trips with [joinTags] so an unchanged field
// is byte-for-byte the loaded list and the "differs from loaded" check below stays exact.
private fun parseTags(raw: String): List<String> =
    raw.split(',').map { it.trim() }.filter { it.isNotEmpty() }

private fun joinTags(tags: List<String>): String = tags.joinToString(", ")

@Composable
private fun ReadyContent(
    state: StreamInfoState.Ready,
    manage: ManageDecision,
    onSave: (title: String, gameName: String, tags: List<String>) -> Unit,
) {
    val spacing = LocalSpacing.current
    val loaded: StreamInfo = state.info

    // Local editable form, re-seeded whenever new info loads (initial load or a successful save). Holding it
    // screen-side keeps the controller a thin persistence boundary; `remember(loaded)` resets every field to the
    // saved baseline so the "differs from loaded" check below is exact.
    var title: String by remember(loaded) { mutableStateOf(loaded.title.orEmpty()) }
    var gameName: String by remember(loaded) { mutableStateOf(loaded.gameName.orEmpty()) }
    var tagsText: String by remember(loaded) { mutableStateOf(joinTags(loaded.tags)) }

    val editedTags: List<String> = parseTags(tagsText)

    // Twitch rejects an empty broadcast title, so the form requires a non-blank title before it lets a save go.
    val titleValid: Boolean = title.isNotBlank()

    // Save is offered only when the form is valid AND actually differs from the saved baseline — saving an
    // unchanged stream info is a no-op the user shouldn't be invited to make. Each input is a State-backed local
    // recomputed every recomposition, so this re-evaluates as fields change.
    val dirty: Boolean =
        title != loaded.title.orEmpty() ||
            gameName != loaded.gameName.orEmpty() ||
            editedTags != loaded.tags
    val canSave: Boolean = titleValid && dirty && !state.saving

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        StatusBanner(isLive = loaded.isLive)

        EditCard(
            title = title,
            onTitleChange = { title = it },
            titleValid = titleValid,
            gameName = gameName,
            onGameNameChange = { gameName = it },
            tagsText = tagsText,
            onTagsChange = { tagsText = it },
            // Editing the broadcast metadata gates at the stream-info (Editor) floor; below it the fields go
            // read-only with reason via the gated SaveBar below.
            enabled = !state.saving && manage.isAllowed,
        )

        SaveBar(
            saving = state.saving,
            justSaved = state.justSaved,
            saveError = state.saveError,
            manage = manage,
            canSave = canSave && manage.isAllowed,
            onSave = { onSave(title.trim(), gameName.trim(), editedTags) },
        )
    }
}

@Composable
private fun StatusBanner(isLive: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusText: String =
        stringResource(if (isLive) Res.string.settings_status_live else Res.string.settings_status_offline)

    // One node for screen readers: "Live" rather than a disconnected dot + label.
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clearAndSetSemantics { contentDescription = statusText },
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Box(
                modifier = Modifier
                    .size(spacing.s2)
                    .clip(CircleShape)
                    .background(if (isLive) tokens.primary else tokens.mutedForeground),
            )
            Text(text = statusText, style = typography.xl, color = tokens.cardForeground)
        }
    }
}

@Composable
private fun EditCard(
    title: String,
    onTitleChange: (String) -> Unit,
    titleValid: Boolean,
    gameName: String,
    onGameNameChange: (String) -> Unit,
    tagsText: String,
    onTagsChange: (String) -> Unit,
    enabled: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s4),
        ) {
            TitleField(value = title, onValueChange = onTitleChange, valid = titleValid, enabled = enabled)
            CategoryField(value = gameName, onValueChange = onGameNameChange, enabled = enabled)
            TagsField(value = tagsText, onValueChange = onTagsChange, enabled = enabled)
        }
    }
}

@Composable
private fun TitleField(value: String, onValueChange: (String) -> Unit, valid: Boolean, enabled: Boolean) {
    AppTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        isError = !valid,
        modifier = Modifier.fillMaxWidth(),
        label = stringResource(Res.string.settings_label_title),
        errorText = stringResource(Res.string.settings_title_invalid),
    )
}

@Composable
private fun CategoryField(value: String, onValueChange: (String) -> Unit, enabled: Boolean) {
    AppTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
        label = stringResource(Res.string.settings_label_category),
    )
}

@Composable
private fun TagsField(value: String, onValueChange: (String) -> Unit, enabled: Boolean) {
    AppTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
        label = stringResource(Res.string.settings_label_tags),
        supportingText = stringResource(Res.string.settings_tags_hint),
    )
}
