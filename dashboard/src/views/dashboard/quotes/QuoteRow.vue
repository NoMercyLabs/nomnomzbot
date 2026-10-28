<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One quote as a row: its number in a muted column, the line, then who said it, the game and the date.
// With `actions` the row ends with Edit and Delete, each behind its own permission. The random pick shows a quote
// with no actions.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { Quote } from '@/api/quotes';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { shortDate } from '@/lib/time';

import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';

const props = withDefaults(defineProps<{ quote: Quote; actions?: boolean }>(), { actions: false });

defineEmits<{ edit: []; remove: [] }>();

const { t, n, locale } = useI18n();
const { can } = useCan();

const label = computed<string>(() =>
  t('dashboard.quotes.numberLabel', { number: n(Number(props.quote.number)) }));

const attribution = computed<string>(() => {
  const when = props.quote.quotedAt
    ? shortDate(props.quote.quotedAt, locale.value)
    : '';

  return [props.quote.quotedDisplayName, props.quote.contextGame, when].filter(Boolean).join(' · ');
});

const showActions = computed<boolean>(
  () => props.actions && (can('quotes:write') || can('quotes:delete')),
);
</script>

<template>
  <SummaryRow :title="quote.text" :detail="attribution">
    <template #leading>
      <span class="min-w-10 flex-none text-sm text-muted-foreground tabular-nums">{{ label }}</span>
    </template>
    <template v-if="showActions" #trailing>
      <Button
        v-if="can('quotes:write')"
        variant="ghost"
        size="icon"
        data-quote-edit
        :aria-label="t('dashboard.quotes.edit', { number: Number(quote.number) })"
        :title="t('dashboard.quotes.edit', { number: Number(quote.number) })"
        @click="$emit('edit')"
      >
        <Edit class="size-5" aria-hidden="true" />
      </Button>
      <Button
        v-if="can('quotes:delete')"
        variant="ghost"
        size="icon"
        data-quote-delete
        :aria-label="t('dashboard.quotes.delete.title', { number: Number(quote.number) })"
        :title="t('dashboard.quotes.delete.title', { number: Number(quote.number) })"
        @click="$emit('remove')"
      >
        <Trash class="size-5" aria-hidden="true" />
      </Button>
    </template>
  </SummaryRow>
</template>
