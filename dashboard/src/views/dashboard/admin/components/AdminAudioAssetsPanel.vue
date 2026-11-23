<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The platform audio library: the audio files no channel owns, which sound templates name. An operator lists
// them, uploads one (mp3, ogg or wav) and deletes one that no template uses. The server refuses a delete while
// a template names the file, so such a row shows what uses it instead of a delete button.
import Upload from '~icons/nnz/upload';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useAudioAssets,
  useDeleteAudioAsset,
  useUploadAudioAsset,
  type AudioAsset,
} from '@/api/adminContent';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { errorText } from '@/lib/errors';
import { formatBytes } from '@/lib/number';

const { t, locale } = useI18n();
const { can } = usePlatformCan();

const assets = useAudioAssets();
const upload = useUploadAudioAsset();
const remove = useDeleteAudioAsset();

const listState = usePanelState(assets, (rows) =>
  rows.length === 0);

const picker = ref<HTMLInputElement | null>(null);
const deleting = ref<AudioAsset | null>(null);
const failure = ref<string | undefined>(undefined);

async function pickFile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];

  if (file === undefined) return;

  failure.value = undefined;

  try {
    await upload.mutateAsync(file);
  } catch (error) {
    failure.value = errorText(error, t);
  } finally {
    input.value = '';
  }
}

async function confirmDelete(): Promise<void> {
  const target = deleting.value;

  if (target === null) return;

  failure.value = undefined;

  try {
    await remove.mutateAsync(target.id);
  } catch (error) {
    failure.value = errorText(error, t);
  } finally {
    deleting.value = null;
  }
}
</script>

<template>
  <PanelCard
    data-panel="audio-assets"
    :title="$t('dashboard.admin.content.audio.title')"
    :description="$t('dashboard.admin.content.audio.description')"
    :state="listState"
    :empty-text="$t('dashboard.admin.content.audio.empty')"
    :error-text="$t('dashboard.admin.content.audio.error')"
    @retry="assets.refetch()"
  >
    <template v-if="can('content:author')" #actions>
      <input
        ref="picker"
        data-audio-file
        type="file"
        accept=".mp3,.ogg,.wav,audio/mpeg,audio/ogg,audio/wav"
        class="hidden"
        @change="pickFile"
      >
      <Button
        variant="outline"
        data-audio-upload
        :disabled="upload.isPending.value"
        @click="picker?.click()"
      >
        <Upload class="size-5" />
        {{ $t("dashboard.admin.content.audio.upload") }}
      </Button>
    </template>
    <InlineNotice v-if="failure"
                  tone="error"
                  compact
                  class="mb-2"
                  data-audio-failure
    >
      {{ failure }}
    </InlineNotice>
    <RowGroup>
      <SummaryRow
        v-for="row in assets.data.value ?? []"
        :key="row.id"
        data-audio-row
        truncate
        :title="row.displayName"
        :detail="row.fileName"
        :meta="formatBytes(Number(row.sizeBytes), locale)"
      >
        <template v-if="row.usedByTemplates.length > 0" #end>
          <Badge variant="outline">{{
            $t("dashboard.admin.content.audio.usedBy", { count: row.usedByTemplates.length })
          }}</Badge>
        </template>
        <template #trailing>
          <RowEditDelete
            v-if="can('content:author')"
            :editable="false"
            :deletable="row.usedByTemplates.length === 0"
            :edit-label="row.displayName"
            :delete-label="$t('dashboard.admin.content.audio.delete.open', { name: row.displayName })"
            @delete="deleting = row"
          />
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
  <ConfirmDialog
    :open="deleting !== null"
    :title="$t('dashboard.admin.content.audio.delete.title', { name: deleting?.displayName ?? '' })"
    :description="$t('dashboard.admin.content.audio.delete.description')"
    :action="$t('dashboard.admin.content.audio.delete.action')"
    :pending="remove.isPending.value"
    @update:open="deleting = null"
    @confirm="confirmDelete"
  />
</template>
