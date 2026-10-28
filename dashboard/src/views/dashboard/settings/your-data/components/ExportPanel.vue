<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The right of access: one press downloads a JSON file of everything the bot holds about you.
import Download from '~icons/nnz/download';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useExportData, type DataExport } from '@/api/yourData';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { downloadText } from '@/lib/download';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const exporter = useExportData();

const last = ref<DataExport | null>(null);
const failure = ref<string | undefined>(undefined);

async function run(): Promise<void> {
  failure.value = undefined;

  try {
    const exported = await exporter.mutateAsync();
    const stamp = exported.generatedAt.slice(0, 10);

    downloadText(`nomnomzbot-my-data-${stamp}.json`, exported.document, 'application/json');
    last.value = exported;
  } catch (error) {
    failure.value = errorText(error, t);
  }
}
</script>

<template>
  <PanelCard
    data-panel="export"
    tier="lead"
    :title="$t('dashboard.settings.yourData.export.title')"
    :description="$t('dashboard.settings.yourData.export.description')"
  >
    <template #actions>
      <Button data-export :disabled="exporter.isPending.value" @click="run">
        <Download class="size-5" />
        {{ $t('dashboard.settings.yourData.export.action') }}
      </Button>
    </template>
    <p v-if="last" data-export-result class="px-3 py-3 text-sm text-muted-foreground">
      {{
        $t('dashboard.settings.yourData.export.result', {
          rows: Number(last.rowsAffected),
          size: Number(last.sizeBytes),
        })
      }}
    </p>
    <p v-if="failure" role="alert" class="px-3 py-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>
</template>
