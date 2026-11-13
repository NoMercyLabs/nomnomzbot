<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// One sound clip as a row: its name, the chat word that plays it, the volume and the length. It ends with the on/off
// switch, Edit and Delete, each behind its own permission.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { SoundClip } from '@/api/soundClips';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCan } from '@/composables/useCan';

const props = defineProps<{ clip: SoundClip; pending: boolean }>();

defineEmits<{
  edit: [];
  remove: [];
  toggle: [enabled: boolean];
}>();

const { t, n } = useI18n();
const { can } = useCan();

const detail = computed<string>(() =>
  [
    props.clip.name,
    props.clip.triggerWord
      ? t('dashboard.sounds.trigger', { word: props.clip.triggerWord })
      : '',
    t('dashboard.sounds.volume', { volume: n(Number(props.clip.defaultVolume)) }),
    t('dashboard.sounds.seconds', { seconds: n(Number(props.clip.durationMs) / 1000, { maximumFractionDigits: 1 }) }),
  ]
    .filter(Boolean)
    .join(' · '));
</script>

<template>
  <SummaryRow :title="clip.displayName" :detail="detail">
    <template #trailing>
      <GatedSwitch
        action="sounds:write"
        data-sound-enabled
        :model-value="clip.isEnabled"
        :disabled="pending"
        :aria-label="t('dashboard.sounds.enabled', { name: clip.displayName })"
        @update:model-value="$emit('toggle', $event)"
      />
      <RowEditDelete
        :edit-label="t('dashboard.sounds.edit', { name: clip.displayName })"
        :delete-label="t('dashboard.sounds.delete.title', { name: clip.displayName })"
        :editable="can('sounds:write')"
        :deletable="can('sounds:write')"
        @edit="$emit('edit')"
        @delete="$emit('remove')"
      />
    </template>
  </SummaryRow>
</template>
