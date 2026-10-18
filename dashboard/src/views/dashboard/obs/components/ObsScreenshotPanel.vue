<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A still of one OBS source. The image comes back as a data URI and is never stored.
import { ref } from 'vue';

import { useObsScreenshot, type ObsScreenshotFormat } from '@/api/obsControl';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastObsError } from '../obsToast';

defineProps<{ offline: boolean }>();

const formats = ['png', 'jpg'] as const satisfies readonly ObsScreenshotFormat[];

const { channelId } = useCurrentChannel();
const capture = useObsScreenshot(channelId);

const sourceName = ref<string>('');
const imageFormat = ref<ObsScreenshotFormat>('png');
const image = ref<string>('');

function take(): void {
  if (sourceName.value.trim() === '') return;

  capture.mutate({
    sourceName: sourceName.value.trim(),
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
        <Input
          v-model="sourceName"
          data-obs-shot-source
          class="w-56"
          :placeholder="$t('dashboard.obs.screenshot.source')"
          :aria-label="$t('dashboard.obs.screenshot.source')"
        />
        <Button
          v-for="format in formats"
          :key="format"
          variant="outline"
          :data-obs-shot-format="format"
          :aria-pressed="imageFormat === format"
          :class="imageFormat === format && 'bg-accent text-accent-foreground'"
          @click="imageFormat = format"
        >
          {{ format }}
        </Button>
        <Button data-obs-shot-capture :disabled="offline || capture.isPending.value" @click="take">
          {{ $t('dashboard.obs.screenshot.capture') }}
        </Button>
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
