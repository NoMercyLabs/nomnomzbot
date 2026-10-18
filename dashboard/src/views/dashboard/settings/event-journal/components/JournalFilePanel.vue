<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The journal as a file: export saves it, import loads one back. Import adds only the events that are missing.
import Download from '~icons/nnz/download';
import Upload from '~icons/nnz/upload';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useExportJournal,
  useImportJournal,
  type JournalFile,
  type JournalImportSummary,
} from '@/api/eventJournal';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { downloadText } from '@/lib/download';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const exporter = useExportJournal(channelId);
const importer = useImportJournal(channelId);

const picker = ref<HTMLInputElement | null>(null);
const exported = ref<JournalFile | null>(null);
const imported = ref<JournalImportSummary | null>(null);
const failure = ref<string | undefined>(undefined);

async function save(): Promise<void> {
  failure.value = undefined;

  try {
    const file = await exporter.mutateAsync();
    const stamp = new Date().toISOString().slice(0, 10);

    downloadText(`event-journal-${channelId.value}-${stamp}.jsonl`, file.text, 'application/x-ndjson');
    exported.value = file;
  } catch (error) {
    failure.value = errorText(error, t);
  }
}

async function load(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];

  if (file === undefined) return;

  failure.value = undefined;

  try {
    imported.value = await importer.mutateAsync(file);
  } catch (error) {
    failure.value = errorText(error, t);
  } finally {
    input.value = '';
  }
}
</script>

<template>
  <PanelCard
    data-panel="journal-file"
    tier="lead"
    :title="$t('dashboard.settings.eventJournal.file.title')"
    :description="$t('dashboard.settings.eventJournal.file.description')"
  >
    <template #actions>
      <input
        ref="picker"
        data-import-file
        type="file"
        accept=".jsonl,application/x-ndjson"
        class="hidden"
        @change="load"
      >
      <Button
        variant="outline"
        data-import
        :disabled="importer.isPending.value"
        @click="picker?.click()"
      >
        <Upload class="size-5" />
        {{ $t('dashboard.settings.eventJournal.file.import') }}
      </Button>
      <Button data-export :disabled="exporter.isPending.value" @click="save">
        <Download class="size-5" />
        {{ $t('dashboard.settings.eventJournal.file.export') }}
      </Button>
    </template>
    <p v-if="exported" data-export-result class="px-3 py-3 text-sm text-muted-foreground">
      {{ $t('dashboard.settings.eventJournal.file.exported', { count: exported.lines }) }}
    </p>
    <p v-if="imported" data-import-result class="px-3 py-3 text-sm text-muted-foreground">
      {{
        $t('dashboard.settings.eventJournal.file.imported', {
          total: Number(imported.totalLines),
          imported: Number(imported.imported),
          duplicate: Number(imported.skippedDuplicate),
          upcast: Number(imported.upcast),
        })
      }}
    </p>
    <p v-if="failure" role="alert" class="px-3 py-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>
</template>
