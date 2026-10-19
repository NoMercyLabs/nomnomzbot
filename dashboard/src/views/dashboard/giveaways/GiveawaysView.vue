<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// The channel's giveaways with the steps that fit each status (open, close, draw), and the winners of the
// giveaway in focus with a redraw per winner, its entries, and the code pools with a dialog to make one.
// The server pushes GiveawayChanged; liveSync.ts reads the lists again, so this page never polls.
import { computed, ref, type Component } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchGiveawayBlastRadius,
  useCodePools,
  useDeleteGiveaway,
  useGiveawayAction,
  useGiveawayEntries,
  useGiveawayWinners,
  useGiveaways,
  useRedrawWinner,
  type Giveaway,
  type GiveawayAction,
  type GiveawayBlastRadius,
  type GiveawayWinner,
} from '@/api/giveaways';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

import Plus from '~icons/nnz/add';
import Dices from '~icons/nnz/dices';
import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';
import Lock from '~icons/nnz/locked';
import Play from '~icons/nnz/play';
import Trophy from '~icons/nnz/trophy';
import Rotate from '~icons/nnz/rotate';

import CodePoolDialog from './CodePoolDialog.vue';
import GiveawayDialog from './GiveawayDialog.vue';
import { actionsFor, focusGiveaway, statusTone } from './giveawayStatus';

const { t, te, n } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const giveaways = useGiveaways(channelId);
const focus = computed(() => focusGiveaway(giveaways.data.value ?? []));
const focusId = computed(() => focus.value?.id ?? null);
const winners = useGiveawayWinners(channelId, focusId);
const change = useGiveawayAction(channelId);
const redraw = useRedrawWinner(channelId, focusId);

const listState = usePanelState(giveaways, (rows) =>
  rows.length === 0);
const winnersState = usePanelState(winners, (rows) =>
  rows.length === 0);
const entries = useGiveawayEntries(channelId, focusId);
const entriesState = usePanelState(entries, (rows) =>
  rows.length === 0);
const pools = useCodePools(channelId);
const poolsState = usePanelState(pools, (rows) =>
  rows.length === 0);
const remove = useDeleteGiveaway(channelId);

const dialogOpen = ref(false);
const poolDialogOpen = ref(false);
const editing = ref<Giveaway | null>(null);

function openDialog(item: Giveaway | null): void {
  editing.value = item;
  dialogOpen.value = true;
}

// Only a giveaway nobody can enter right now is edited; an open or drawn one keeps its rules.
function canEdit(item: Giveaway): boolean {
  return item.status === 'draft' || item.status === 'closed';
}

const confirmOpen = ref(false);
const deleting = ref<Giveaway | null>(null);
const consequences = ref<string[]>([]);
const deleteError = ref<string | undefined>(undefined);

function categoryLabel(key: string): string {
  return te(key)
    ? t(key)
    : (key.split('.').pop() ?? key).replaceAll('_', ' ');
}

function blastLines(blast: GiveawayBlastRadius): string[] {
  const lines: string[] = blast.categories
    .filter((category) => Number(category.count) > 0)
    .map((category) =>
      t(
        blast.isMinimum
          ? 'dashboard.giveaways.delete.lineAtLeast'
          : 'dashboard.giveaways.delete.line',
        { count: Number(category.count), what: categoryLabel(category.categoryKey) },
      ),
    );

  return lines.length > 0
    ? lines
    : [t('dashboard.giveaways.delete.nothing')];
}

async function askDelete(item: Giveaway): Promise<void> {
  deleteError.value = undefined;

  try {
    consequences.value = blastLines(await fetchGiveawayBlastRadius(item.id));
    deleting.value = item;
    confirmOpen.value = true;
  } catch (failure) {
    deleteError.value = errorText(failure, t);
  }
}

function confirmDelete(): void {
  if (!deleting.value) return;

  remove.mutate(deleting.value.id, {
    onSettled: (): void => {
      confirmOpen.value = false;
    },
    onError: (failure: Error): void => {
      deleteError.value = errorText(failure, t);
    },
  });
}

