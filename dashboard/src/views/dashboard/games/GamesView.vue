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
import DateTime from '@/components/layout/DateTime.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import Edit from '~icons/nnz/edit';
import Play from '~icons/nnz/play';

import GameConfigDialog from './components/GameConfigDialog.vue';
import { sessionTone, statusKey } from './gameStatus';

const { t, n } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

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
const editing = ref<GameConfig | null>(null);
const editOpen = computed({
  get: () => editing.value !== null,
  set: (value: boolean) => {
    if (!value) {
      editing.value = null;
    }
  },
});
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
      v-if="can('games:session:read')"
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
          v-if="can('games:session:cancel')"
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
      v-if="can('games:session:read')"
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
            v-if="can('games:session:start')"
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
      v-if="can('economy:games:read')"
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
        <div class="flex items-center gap-2">
          <SettingRow
            class="flex-1"
            :model-value="config.isEnabled"
            :title="nameOf(config.gameType)"
            :description="config.requires18Plus ? t('dashboard.games.configs.age') : undefined"
            :disabled="setEnabled.isPending.value"
            :readonly="!can('economy:games:write')"
            @update:model-value="toggle(config, $event)"
          />
          <Button
            v-if="can('economy:games:write')"
            :data-edit-game="config.gameType"
            variant="ghost"
            size="icon"
            :aria-label="t('dashboard.games.configs.edit', { name: nameOf(config.gameType) })"
            @click="editing = config"
          >
            <Edit class="size-5" aria-hidden="true" />
          </Button>
        </div>
      </div>
    </PanelCard>

    <PanelCard
      v-if="can('games:session:read')"
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
          <span class="text-sm text-muted-foreground tabular-nums"><DateTime :value="session.startedAt" variant="dateTime" /></span>
          <StatusBadge class="ms-auto" :tone="sessionTone(session.status)" :label="statusLabel(session)" />
        </li>
      </ul>
    </PanelCard>

    <GameConfigDialog
      v-model:open="editOpen"
      :config="editing"
      :name="editing === null ? '' : nameOf(editing.gameType)"
    />

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
