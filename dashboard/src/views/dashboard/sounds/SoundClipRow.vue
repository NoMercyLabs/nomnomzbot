<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// One sound clip as a row: its name, the chat word that plays it, the volume and the length. It ends with the on/off
// switch, Edit and Delete, each behind its own permission.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { SoundClip } from '@/api/soundClips';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';

import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';

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
      <Button
        v-if="can('sounds:write')"
        variant="ghost"
        size="icon"
        data-sound-edit
        :aria-label="t('dashboard.sounds.edit', { name: clip.displayName })"
        :title="t('dashboard.sounds.edit', { name: clip.displayName })"
        @click="$emit('edit')"
      >
        <Edit class="size-5" aria-hidden="true" />
      </Button>
      <Button
        v-if="can('sounds:write')"
        variant="ghost"
        size="icon"
        data-sound-delete
        :aria-label="t('dashboard.sounds.delete.title', { name: clip.displayName })"
        :title="t('dashboard.sounds.delete.title', { name: clip.displayName })"
        @click="$emit('remove')"
      >
        <Trash class="size-5" aria-hidden="true" />
      </Button>
    </template>
  </SummaryRow>
</template>
