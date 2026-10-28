<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A still of one OBS source, picked from the inputs OBS reports. The image comes back as a data URI and is never stored.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useObsInputs, useObsScenes } from '@/api/obs';
import { useObsScreenshot, type ObsScreenshotFormat } from '@/api/obsControl';
import PanelCard from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { groupScreenshotSources } from '../obsSourceKinds';
import { toastObsError } from '../obsToast';

const props = defineProps<{ offline: boolean }>();

const { t } = useI18n();

const formats = [{ value: 'png', label: 'png' }, { value: 'jpg', label: 'jpg' }] as const satisfies readonly {
  value: ObsScreenshotFormat;
  label: string;
}[];

const { channelId } = useCurrentChannel();
const inputs = useObsInputs(channelId, () =>
  !props.offline);
const scenes = useObsScenes(channelId, () =>
  !props.offline);
const capture = useObsScreenshot(channelId);

const sourceName = ref<string>('');
const imageFormat = ref<ObsScreenshotFormat>('png');
const image = ref<string>('');

const groups = computed(() =>
  groupScreenshotSources(inputs.data.value ?? [], scenes.data.value ?? []));

// Capture waits for OBS and a picked source, and says which one it waits for.
const holdReason = computed<string | undefined>(() => {
  if (props.offline)
    return t('dashboard.obs.screenshot.offline');

  return sourceName.value === ''
    ? t('dashboard.obs.screenshot.pickFirst')
    : undefined;
});

function take(): void {
  if (holdReason.value !== undefined) return;

  capture.mutate({
    sourceName: sourceName.value,
    imageFormat: imageFormat.value,
  }, {
    onSuccess: (uri: string) => {
      image.value = uri;
    },
    onError: toastObsError,
  });
}
</script>

<template>
  <PanelCard :title="$t('dashboard.obs.screenshot.title')">
    <div class="grid gap-3 p-3">
      <div class="flex flex-wrap items-center gap-2">
        <Select v-model="sourceName" :disabled="offline">
          <SelectTrigger data-obs-shot-source class="w-56" :aria-label="$t('dashboard.obs.screenshot.source')">
            <SelectValue :placeholder="$t('dashboard.obs.screenshot.source')" />
          </SelectTrigger>
          <SelectContent position="popper">
            <SelectGroup v-for="entry in groups" :key="entry.group">
              <SelectLabel>{{ $t(`dashboard.obs.screenshot.groups.${entry.group}`) }}</SelectLabel>
              <SelectItem v-for="name in entry.names" :key="name" :value="name">
                {{ name }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <SegmentedControl
          v-model="imageFormat"
          :label="$t('dashboard.obs.screenshot.format')"
          :options="formats"
        />
        <span data-obs-shot-hold :title="holdReason">
          <Button
            data-obs-shot-capture
            :disabled="holdReason !== undefined || capture.isPending.value"
            @click="take"
          >
            {{ $t('dashboard.obs.screenshot.capture') }}
          </Button>
        </span>
      </div>
      <img
        v-if="image"
        data-obs-shot-image
        class="max-h-64 rounded-md border"
        :src="image"
        :alt="$t('dashboard.obs.screenshot.alt', { name: sourceName })"
      >
    </div>
  </PanelCard>
</template>
