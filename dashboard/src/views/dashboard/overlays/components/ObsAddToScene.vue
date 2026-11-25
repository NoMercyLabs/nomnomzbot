<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Puts one overlay into an OBS scene in one press: the bot makes the browser source at the overlay's own size and
// names it after the overlay. Shown only while the bot controls OBS and the person may control it.
import { computed, ref, watch } from 'vue';

import { useChannelAccess } from '@/api/access';
import { useObsConnectionStatus, useObsInstallOverlays, useObsScenes } from '@/api/obs';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { toastObsError } from '@/views/dashboard/obs/obsToast';

const props = defineProps<{
  placeKey: string;
  name: string;
}>();

const emit = defineEmits<{ placed: [] }>();

// The parent hands the primary look to this button only while it is shown.
const shown = defineModel<boolean>('shown', { default: false });

const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const status = useObsConnectionStatus(channelId);
const available = computed<boolean>(() =>
  access.can('obs:control') && status.data.value?.isEnabled === true);

watch(available, (on) => {
  shown.value = on;
}, { immediate: true });

const scenes = useObsScenes(channelId, available);
const install = useObsInstallOverlays(channelId);

const scene = ref<string>('');
const sourceName = ref<string>(props.name);
const outcome = ref<string | null>(null);

const choices = computed(() =>
  (scenes.data.value ?? []).map((item) => ({
    value: item.name,
    label: item.name,
  })));

// The scene on air is the first guess, once the scenes arrive.
watch(
  () => scenes.data.value,
  (list) => {
    if (scene.value !== '') return;

    scene.value = list?.find((item) => item.isCurrent)?.name ?? '';
  },
  { immediate: true },
);

function place(): void {
  install.mutate({
    sceneName: scene.value,
    overlayKeys: [props.placeKey],
    placements: [
      {
        key: props.placeKey,
        sceneName: scene.value,
        sourceName: sourceName.value.trim() || null,
      },
    ],
  }, {
    onSuccess: (done) => {
      const entry = done.overlays.find((item) => item.key === props.placeKey);

      outcome.value = entry?.error ?? null;

      if (!entry?.error) {
        emit('placed');
      }
    },
    onError: toastObsError,
  });
}
</script>

<template>
  <div v-if="available" class="grid gap-2" data-obs-add-to-scene>
    <Field>
      <FieldLabel>{{ $t('dashboard.overlays.setup.place.scene') }}</FieldLabel>
      <OptionSelect
        v-model="scene"
        data-obs-add-scene
        :options="choices"
        :placeholder="$t('dashboard.overlays.setup.place.scenePlaceholder')"
      />
    </Field>
    <Field>
      <FieldLabel>{{ $t('dashboard.overlays.setup.place.name') }}</FieldLabel>
      <Input v-model="sourceName" data-obs-add-name :placeholder="name" />
    </Field>
    <p v-if="outcome" class="text-sm text-destructive" role="alert">{{ outcome }}</p>
    <Button
      class="justify-self-end"
      data-obs-add-submit
      :disabled="scene === '' || install.isPending.value"
      @click="place"
    >
      {{ $t('dashboard.overlays.setup.place.submit') }}
    </Button>
  </div>
</template>