const icons = {
  open: Play,
  close: Lock,
  draw: Dices,
} as const satisfies Record<GiveawayAction, Component>;

function statusLabel(status: string): string {
  return t(`dashboard.giveaways.status.${status}`, status);
}

function winnerStatus(winner: GiveawayWinner): string {
  return t(`dashboard.giveaways.winnerStatus.${winner.status}`, winner.status);
}

function entryMode(item: Giveaway): string {
  return item.keyword
    ? t('dashboard.giveaways.keyword', { keyword: item.keyword })
    : t('dashboard.giveaways.viewerEntryMode');
}

function summary(item: Giveaway): string {
  const entries = Number(item.entryCount);
  const count = Number(item.winnerCount);

  return [
    t('dashboard.giveaways.entries', { count: n(entries) }, entries),
    t('dashboard.giveaways.winnerCount', { count: n(count) }, count),
    entryMode(item),
  ].join(' · ');
}

// The step the page leads with is the last one of the status; the earlier steps stay quiet.
function variantOf(item: Giveaway, action: GiveawayAction): 'default' | 'outline' {
  const steps = actionsFor(item.status);

  return steps[steps.length - 1] === action
    ? 'default'
    : 'outline';
}
</script>

<template>
  <PageHeader :title="t('dashboard.giveaways.title')">
    <template #actions>
      <Button
        v-if="can('giveaways:write')"
        type="button"
        variant="outline"
        size="sm"
        data-giveaway-new
        @click="openDialog(null)"
      >
        <Plus aria-hidden="true" />
        {{ t("dashboard.giveaways.new") }}
      </Button>
    </template>
  </PageHeader>
  <PageBody>
    <p v-if="deleteError" role="alert" class="text-sm text-destructive">{{ deleteError }}</p>
    <PanelCard
      v-if="can('giveaways:read')"
      tier="lead"
      :title="t('dashboard.giveaways.listTitle')"
      :state="listState"
      :empty-text="t('dashboard.giveaways.empty')"
      :error-text="t('dashboard.giveaways.error')"
      @retry="giveaways.refetch()"
    >
      <ul class="divide-y">
        <li
          v-for="item in giveaways.data.value"
          :key="item.id"
          data-giveaway-row
          class="flex flex-wrap items-center gap-x-3 gap-y-2 py-3 first:pt-0 last:pb-0"
        >
          <div class="flex min-w-0 flex-1 flex-col gap-0.5">
            <span class="truncate font-medium">{{ item.title }}</span>
            <span class="text-sm text-muted-foreground tabular-nums">{{ summary(item) }}</span>
          </div>
          <StatusBadge :tone="statusTone(item.status)" :label="statusLabel(item.status)" />
          <div v-if="can('giveaways:write')" class="flex items-center gap-2">
            <Button
              v-for="action in actionsFor(item.status)"
              :key="action"
              size="sm"
              :variant="variantOf(item, action)"
              :data-giveaway-action="action"
              :disabled="change.isPending.value"
              @click="change.mutate({ id: item.id, action })"
            >
              <component :is="icons[action]" class="size-4" />
              {{ t(`dashboard.giveaways.action.${action}`) }}
            </Button>
            <Button
              v-if="canEdit(item)"
              variant="ghost"
              size="icon"
              data-giveaway-edit
              :aria-label="t('dashboard.giveaways.edit')"
              :title="t('dashboard.giveaways.edit')"
              @click="openDialog(item)"
            >
              <Edit class="size-5" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              data-giveaway-delete
              :aria-label="t('dashboard.giveaways.delete.title', { title: item.title })"
              :title="t('dashboard.giveaways.delete.title', { title: item.title })"
              @click="askDelete(item)"
            >
              <Trash class="size-5" />
            </Button>
          </div>
        </li>
      </ul>
    </PanelCard>
    <PanelCard
      v-if="focus && can('giveaways:read')"
      :title="t('dashboard.giveaways.winnersTitle', { title: focus.title })"
      :state="winnersState"
      :empty-text="t('dashboard.giveaways.winnersEmpty')"
      :error-text="t('dashboard.giveaways.winnersError')"
      @retry="winners.refetch()"
    >
      <ul class="divide-y">
        <li
          v-for="winner in winners.data.value"
          :key="winner.id"
          data-winner-row
          class="flex items-center gap-3 py-2 first:pt-0 last:pb-0"
        >
          <Trophy class="size-4 flex-none text-muted-foreground" aria-hidden="true" />
          <span class="min-w-0 flex-1 truncate font-medium">{{ winner.viewerDisplayName }}</span>
          <span v-if="winner.isRedraw" class="text-sm text-muted-foreground">{{
            t("dashboard.giveaways.redrawn")
          }}</span>
          <StatusBadge tone="off" :label="winnerStatus(winner)" />
          <Button
            v-if="can('giveaways:write')"
            variant="ghost"
            size="icon"
            data-winner-redraw
            :aria-label="t('dashboard.giveaways.redraw')"
            :title="t('dashboard.giveaways.redraw')"
            :disabled="redraw.isPending.value"
            @click="redraw.mutate(winner.id)"
          >
            <Rotate class="size-5" />
          </Button>
        </li>
      </ul>
    </PanelCard>
    <PanelCard
      v-if="focus && can('giveaways:read')"
      :title="t('dashboard.giveaways.entryList.title', { title: focus.title })"
      :state="entriesState"
      :empty-text="t('dashboard.giveaways.entryList.empty')"
      :error-text="t('dashboard.giveaways.entryList.error')"
      @retry="entries.refetch()"
    >
      <ul class="divide-y">
        <li
          v-for="entry in entries.data.value"
          :key="entry.id"
          data-entry-row
          class="flex items-center gap-3 py-2 first:pt-0 last:pb-0"
        >
          <span class="min-w-0 flex-1 truncate font-medium">{{ entry.viewerDisplayName }}</span>
          <span class="text-sm text-muted-foreground tabular-nums">{{
            t(
              "dashboard.giveaways.entryList.tickets",
              { count: n(Number(entry.ticketCount)) },
              Number(entry.ticketCount),
            )
          }}</span>
        </li>
      </ul>
    </PanelCard>
    <PanelCard
      v-if="can('giveaways:codes:write')"
      :title="t('dashboard.giveaways.pools.title')"
      :state="poolsState"
      :empty-text="t('dashboard.giveaways.pools.empty')"
      :error-text="t('dashboard.giveaways.pools.error')"
      @retry="pools.refetch()"
    >
      <ul class="divide-y">
        <li
          v-for="pool in pools.data.value"
          :key="pool.id"
          data-pool-row
          class="flex items-center gap-3 py-2 first:pt-0 last:pb-0"
        >
          <span class="min-w-0 flex-1 truncate font-medium">{{ pool.name }}</span>
          <span class="text-sm text-muted-foreground tabular-nums">{{
            t("dashboard.giveaways.pools.counts", {
              available: n(Number(pool.available)),
              total: n(Number(pool.total)),
            })
          }}</span>
        </li>
      </ul>
      <template #actions>
        <Button
          type="button"
          variant="outline"
          size="sm"
          data-pool-new
          @click="poolDialogOpen = true"
        >
          <Plus aria-hidden="true" />
          {{ t("dashboard.giveaways.pools.new") }}
        </Button>
      </template>
    </PanelCard>
  </PageBody>
  <GiveawayDialog v-model:open="dialogOpen" :giveaway="editing" />
  <CodePoolDialog v-model:open="poolDialogOpen" />
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="t('dashboard.giveaways.delete.title', { title: deleting?.title ?? '' })"
    :description="t('dashboard.giveaways.delete.description')"
    :consequences="consequences"
    :action="t('dashboard.giveaways.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>
