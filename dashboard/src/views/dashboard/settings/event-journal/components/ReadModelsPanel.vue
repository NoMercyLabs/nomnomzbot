<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The pages built from the journal. Replay runs imported events through the bot; rebuild clears those pages and
// fills them again, so it asks first.
import Database from '~icons/nnz/data-base-refresh';
import Replay from '~icons/nnz/rotate-right';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useRebuildProjections,
  useReplayImportedEvents,
  type ProjectionRebuild,
} from '@/api/eventJournal';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const replay = useReplayImportedEvents(channelId);
const rebuild = useRebuildProjections(channelId);

const replayed = ref<number | null>(null);
const rebuilt = ref<ProjectionRebuild[] | null>(null);
const failure = ref<string | undefined>(undefined);
const confirmOpen = ref(false);

async function runReplay(): Promise<void> {
  failure.value = undefined;

  try {
    replayed.value = await replay.mutateAsync();
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

async function runRebuild(): Promise<void> {
  failure.value = undefined;

  try {
    rebuilt.value = await rebuild.mutateAsync();
  } catch (error) {
    failure.value = errorText(error, t);
  } finally {
    confirmOpen.value = false;
  }
}
</script>

<template>
  <PanelCard
    data-panel="read-models"
    :title="$t('dashboard.settings.eventJournal.readModels.title')"
    :description="$t('dashboard.settings.eventJournal.readModels.description')"
  >
    <RowGroup>
      <div class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
        <p class="min-w-0 flex-1 text-sm text-muted-foreground">
          {{ $t('dashboard.settings.eventJournal.readModels.replayHelp') }}
        </p>
        <Button v-if="can('eventstore:replay:write')"
                variant="outline"
                data-replay
                :disabled="replay.isPending.value"
                @click="runReplay"
        >
          <Replay class="size-5" />
          {{ $t('dashboard.settings.eventJournal.readModels.replay') }}
        </Button>
      </div>
      <div class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
        <p class="min-w-0 flex-1 text-sm text-muted-foreground">
          {{ $t('dashboard.settings.eventJournal.readModels.rebuildHelp') }}
        </p>
        <Button v-if="can('eventstore:projection:rebuild')"
                variant="outline"
                data-rebuild
                :disabled="rebuild.isPending.value"
                @click="confirmOpen = true"
        >
          <Database class="size-5" />
          {{ $t('dashboard.settings.eventJournal.readModels.rebuild') }}
        </Button>
      </div>
    </RowGroup>
    <p v-if="replayed !== null" data-replay-result class="px-3 py-3 text-sm text-muted-foreground">
      {{ $t('dashboard.settings.eventJournal.readModels.replayed', { count: replayed }) }}
    </p>
    <ul v-if="rebuilt" data-rebuild-result class="px-3 py-3 text-sm text-muted-foreground">
      <li v-for="row in rebuilt" :key="row.projectionName">
        {{
          $t('dashboard.settings.eventJournal.readModels.rebuilt', {
            name: row.projectionName,
            count: Number(row.eventsApplied),
          })
        }}
      </li>
    </ul>
    <p v-if="failure" role="alert" class="px-3 py-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.settings.eventJournal.readModels.confirmTitle')"
    :description="$t('dashboard.settings.eventJournal.readModels.confirmDescription')"
    :consequences="[$t('dashboard.settings.eventJournal.readModels.confirmEffect')]"
    :action="$t('dashboard.settings.eventJournal.readModels.confirmAction')"
    :pending="rebuild.isPending.value"
    @confirm="runRebuild"
  />
</template>
