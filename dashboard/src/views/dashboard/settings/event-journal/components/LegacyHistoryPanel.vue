<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Copies the old bot's channel history into the journal. Running it again imports nothing twice.
import History from '~icons/nnz/rotate-left';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useImportLegacyHistory, type LegacyImportResult } from '@/api/eventJournal';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
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
      <Button variant="outline" data-import-legacy :disabled="importer.isPending.value" @click="run">
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
