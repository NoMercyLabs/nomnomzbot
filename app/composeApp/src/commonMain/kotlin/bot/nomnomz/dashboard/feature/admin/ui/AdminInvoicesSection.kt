// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AdminInvoice
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_invoice_amount
import nomnomzbot.composeapp.generated.resources.admin_invoice_dunning_past_due
import nomnomzbot.composeapp.generated.resources.admin_invoice_issued_label
import nomnomzbot.composeapp.generated.resources.admin_invoice_paid_label
import nomnomzbot.composeapp.generated.resources.admin_invoice_refund
import nomnomzbot.composeapp.generated.resources.admin_invoice_refund_confirm_action
import nomnomzbot.composeapp.generated.resources.admin_invoice_refund_confirm_body
import nomnomzbot.composeapp.generated.resources.admin_invoice_refund_confirm_title
import nomnomzbot.composeapp.generated.resources.admin_invoice_refunded_label
import nomnomzbot.composeapp.generated.resources.admin_invoice_type_label
import nomnomzbot.composeapp.generated.resources.admin_invoices_broadcaster_label
import nomnomzbot.composeapp.generated.resources.admin_invoices_broadcaster_load
import nomnomzbot.composeapp.generated.resources.admin_invoices_empty
import nomnomzbot.composeapp.generated.resources.admin_invoices_heading
import org.jetbrains.compose.resources.stringResource

/**
 * Invoices, dunning and refunds (S-ADMIN-4c) — an operator looks up a tenant by broadcaster id and sees
 * every invoice with its REAL status and, when it applies, a "Past due" badge — computed by the backend's
 * `Invoice.ResolveDunningStatus`, the exact rule the rest of the system reads, never a decorative guess. A
 * paid invoice can be refunded; the row action is a quiet outline trigger (never an equal-weight button next
 * to the rest of the row — refunds are destructive and get their own treatment), and the actual commit sits
 * behind a confirmation dialog that states the counted blast radius — the exact amount and currency about to
 * move — before the loud destructive button can be pressed.
 */
@Composable
internal fun InvoicesSection(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var broadcasterIdInput: String by remember { mutableStateOf(state.invoiceBroadcasterId.orEmpty()) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = stringResource(Res.string.admin_invoices_heading), style = typography.base, color = tokens.foreground)

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            AppTextField(
                value = broadcasterIdInput,
                onValueChange = { broadcasterIdInput = it },
                label = stringResource(Res.string.admin_invoices_broadcaster_label),
                modifier = Modifier.fillMaxWidth(),
            )
            TextButton(
                onClick = { scope.launch { controller.selectInvoiceBroadcaster(broadcasterIdInput) } },
                enabled = broadcasterIdInput.isNotBlank(),
            ) {
                Text(text = stringResource(Res.string.admin_invoices_broadcaster_load))
            }
        }

        if (state.invoiceBroadcasterId != null) {
            if (state.invoices.isEmpty()) {
                EmptyLine(stringResource(Res.string.admin_invoices_empty))
            } else {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        state.invoices.forEachIndexed { index, invoice ->
                            InvoiceRow(
                                invoice = invoice,
                                onRefund = { controller.stageRefund(invoice.id) },
                            )
                            if (index < state.invoices.lastIndex) Separator()
                        }
                    }
                }
            }
        }
    }

    val pending: AdminInvoice? = state.invoices.firstOrNull { it.id == state.refundPendingInvoiceId }
    if (pending != null) {
        RefundConfirmDialog(
            invoice = pending,
            onDismiss = { controller.dismissRefund() },
            onConfirm = { scope.launch { controller.confirmRefund(pending.id) } },
        )
    }
}

@Composable
private fun InvoiceRow(invoice: AdminInvoice, onRefund: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text =
                        resolveRowLabel(
                            primary = invoice.number,
                            secondary = invoice.status,
                            typeLabel = stringResource(Res.string.admin_invoice_type_label),
                            discriminatorSource = invoice.id,
                        ),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                if (invoice.dunningStatus == "PastDue") {
                    Badge(variant = BadgeVariant.Destructive) {
                        Text(text = stringResource(Res.string.admin_invoice_dunning_past_due))
                    }
                }
            }
            Text(
                text = stringResource(
                    Res.string.admin_invoice_amount,
                    invoice.amountDueCents / 100,
                    centsMinorPart(invoice.amountDueCents),
                    invoice.currency.uppercase(),
                ),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            Text(
                text = stringResource(Res.string.admin_invoice_issued_label, invoice.issuedAt),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            invoice.paidAt?.let { paidAt ->
                Text(
                    text = stringResource(Res.string.admin_invoice_paid_label, paidAt),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            invoice.refundedAt?.let { refundedAt ->
                Text(
                    text = stringResource(
                        Res.string.admin_invoice_refunded_label,
                        invoice.amountRefundedCents / 100,
                        centsMinorPart(invoice.amountRefundedCents),
                        invoice.currency.uppercase(),
                        refundedAt,
                    ),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }

        // Refund is destructive and only ever makes sense on a Paid invoice — a quiet outline trigger here,
        // never level with the row's own text; the loud commit lives in RefundConfirmDialog.
        if (invoice.status == "paid") {
            Button(variant = ButtonVariant.Outline, onClick = onRefund) {
                Text(text = stringResource(Res.string.admin_invoice_refund))
            }
        }
    }
}

/** The refund confirmation — the counted blast radius (the exact amount and currency about to move) is
 * shown BEFORE the destructive button can be pressed, never hidden behind a bare "Refund" click. */
@Composable
private fun RefundConfirmDialog(invoice: AdminInvoice, onDismiss: () -> Unit, onConfirm: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_invoice_refund_confirm_title))
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(
            text = stringResource(
                Res.string.admin_invoice_refund_confirm_body,
                invoice.amountPaidCents / 100,
                centsMinorPart(invoice.amountPaidCents),
                invoice.currency.uppercase(),
            ),
            style = typography.sm,
            color = tokens.foreground,
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(variant = ButtonVariant.Destructive, onClick = onConfirm) {
                Text(text = stringResource(Res.string.admin_invoice_refund_confirm_action))
            }
        }
    }
}

/**
 * The two-digit minor-unit part of an integer-cents amount, e.g. 1999 -> "99", 1905 -> "05". Passed as a
 * plain `%s` rather than a width-specced `%02d` — the compose-resources string formatter does not honor
 * flags/width on positional specifiers (`%2$02d` renders literally instead of zero-padding), only a bare
 * conversion, so the padding is done here instead of relied on in the resource string.
 */
private fun centsMinorPart(cents: Int): String = (cents % 100).toString().padStart(2, '0')
