<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one button on an overlay row that sets it up in OBS, and the dialog it opens. Not loaded in OBS yet: the
// button says Connect. Loaded: it says Configure. Both open the same dialog, which teaches the frame size and the
// two OBS boxes from the catalog, and holds the Copy address button. The address holds a secret token, so the page
// never prints it: Copy asks for it only when pressed. One primary per group: the button is primary only when the
// parent says it is the next thing to do; the Copy button in the dialog is primary until the overlay is loaded.
import Check from '~icons/nnz/check';
import Copy from '~icons/nnz/file-copy';
import { useTimeoutFn } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ObsBrowserSource } from '@/api/widgets';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { useObsRepairsWidgets } from '@/composables/useObsRepairsWidgets';

const COPIED_MS = 2000;
const FULL_WIDTH = 1920;
const FULL_HEIGHT = 1080;

const props = withDefaults(
  defineProps<{
    title: string;
    setup?: ObsBrowserSource | null;
    connected: boolean;
    primary?: boolean;
    disabled?: boolean;
    expected?: boolean;
    resolveAddress: () => Promise<string | null | undefined>;
  }>(),
  {
    setup: null,
    primary: false,
    disabled: false,
    expected: false,
  },
);

const { t } = useI18n();

// An overlay that is switched on and expected in OBS, but has no live connection to the bot, shows nothing on stream.
// When the bot controls OBS it reloads that source by itself, so the warning is only for the setups where it cannot.
const repairs = useObsRepairsWidgets();
const stranded = computed<boolean>(() => props.expected && !props.connected && !repairs.value);

const open = ref<boolean>(false);
const copied = ref<boolean>(false);
const { start: resetCopied } = useTimeoutFn(
  () => {
    copied.value = false;
  },
  COPIED_MS,
  { immediate: false },
);

const sizeKey = computed<string>(() => {
  const { width, height } = props.setup ?? { width: 0, height: 0 };

  if (width === FULL_WIDTH && height === FULL_HEIGHT)
    return 'dashboard.overlays.setup.size.full';

  return width === 1 && height === 1
    ? 'dashboard.overlays.setup.size.tiny'
    : 'dashboard.overlays.setup.size.other';
});

const facts = computed<{
  id: string;
  label: string;
  value: string;
}[]>(() => {
  if (!props.setup)
    return [];

  const { width, height, controlAudioViaObs, shutdownWhenHidden } = props.setup;

  return [
    {
      id: 'size',
      label: t('dashboard.overlays.setup.size.label'),
      value: t(sizeKey.value, { width, height }),
    },
    {
      id: 'audio',
      label: t('dashboard.overlays.setup.audio.label'),
      value: t(
        controlAudioViaObs
          ? 'dashboard.overlays.setup.audio.on'
          : 'dashboard.overlays.setup.audio.off',
      ),
    },
    {
      id: 'shutdown',
      label: t('dashboard.overlays.setup.shutdown.label'),
      value: t(
        shutdownWhenHidden
          ? 'dashboard.overlays.setup.shutdown.on'
          : 'dashboard.overlays.setup.shutdown.off',
      ),
    },
  ];
});

async function copyAddress(): Promise<void> {
  const address = await props.resolveAddress();

  if (!address) return;

  await navigator.clipboard.writeText(address);
  copied.value = true;
  resetCopied();
}
</script>

<template>
  <span
    v-if="stranded"
    role="alert"
    data-obs-setup-warning
    class="font-medium text-destructive"
    :title="$t('dashboard.overlays.setup.strandedHint')"
  >
    <StatusBadge tone="bad" :label="$t('dashboard.overlays.setup.stranded')" />
  </span>
  <Button
    :variant="primary && !connected ? 'default' : 'outline'"
    :disabled="disabled"
    data-obs-setup-open
    @click="open = true"
  >
    {{ $t(connected ? 'dashboard.overlays.setup.configure' : 'dashboard.overlays.setup.connect') }}
  </Button>
  <Dialog v-model:open="open">
    <DialogContent data-obs-setup-dialog :show-close-button="false">
      <DialogHeader>
        <DialogTitle>{{ $t('dashboard.overlays.setup.title', { name: title }) }}</DialogTitle>
        <DialogDescription>{{ $t('dashboard.overlays.setup.description') }}</DialogDescription>
      </DialogHeader>
      <p class="flex items-center gap-2 text-sm text-muted-foreground" data-obs-setup-status>
        <span class="dot" :class="connected ? 'ok' : 'warn'" aria-hidden="true" />
        {{ $t(connected ? 'dashboard.overlays.setup.loaded' : 'dashboard.overlays.setup.notLoaded') }}
      </p>
      <dl v-if="facts.length" class="grid gap-3" data-obs-setup-facts>
        <div v-for="fact in facts" :key="fact.id" :data-obs-setup-fact="fact.id">
          <dt class="text-sm font-medium">{{ fact.label }}</dt>
          <dd class="text-sm text-muted-foreground">{{ fact.value }}</dd>
        </div>
      </dl>
      <div>
        <p class="mb-1 text-sm font-medium">{{ $t('dashboard.overlays.setup.stepsTitle') }}</p>
        <ol class="list-decimal pl-5 text-sm text-muted-foreground">
          <li>{{ $t('dashboard.overlays.setup.steps.copy') }}</li>
          <li>{{ $t('dashboard.overlays.setup.steps.add') }}</li>
          <li>{{ $t('dashboard.overlays.setup.steps.set') }}</li>
        </ol>
      </div>
      <div class="flex justify-end gap-2">
        <DialogClose as-child>
          <Button variant="ghost" data-obs-setup-close>{{ $t('common.close') }}</Button>
        </DialogClose>
        <Button
          :variant="connected ? 'outline' : 'default'"
          data-obs-setup-copy
          @click="copyAddress"
        >
          <component :is="copied ? Check : Copy" aria-hidden="true" />
          {{ $t(copied ? 'dashboard.overlays.setup.copied' : 'dashboard.overlays.setup.copy') }}
        </Button>
      </div>
    </DialogContent>
  </Dialog>
</template>
