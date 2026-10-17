<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// The chat games of the channel: the game that runs now (with a cancel that refunds), the games the bot can
// start, the finished games, and the mini-games with their on/off switch.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useActiveGame,
  useCancelGame,
  useGameCatalog,
  useGameConfigs,
  useGameHistory,
  useSetGameEnabled,
  useStartGame,
  type GameCatalogEntry,
  type GameConfig,
  type GameSession,
} from '@/api/games';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import Play from '~icons/nnz/play';

import { sessionTone, statusKey } from './gameStatus';

const { t, n, locale } = useI18n();
const { channelId } = useCurrentChannel();

const catalog = useGameCatalog(channelId);
const active = useActiveGame(channelId);
const history = useGameHistory(channelId);
const configs = useGameConfigs(channelId);

const start = useStartGame(channelId);
const cancel = useCancelGame(channelId);
const setEnabled = useSetGameEnabled(channelId);

const catalogState = usePanelState(catalog, (rows) =>
  rows.length === 0);
const activeState = usePanelState(active, (session) =>
  session === null);
const historyState = usePanelState(history, (rows) =>
  rows.length === 0);
const configState = usePanelState(configs, (rows) =>
  rows.length === 0);

const confirming = ref(false);
const runningGame = computed(() => active.data.value ?? null);
const finished = computed(() =>
  (history.data.value ?? []).filter((row) => row.id !== runningGame.value?.id));

function catalogDetail(entry: GameCatalogEntry): string {
  const parts = [
    t('dashboard.games.catalog.playerRange', {
      min: n(Number(entry.minPlayers)),
      max: n(Number(entry.maxPlayers)),
    }),
    t('dashboard.games.catalog.lobby', { seconds: n(Number(entry.lobbyWindowSeconds)) }),
  ];

  if (entry.requiresEntryFee) {
    parts.push(t('dashboard.games.catalog.entryFee'));
  }

  return parts.join(' · ');
}

const nameOf = (gameType: string): string =>
  catalog.data.value?.find((entry) => entry.gameKey === gameType)?.displayName ?? gameType;

function statusLabel(session: GameSession): string {
  return t(`dashboard.games.status.${statusKey(session.status)}`, session.status);
}

function when(timestamp: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(timestamp));
}

function players(session: GameSession): string {
  const count = Number(session.participantCount);

  return t('dashboard.games.active.players', { count: n(count) }, count);
}

async function cancelRunning(): Promise<void> {
  if (runningGame.value === null) return;

  await cancel.mutateAsync(runningGame.value.id);
  confirming.value = false;
}

function toggle(config: GameConfig, enabled: boolean): void {
  setEnabled.mutate({ config, enabled });
}
</script>

<template>
  <PageHeader :title="t('dashboard.games.title')" />
  <PageBody>
    <PanelCard
      tier="lead"
      :title="t('dashboard.games.active.title')"
      :state="activeState"
      :empty-text="t('dashboard.games.active.empty')"
      :error-text="t('dashboard.games.active.error')"
      @retry="active.refetch()"
    >
      <div v-if="runningGame" data-active-game class="flex flex-wrap items-center gap-3">
        <span class="font-medium">{{ nameOf(runningGame.gameType) }}</span>
        <StatusBadge :tone="sessionTone(runningGame.status)" :label="statusLabel(runningGame)" />
        <span class="text-sm text-muted-foreground">{{ players(runningGame) }}</span>
        <Button
          data-cancel-game
          class="ms-auto"
          variant="outline"
          @click="confirming = true"
        >
          {{ t('dashboard.games.active.cancel') }}
        </Button>
      </div>
    </PanelCard>

    <PanelCard
      :title="t('dashboard.games.catalog.title')"
      :state="catalogState"
      :empty-text="t('dashboard.games.catalog.empty')"
      :error-text="t('dashboard.games.catalog.error')"
      @retry="catalog.refetch()"
    >
      <ul class="divide-y">
        <li
          v-for="entry in catalog.data.value"
          :key="entry.gameKey"
          class="flex flex-wrap items-center gap-3 py-3 first:pt-0 last:pb-0"
        >
          <div class="min-w-0 flex-1">
            <span class="block font-medium">{{ entry.displayName }}</span>
            <span class="block text-sm text-muted-foreground">{{ catalogDetail(entry) }}</span>
            <span class="block text-sm text-muted-foreground">
              {{ t('dashboard.games.catalog.keywords', { words: entry.inputKeywords.join(', ') }) }}
            </span>
          </div>
          <Button
            :data-start-game="entry.gameKey"
            variant="outline"
            :disabled="runningGame !== null || start.isPending.value"
            @click="start.mutate(entry.gameKey)"
          >
            <Play class="size-5" aria-hidden="true" />
            {{ t('dashboard.games.catalog.start') }}
          </Button>
        </li>
      </ul>
    </PanelCard>

    <PanelCard
      :title="t('dashboard.games.configs.title')"
      :state="configState"
      :empty-text="t('dashboard.games.configs.empty')"
      :error-text="t('dashboard.games.configs.error')"
      @retry="configs.refetch()"
    >
      <div
        v-for="config in configs.data.value"
        :key="config.id"
        :data-game-config="config.gameType"
      >
        <SettingRow
          :model-value="config.isEnabled"
          :title="nameOf(config.gameType)"
          :description="config.requires18Plus ? t('dashboard.games.configs.age') : undefined"
          :disabled="setEnabled.isPending.value"
          @update:model-value="toggle(config, $event)"
        />
      </div>
      <p class="px-3 pt-2 text-sm text-muted-foreground">
        {{ t('dashboard.games.configs.unavailable') }}
      </p>
    </PanelCard>

    <PanelCard
      tier="quiet"
      :title="t('dashboard.games.history.title')"
      :state="historyState"
      :empty-text="t('dashboard.games.history.empty')"
      :error-text="t('dashboard.games.history.error')"
      @retry="history.refetch()"
    >
      <ul class="divide-y">
        <li
          v-for="session in finished"
          :key="session.id"
          class="flex flex-wrap items-baseline gap-x-3 gap-y-1 py-2 first:pt-0 last:pb-0"
        >
          <span class="font-medium">{{ nameOf(session.gameType) }}</span>
          <span class="text-sm text-muted-foreground">{{ players(session) }}</span>
          <span class="text-sm text-muted-foreground tabular-nums">{{ when(session.startedAt) }}</span>
          <StatusBadge class="ms-auto" :tone="sessionTone(session.status)" :label="statusLabel(session)" />
        </li>
      </ul>
    </PanelCard>

    <ConfirmDialog
      v-model:open="confirming"
      :title="t('dashboard.games.active.cancelTitle')"
      :description="t('dashboard.games.active.cancelBody')"
      :consequences="[t('dashboard.games.active.cancelRefund')]"
      :action="t('dashboard.games.active.cancelConfirm')"
      :pending="cancel.isPending.value"
      @confirm="cancelRunning"
    />
  </PageBody>
</template>
