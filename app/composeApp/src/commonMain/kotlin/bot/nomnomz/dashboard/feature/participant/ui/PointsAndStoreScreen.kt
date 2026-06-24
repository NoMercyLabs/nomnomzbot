// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.participant.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CatalogItem
import bot.nomnomz.dashboard.core.network.SavingsJar
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.participant.state.StoreState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.participant_loading
import nomnomzbot.composeapp.generated.resources.participant_store_balance
import nomnomzbot.composeapp.generated.resources.participant_store_buy
import nomnomzbot.composeapp.generated.resources.participant_store_catalog_empty
import nomnomzbot.composeapp.generated.resources.participant_store_catalog_title
import nomnomzbot.composeapp.generated.resources.participant_store_contribute
import nomnomzbot.composeapp.generated.resources.participant_store_cost
import nomnomzbot.composeapp.generated.resources.participant_store_frozen
import nomnomzbot.composeapp.generated.resources.participant_store_jar_goal
import nomnomzbot.composeapp.generated.resources.participant_store_jars_empty
import nomnomzbot.composeapp.generated.resources.participant_store_jars_title
import nomnomzbot.composeapp.generated.resources.participant_store_lifetime
import nomnomzbot.composeapp.generated.resources.participant_store_sold_out
import nomnomzbot.composeapp.generated.resources.participant_store_transfer_amount
import nomnomzbot.composeapp.generated.resources.participant_store_transfer_recipient
import nomnomzbot.composeapp.generated.resources.participant_store_transfer_send
import nomnomzbot.composeapp.generated.resources.participant_store_transfer_title
import org.jetbrains.compose.resources.stringResource

// My Points & Store: the caller's own wallet (balance + lifetime), the channel catalog (read + purchase), the
// community jars (read + contribute), and points transfers (capability-gated on economy:transfer:write). Every
// write addresses the caller's own account; the backend binds buyer/contributor/actor from the JWT and re-checks.
@Composable
fun PointsAndStoreScreen(controller: ParticipantController) {
    val state: StoreState by controller.store.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.loadStore() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: StoreState = state) {
            is StoreState.Loading -> ParticipantMessage(stringResource(Res.string.participant_loading))
            is StoreState.Error ->
                ParticipantError(detail = current.detail, onRetry = { scope.launch { controller.loadStore() } })
            is StoreState.Ready ->
                Ready(
                    state = current,
                    onPurchase = { itemId -> scope.launch { controller.purchase(itemId, null) } },
                    onContribute = { jarId, amount -> scope.launch { controller.contributeToJar(jarId, amount) } },
                    onTransfer = { to, amount -> scope.launch { controller.transfer(to, amount, null) } },
                )
        }
    }
}

@Composable
private fun Ready(
    state: StoreState.Ready,
    onPurchase: (String) -> Unit,
    onContribute: (String, Long) -> Unit,
    onTransfer: (String, Long) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        state.actionError?.let { ActionErrorBanner(detail = it) }
        BalanceCard(state = state)
        if (state.canTransfer) TransferCard(frozen = state.account.isFrozen, onTransfer = onTransfer)
        CatalogCard(items = state.catalog, frozen = state.account.isFrozen, onPurchase = onPurchase)
        JarsCard(jars = state.jars, frozen = state.account.isFrozen, onContribute = onContribute)
    }
}

@Composable
private fun BalanceCard(state: StoreState.Ready) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_store_balance, state.account.balance)) {
        Text(
            text =
                stringResource(
                    Res.string.participant_store_lifetime,
                    state.account.lifetimeEarned,
                    state.account.lifetimeSpent,
                ),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        if (state.account.isFrozen) {
            Text(
                text = stringResource(Res.string.participant_store_frozen),
                style = typography.sm,
                color = tokens.destructive,
            )
        }
    }
}

