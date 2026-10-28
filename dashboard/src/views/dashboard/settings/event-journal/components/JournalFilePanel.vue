<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The journal as a file: export saves it, import loads one back. Import adds only the events that are missing.
// A large journal takes the server minutes to write, so the export says it is working and for how long.
import { useIntervalFn } from '@vueuse/core';
import Download from '~icons/nnz/download';
import LoaderFade from '~icons/nnz/loader-fade';
import Upload from '~icons/nnz/upload';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useExportJournal,
  useImportJournal,
  type JournalFile,
  type JournalImportSummary,
} from '@/api/eventJournal';
import DateTime from '@/components/layout/DateTime.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { downloadText } from '@/lib/download';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const exporter = useExportJournal(channelId);
const importer = useImportJournal(channelId);

const picker = ref<HTMLInputElement | null>(null);
const exported = ref<JournalFile | null>(null);
const imported = ref<JournalImportSummary | null>(null);
const failure = ref<string | undefined>(undefined);
const waited = ref(0);
const ticker = useIntervalFn(() => {
  waited.value += 1;
}, 1000, { immediate: false });

async function save(): Promise<void> {
  failure.value = undefined;
  exported.value = null;
  waited.value = 0;
  ticker.resume();

  try {
    const file = await exporter.mutateAsync();
    const stamp = new Date().toISOString().slice(0, 10);

    downloadText(`event-journal-${channelId.value}-${stamp}.jsonl`, file.text, 'application/x-ndjson');
    exported.value = file;
  } catch (error) {
    failure.value = errorText(error, t);
  } finally {
    ticker.pause();
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
        v-if="can('eventstore:import')"
        ref="picker"
        data-import-file
        type="file"
        accept=".jsonl,application/x-ndjson"
        class="hidden"
        @change="load"
      >
      <Button
        v-if="can('eventstore:import')"
        variant="outline"
        data-import
        :disabled="importer.isPending.value"
        @click="picker?.click()"
      >
        <Upload class="size-5" />
        {{ $t('dashboard.settings.eventJournal.file.import') }}
      </Button>
      <Button v-if="can('eventstore:export')" data-export :disabled="exporter.isPending.value" @click="save">
        <Download class="size-5" />
        {{ $t('dashboard.settings.eventJournal.file.export') }}
      </Button>
    </template>
    <p
      v-if="exporter.isPending.value"
      data-export-progress
      role="status"
      class="flex items-center gap-2 px-3 py-3 text-sm text-muted-foreground"
    >
      <LoaderFade class="size-5 shrink-0 motion-safe:animate-spin" aria-hidden="true" />
      <span class="min-w-0 flex-1">{{ $t('dashboard.settings.eventJournal.file.exporting') }}</span>
      <DateTime :value="waited" variant="durationClock" class="tabular-nums" />
    </p>
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
