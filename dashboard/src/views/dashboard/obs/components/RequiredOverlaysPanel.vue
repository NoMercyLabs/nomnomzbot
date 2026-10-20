<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one place for the overlays the bot cannot work without (alerts, audio, TTS caption, raid recap).
// Each is one browser source in OBS. The first one not loaded yet gets the one primary button; the rest are outline.
// The address holds a secret token: Copy sends it to the clipboard and the page never prints it.
// The id is the anchor a setup guide links to: /app/<channel>/obs#required-overlays.
import Check from '~icons/nnz/check';
import Copy from '~icons/nnz/file-copy';
import { useTimeoutFn } from '@vueuse/core';
import { computed, ref } from 'vue';

import { readObsBridgeAddress, useObsBridge } from '@/api/obs';
import type { RequiredOverlay } from '@/api/widgets';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { overlayHealth } from '@/views/dashboard/overlays/components/overlayHealth';

import { sourceReasonKey } from '../obsHealth';

const COPIED_MS = 2000;

const props = defineProps<{
  overlays: readonly RequiredOverlay[] | undefined;
  panelState: PanelState;
}>();

defineEmits<{ retry: [] }>();

const copiedKey = ref<string | null>(null);
const { start: resetCopied } = useTimeoutFn(
  () => {
    copiedKey.value = null;
  },
  COPIED_MS,
  { immediate: false },
);

// The first surface that is switched on but not open in OBS: the one thing to do next.
const nextKey = computed<string | null>(
  () =>
    props.overlays?.find(
      (overlay: RequiredOverlay) =>
        overlay.widget.isEnabled && !overlay.widget.isAttached && !!overlay.widget.overlayUrl,
    )?.key ?? null,
);

async function copyAddress(overlay: RequiredOverlay): Promise<void> {
  if (!overlay.widget.overlayUrl) return;

  await navigator.clipboard.writeText(overlay.widget.overlayUrl);
  copiedKey.value = overlay.key;
  resetCopied();
}

// The OBS control bridge is a browser source too. Its address is read when Copy is pressed, never kept on the page.
const BRIDGE_KEY = 'bridge';
const { channelId } = useCurrentChannel();
const { can } = useCan();
const canCopyBridge = computed<boolean>(() => can('obs:config:write'));
const bridgeChannelId = computed<string | null>(() =>
  can('obs:config:read')
    ? channelId.value
    : null,
);
const bridge = useObsBridge(bridgeChannelId);
const bridgeOpen = computed<boolean>(() => Number(bridge.data.value?.instanceCount ?? 0) > 0);
const bridgeDot = computed<string>(() =>
  bridgeOpen.value
    ? 'ok'
    : 'warn',
);
const bridgeMeta = computed<string>(() => {
  if (!canCopyBridge.value)
    return 'dashboard.obs.required.bridge.locked';

  return bridgeOpen.value
    ? 'dashboard.obs.sources.loaded'
    : 'dashboard.obs.sources.notLoaded';
});
const bridgeIsNext = computed<boolean>(
  () => nextKey.value === null && !bridgeOpen.value && canCopyBridge.value,
);

async function copyBridgeAddress(): Promise<void> {
  if (!channelId.value) return;

  const address = await readObsBridgeAddress(channelId.value);

  if (!address) return;

  await navigator.clipboard.writeText(address);
  copiedKey.value = BRIDGE_KEY;
  resetCopied();
}
</script>

<template>
  <PanelCard
    id="required-overlays"
    data-required-overlays
    :title="$t('dashboard.obs.required.title')"
    :description="$t('dashboard.obs.required.description')"
    :state="panelState"
    :empty-text="$t('dashboard.obs.required.empty')"
    :error-text="$t('dashboard.obs.required.loadError')"
    @retry="$emit('retry')"
  >
    <div class="px-1 py-0.5">
      <SummaryRow
        v-for="overlay in overlays"
        :key="overlay.key"
        :data-required-overlay="overlay.key"
        :title="$t(`dashboard.obs.required.surfaces.${overlay.key}.name`)"
        :detail="$t(`dashboard.obs.required.surfaces.${overlay.key}.detail`)"
        :meta="$t(sourceReasonKey(overlay.widget))"
      >
        <template #leading>
          <span data-obs-dot="required" class="dot" :class="overlayHealth(overlay.widget)" aria-hidden="true" />
        </template>
        <template #trailing>
          <Button
            :variant="overlay.key === nextKey ? 'default' : 'outline'"
            :disabled="!overlay.widget.overlayUrl"
            data-required-copy
            @click="copyAddress(overlay)"
          >
            <component :is="copiedKey === overlay.key ? Check : Copy" aria-hidden="true" />
            {{ $t(copiedKey === overlay.key ? 'dashboard.obs.required.copied' : 'dashboard.obs.required.copy') }}
          </Button>
        </template>
      </SummaryRow>
      <SummaryRow
        data-required-bridge
        :title="$t('dashboard.obs.required.bridge.name')"
        :detail="$t('dashboard.obs.required.bridge.detail')"
        :meta="$t(bridgeMeta)"
      >
        <template #leading>
          <span data-obs-dot="required" class="dot" :class="bridgeDot" aria-hidden="true" />
        </template>
        <template #trailing>
          <Button
            :variant="bridgeIsNext ? 'default' : 'outline'"
            :disabled="!canCopyBridge"
            data-required-copy
            data-required-bridge-copy
            @click="copyBridgeAddress()"
          >
            <component :is="copiedKey === BRIDGE_KEY ? Check : Copy" aria-hidden="true" />
            {{ $t(copiedKey === BRIDGE_KEY ? 'dashboard.obs.required.copied' : 'dashboard.obs.required.copy') }}
          </Button>
        </template>
      </SummaryRow>
    </div>
  </PanelCard>
</template>
