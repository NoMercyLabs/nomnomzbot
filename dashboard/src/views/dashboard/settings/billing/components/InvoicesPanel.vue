<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What the channel was billed, newest first, a page at a time.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import Receipt from '~icons/nnz/receipt';

import { useInvoices, type ChannelInvoice } from '@/api/billing';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { centsAsMoney } from '@/lib/number';

const { t, d, locale } = useI18n();
const { channelId } = useCurrentChannel();

const page = ref(1);
const invoices = useInvoices(channelId, page);

const rows = computed(() => invoices.data.value?.invoices ?? []);
const hasMore = computed(() => invoices.data.value?.hasMore ?? false);

const state = computed<PanelState>(() => {
  if (invoices.isError.value)
    return 'error';

  if (invoices.isPending.value)
    return 'loading';

  return rows.value.length === 0
    ? 'empty'
    : 'ready';
});

function dateOf(value: string): string {
  return d(new Date(value), { dateStyle: 'medium' });
}

function moneyOf(cents: number | string, invoice: ChannelInvoice): string {
  return centsAsMoney(cents, invoice.currency, locale.value);
}

function detailsOf(invoice: ChannelInvoice): string {
  const parts = [
    `${t('dashboard.settings.billing.invoices.issued')} ${dateOf(invoice.issuedAt)}`,
  ];

  if (invoice.dueAt) {
    parts.push(`${t('dashboard.settings.billing.invoices.due')} ${dateOf(invoice.dueAt)}`);
  }

  if (invoice.paidAt) {
    parts.push(`${t('dashboard.settings.billing.invoices.paid')} ${dateOf(invoice.paidAt)}`);
  }

  if (Number(invoice.amountRefundedCents) > 0) {
    parts.push(`${t('dashboard.settings.billing.invoices.refunded')} ${moneyOf(invoice.amountRefundedCents, invoice)}`);
  }

  return parts.join(' · ');
}
</script>

<template>
  <PanelCard
    data-panel="invoices"
    tier="quiet"
    pad
    :title="$t('dashboard.settings.billing.invoices.title')"
    :description="$t('dashboard.settings.billing.invoices.description')"
    :state="state"
    :empty-text="$t('dashboard.settings.billing.invoices.empty')"
    :error-text="$t('dashboard.settings.billing.invoices.error')"
    @retry="invoices.refetch()"
  >
    <div class="grid gap-3">
      <RowGroup>
        <div
          v-for="invoice in rows"
          :key="invoice.id"
          data-invoice
          class="flex flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <Receipt class="size-5 flex-none text-muted-foreground" />
          <div class="min-w-0 flex-1 basis-56">
            <div class="flex flex-wrap items-baseline gap-x-3">
              <span class="text-sm font-medium">{{ invoice.number ?? $t('dashboard.settings.billing.invoices.number') }}</span>
              <span data-invoice-amount class="text-sm">{{ moneyOf(invoice.amountDueCents, invoice) }}</span>
              <span data-invoice-status class="text-sm text-muted-foreground">{{ invoice.status }}</span>
            </div>
            <p class="text-sm text-muted-foreground">{{ detailsOf(invoice) }}</p>
          </div>
          <Button v-if="invoice.hostedInvoiceUrl" size="sm" variant="outline" as-child>
            <a :href="invoice.hostedInvoiceUrl" target="_blank" rel="noopener">
              {{ $t('dashboard.settings.billing.invoices.open') }}
            </a>
          </Button>
        </div>
      </RowGroup>
      <div v-if="page > 1 || hasMore" class="flex items-center justify-between gap-2">
        <Button
          data-action="previous"
          size="sm"
          variant="outline"
          :disabled="page <= 1"
          @click="page -= 1"
        >
          {{ $t('dashboard.settings.billing.invoices.previous') }}
        </Button>
        <span class="text-sm text-muted-foreground">{{ $t('dashboard.settings.billing.invoices.page', { page }) }}</span>
        <Button
          data-action="next"
          size="sm"
          variant="outline"
          :disabled="!hasMore"
          @click="page += 1"
        >
          {{ $t('dashboard.settings.billing.invoices.next') }}
        </Button>
      </div>
    </div>
  </PanelCard>
</template>
