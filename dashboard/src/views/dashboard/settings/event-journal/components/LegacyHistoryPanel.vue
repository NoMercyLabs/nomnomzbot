<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Copies the old bot's channel history into the journal. Running it again imports nothing twice.
import History from '~icons/nnz/rotate-left';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useImportLegacyHistory, type LegacyImportResult } from '@/api/eventJournal';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const importer = useImportLegacyHistory(channelId);

const result = ref<LegacyImportResult | null>(null);
const failure = ref<string | undefined>(undefined);

const skippedTypes = computed(() =>
  Object.entries(result.value?.skippedByLegacyType ?? {}));

async function run(): Promise<void> {
  failure.value = undefined;

  try {
    result.value = await importer.mutateAsync();
  } catch (error) {
    failure.value = errorText(error, t);
  }
}
</script>

<template>
  <PanelCard
    data-panel="legacy-history"
    :title="$t('dashboard.settings.eventJournal.legacy.title')"
    :description="$t('dashboard.settings.eventJournal.legacy.description')"
  >
    <template #actions>
      <Button v-if="can('eventstore:import:legacy')"
              variant="outline"
              data-import-legacy
              :disabled="importer.isPending.value"
              @click="run"
      >
        <History class="size-5" />
        {{ $t('dashboard.settings.eventJournal.legacy.import') }}
      </Button>
    </template>
    <div v-if="result" data-legacy-result class="px-3 py-3 text-sm text-muted-foreground">
      <p>
        {{
          $t('dashboard.settings.eventJournal.legacy.imported', {
            total: Number(result.totalRead),
            imported: Number(result.imported),
            duplicate: Number(result.skippedDuplicate),
            unmapped: Number(result.skippedUnmapped),
            before: Number(result.headBefore),
            after: Number(result.headAfter),
          })
        }}
      </p>
      <ul>
        <li v-for="[type, count] in skippedTypes" :key="type">
          {{ $t('dashboard.settings.eventJournal.legacy.skippedType', { type, count: Number(count) }) }}
        </li>
      </ul>
    </div>
    <p v-if="failure" role="alert" class="px-3 py-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>
</template>
