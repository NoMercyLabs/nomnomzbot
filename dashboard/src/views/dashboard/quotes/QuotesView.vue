<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// The channel's numbered quotes: search, add, edit, delete and a random pick. The server reports every change on
// the "quotes" config domain, so the list follows chat commands and other dashboards live.
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { fetchRandomQuote, useDeleteQuote, useQuotes, type Quote } from '@/api/quotes';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

import Plus from '~icons/nnz/add';
import Dices from '~icons/nnz/dices';

import QuoteDialog from './QuoteDialog.vue';
import QuoteRow from './QuoteRow.vue';

const pageSize = 50;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const search = ref('');
const typed = refDebounced(search, 300);
const limit = ref(pageSize);

const quotes = useQuotes(channelId, typed, limit);
const remove = useDeleteQuote(channelId);

const rows = computed(() => quotes.data.value?.quotes ?? []);
const hasMore = computed(() => quotes.data.value?.hasMore === true);
const listState = usePanelState(quotes, (page) =>
  page.quotes.length === 0);

const dialogOpen = ref(false);
const editing = ref<Quote | null>(null);

function openDialog(item: Quote | null): void {
  editing.value = item;
  dialogOpen.value = true;
}

const confirmOpen = ref(false);
const deleting = ref<Quote | null>(null);
const actionError = ref<string | undefined>(undefined);

function askDelete(item: Quote): void {
  actionError.value = undefined;
  deleting.value = item;
  confirmOpen.value = true;
}

function confirmDelete(): void {
  if (!deleting.value) return;

  remove.mutate(Number(deleting.value.number), {
    onSettled: (): void => {
      confirmOpen.value = false;
    },
    onError: (failure: Error): void => {
      actionError.value = errorText(failure, t);
    },
  });
}

const random = ref<Quote | null>(null);
const picking = ref(false);

async function pickRandom(): Promise<void> {
  actionError.value = undefined;
  picking.value = true;

  try {
    random.value = await fetchRandomQuote();
  } catch (failure) {
    random.value = null;
    actionError.value = errorText(failure, t);
  } finally {
    picking.value = false;
  }
}

function loadMore(): void {
  limit.value += pageSize;
}
</script>

<template>
  <PageHeader :title="t('dashboard.quotes.title')">
    <template #actions>
      <Button v-if="can('quotes:read')"
              type="button"
              variant="outline"
              size="sm"
              data-quote-random
              :disabled="picking"
              @click="pickRandom"
      >
        <Dices aria-hidden="true" />
        {{ t("dashboard.quotes.random") }}
      </Button>
      <Button v-if="can('quotes:write')"
              type="button"
              size="sm"
              data-quote-new
              @click="openDialog(null)"
      >
        <Plus aria-hidden="true" />
        {{ t("dashboard.quotes.new") }}
      </Button>
    </template>
  </PageHeader>
  <PageBody>
    <p v-if="actionError" role="alert" class="text-sm text-destructive">{{ actionError }}</p>
    <RowGroup v-if="random">
      <QuoteRow :quote="random" data-random-quote />
    </RowGroup>
    <Input
      v-model="search"
      type="search"
      data-quote-search
      :placeholder="t('dashboard.quotes.search')"
      :aria-label="t('dashboard.quotes.search')"
    />
    <PanelCard
      tier="lead"
      :title="t('dashboard.quotes.listTitle')"
      :state="listState"
      :empty-text="typed === '' ? t('dashboard.quotes.empty') : t('dashboard.quotes.noMatch')"
      :error-text="t('dashboard.quotes.error')"
      @retry="quotes.refetch()"
    >
      <QuoteRow
        v-for="item in rows"
        :key="item.id"
        :quote="item"
        actions
        data-quote-row
        @edit="openDialog(item)"
        @remove="askDelete(item)"
      />
      <div v-if="hasMore" class="pt-3">
        <Button type="button"
                variant="outline"
                size="sm"
                data-quote-more
                @click="loadMore"
        >
          {{ t("dashboard.quotes.more") }}
        </Button>
      </div>
    </PanelCard>
  </PageBody>
  <QuoteDialog v-model:open="dialogOpen" :quote="editing" />
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="t('dashboard.quotes.delete.title', { number: Number(deleting?.number ?? 0) })"
    :description="t('dashboard.quotes.delete.description')"
    :consequences="[t('dashboard.quotes.delete.line')]"
    :action="t('dashboard.quotes.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>
