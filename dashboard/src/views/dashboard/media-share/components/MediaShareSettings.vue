<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What the bot accepts. Edits stay in a draft until Save; Discard drops the draft without a request.
// The bot has no partial update, so Save sends the whole config.
import type { UseQueryReturnType } from '@tanstack/vue-query';
import { computed, ref, watch } from 'vue';

import { useSaveMediaShareConfig, type MediaShareConfig } from '@/api/mediaShare';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { usePanelState } from '@/composables/usePanelState';

const props = defineProps<{
  query: UseQueryReturnType<MediaShareConfig | null, Error>;
  channelId: string | null;
}>();

const { can } = useCan();
const save = useSaveMediaShareConfig(() => props.channelId);
const state = usePanelState(
  props.query,
  (config: MediaShareConfig | null): boolean => config === null,
);

const draft = ref<MediaShareConfig | null>(null);

watch(
  () => props.query.data.value,
  (config: MediaShareConfig | null | undefined) => {
    draft.value = config
      ? { ...config }
      : null;
  },
  { immediate: true },
);

const switches = ['isEnabled', 'requireApproval', 'allowTwitchClips', 'allowYouTube'] as const;
const numbers = [
  'maxDurationSeconds',
  'entryCost',
  'maxQueueLength',
  'perUserCooldownSeconds',
] as const;

const dirty = computed<boolean>(() => {
  const saved = props.query.data.value;

  return (
    saved !== null &&
    saved !== undefined &&
    draft.value !== null &&
    JSON.stringify(saved) !== JSON.stringify(draft.value)
  );
});

function setNumber(key: (typeof numbers)[number], value: string | number): void {
  if (draft.value === null) return;

  draft.value[key] = Number(value);
}

function discard(): void {
  const saved = props.query.data.value;

  draft.value = saved
    ? { ...saved }
    : null;
}

function submit(): void {
  if (draft.value === null) return;

  save.mutate(draft.value);
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.mediaShare.config.title')"
    :state="state"
    :error-text="$t('dashboard.mediaShare.config.error')"
    @retry="query.refetch()"
  >
    <div v-if="draft" data-media-share-settings class="flex flex-col gap-3">
      <RowGroup>
        <SettingRow
          v-for="key in switches"
          :key="key"
          v-model="draft[key]"
          :disabled="!can('media:write')"
          :title="$t(`dashboard.mediaShare.config.${key}`)"
        />
      </RowGroup>
      <RowGroup>
        <label
          v-for="key in numbers"
          :key="key"
          class="flex min-h-11 items-center gap-3 px-3 py-2 text-sm font-medium"
        >
          <span class="min-w-0 flex-1">{{ $t(`dashboard.mediaShare.config.${key}`) }}</span>
          <Input
            type="number"
            min="0"
            class="w-28"
            :disabled="!can('media:write')"
            :model-value="draft[key] ?? 0"
            :data-field="key"
            @update:model-value="setNumber(key, $event)"
          />
        </label>
      </RowGroup>
      <div v-if="can('media:write')" class="flex justify-end gap-2">
        <Button variant="ghost" :disabled="!dirty" data-action="discard" @click="discard">
          {{ $t("dashboard.mediaShare.config.discard") }}
        </Button>
        <Button :disabled="!dirty || save.isPending.value" data-action="save" @click="submit">
          {{ $t("dashboard.mediaShare.config.save") }}
        </Button>
      </div>
    </div>
  </PanelCard>
</template>
