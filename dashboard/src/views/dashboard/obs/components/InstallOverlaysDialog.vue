<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One call puts the overlays into OBS: pick a scene, tick the overlays, press Add to OBS. The bot makes one
// grouped scene with a browser source per overlay and adds it to the picked scene. Every overlay is optional.
// An overlay not yet open in OBS starts ticked, one already loaded starts unticked; the person can change each box.
// After the call the dialog lists each overlay with its result, and the primary button becomes Done.
import { computed, ref, watch } from 'vue';

import { useObsInstallOverlays, type ObsOverlayInstallResult, type ObsScene } from '@/api/obs';
import type { RequiredOverlay } from '@/api/widgets';
import FormDialog from '@/components/layout/FormDialog.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldLabel } from '@/components/ui/field';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastObsError } from '../obsToast';

const props = defineProps<{
  scenes: readonly ObsScene[] | undefined;
  overlays: readonly RequiredOverlay[] | undefined;
}>();

const open = defineModel<boolean>('open', { required: true });

const { channelId } = useCurrentChannel();
const install = useObsInstallOverlays(channelId);

const scene = ref<string>('');
const ticked = ref<string[]>([]);
const result = ref<ObsOverlayInstallResult | null>(null);

const choices = computed(() =>
  (props.scenes ?? []).map((item) => ({
    value: item.name,
    label: item.name,
  })));

// Each opening starts again: the on-air scene, the overlays not yet in OBS ticked, no result from an earlier run.
watch(
  open,
  (shown) => {
    if (!shown) return;

    scene.value = props.scenes?.find((item) => item.isCurrent)?.name ?? '';

    ticked.value = (props.overlays ?? [])
      .filter((overlay) => !overlay.widget.isAttached)
      .map((overlay) => overlay.key);

    result.value = null;
  },
  { immediate: true },
);

function toggle(key: string, on: boolean | 'indeterminate'): void {
  ticked.value = on === true
    ? [...ticked.value, key]
    : ticked.value.filter((item) => item !== key);
}

const canSend = computed<boolean>(() => scene.value !== '' && ticked.value.length > 0);

// The overlays go out in the order they are listed, not the order they were ticked.
const keysToSend = computed<string[]>(() =>
  (props.overlays ?? [])
    .map((overlay) => overlay.key)
    .filter((key) => ticked.value.includes(key)));

function submit(): void {
  if (result.value) {
    open.value = false;

    return;
  }

  install.mutate({
    sceneName: scene.value,
    overlayKeys: keysToSend.value,
  }, {
    onSuccess: (done) => {
      result.value = done;
    },
    onError: toastObsError,
  });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-obs-install-dialog
    :title="$t('dashboard.obs.install.title')"
    :description="$t('dashboard.obs.install.description')"
    :action="result
      ? $t('dashboard.obs.install.done')
      : $t('dashboard.obs.install.submit')"
    :pending="install.isPending.value"
    :disabled="!result && !canSend"
    @submit="submit"
  >
    <template v-if="result">
      <ul class="grid gap-2" :aria-label="$t('dashboard.obs.install.results')">
        <li
          v-for="entry in result.overlays"
          :key="entry.key"
          :data-install-result="entry.key"
          class="grid gap-1 text-sm"
        >
          <span class="flex items-center justify-between gap-2">
            <span class="font-medium">{{ $t(`dashboard.obs.required.surfaces.${entry.key}.name`) }}</span>
            <span data-install-outcome class="text-muted-foreground">
              {{ $t(`dashboard.obs.install.outcome.${entry.outcome}`) }}
            </span>
          </span>
          <InlineNotice v-if="entry.error" tone="error" compact>{{ entry.error }}</InlineNotice>
        </li>
      </ul>
    </template>
    <template v-else>
      <Field>
        <FieldLabel>{{ $t('dashboard.obs.install.scene') }}</FieldLabel>
        <OptionSelect
          v-model="scene"
          data-install-scene
          :options="choices"
          :placeholder="$t('dashboard.obs.install.scenePlaceholder')"
        />
        <p v-if="choices.length === 0" class="text-sm text-muted-foreground">
          {{ $t('dashboard.obs.install.noScenes') }}
        </p>
      </Field>
      <fieldset class="grid gap-2">
        <legend class="mb-1 text-sm font-medium">{{ $t('dashboard.obs.install.overlays') }}</legend>
        <label
          v-for="overlay in overlays"
          :key="overlay.key"
          class="flex items-center gap-2 text-sm"
        >
          <Checkbox
            :data-install-overlay="overlay.key"
            :model-value="ticked.includes(overlay.key)"
            @update:model-value="toggle(overlay.key, $event)"
          />
          <span>{{ $t(`dashboard.obs.required.surfaces.${overlay.key}.name`) }}</span>
          <span v-if="overlay.widget.isAttached" class="text-muted-foreground">
            {{ $t('dashboard.obs.install.loaded') }}
          </span>
        </label>
      </fieldset>
    </template>
  </FormDialog>
</template>