@Composable
private fun TransferCard(frozen: Boolean, onTransfer: (String, Long) -> Unit) {
    val spacing = LocalSpacing.current
    var recipient: String by remember { mutableStateOf("") }
    var amount: String by remember { mutableStateOf("") }

    val parsedAmount: Long? = amount.toLongOrNull()
    val enabled: Boolean = !frozen && recipient.isNotBlank() && parsedAmount != null && parsedAmount > 0

    SectionCard(title = stringResource(Res.string.participant_store_transfer_title)) {
        OutlinedTextField(
            value = recipient,
            onValueChange = { recipient = it },
            label = { Text(text = stringResource(Res.string.participant_store_transfer_recipient)) },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            OutlinedTextField(
                value = amount,
                onValueChange = { amount = it.filter { ch -> ch.isDigit() } },
                label = { Text(text = stringResource(Res.string.participant_store_transfer_amount)) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.weight(1f),
            )
            TextButton(
                onClick = {
                    val value: Long = parsedAmount ?: return@TextButton
                    onTransfer(recipient.trim(), value)
                    recipient = ""
                    amount = ""
                },
                enabled = enabled,
            ) {
                Text(text = stringResource(Res.string.participant_store_transfer_send))
            }
        }
    }
}

@Composable
private fun CatalogCard(items: List<CatalogItem>, frozen: Boolean, onPurchase: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_store_catalog_title)) {
        val enabledItems: List<CatalogItem> = items.filter { it.isEnabled }
        if (enabledItems.isEmpty()) {
            Text(
                text = stringResource(Res.string.participant_store_catalog_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                enabledItems.forEach { item -> CatalogRow(item = item, frozen = frozen, onPurchase = onPurchase) }
            }
        }
    }
}

@Composable
private fun CatalogRow(item: CatalogItem, frozen: Boolean, onPurchase: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val soldOut: Boolean = item.stockRemaining != null && item.stockRemaining <= 0
    val buyLabel: String = stringResource(Res.string.participant_store_buy, item.name)

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = item.name,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = stringResource(Res.string.participant_store_cost, item.cost),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        if (soldOut) {
            Text(
                text = stringResource(Res.string.participant_store_sold_out),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        } else {
            TextButton(
                onClick = { onPurchase(item.id) },
                enabled = !frozen,
                modifier = Modifier.semantics { contentDescription = buyLabel },
            ) {
                Text(text = stringResource(Res.string.participant_store_buy, item.name), maxLines = 1)
            }
        }
    }
}

@Composable
private fun JarsCard(jars: List<SavingsJar>, frozen: Boolean, onContribute: (String, Long) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    SectionCard(title = stringResource(Res.string.participant_store_jars_title)) {
        if (jars.isEmpty()) {
            Text(
                text = stringResource(Res.string.participant_store_jars_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                jars.forEach { jar -> JarRow(jar = jar, frozen = frozen, onContribute = onContribute) }
            }
        }
    }
}

@Composable
private fun JarRow(jar: SavingsJar, frozen: Boolean, onContribute: (String, Long) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var amount: String by remember { mutableStateOf("") }
    val parsedAmount: Long? = amount.toLongOrNull()
    val enabled: Boolean = jar.isOpen && !frozen && parsedAmount != null && parsedAmount > 0

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = jar.name,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            text = stringResource(Res.string.participant_store_jar_goal, jar.balance, jar.goalAmount ?: 0L),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            OutlinedTextField(
                value = amount,
                onValueChange = { amount = it.filter { ch -> ch.isDigit() } },
                label = { Text(text = stringResource(Res.string.participant_store_contribute)) },
                singleLine = true,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.weight(1f),
            )
            TextButton(
                onClick = {
                    val value: Long = parsedAmount ?: return@TextButton
                    onContribute(jar.id, value)
                    amount = ""
                },
                enabled = enabled,
            ) {
                Text(text = stringResource(Res.string.participant_store_contribute))
            }
        }
    }
}
