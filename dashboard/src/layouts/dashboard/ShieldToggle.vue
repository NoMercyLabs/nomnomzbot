<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Shield mode is a one-tap write to the live channel. A phone-sized thumb hits it by mistake, so below md
// a tap asks first and names the effect; a wide screen keeps the one tap.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import Shield from '~icons/nnz/shield';
import ShieldCheck from '~icons/nnz/shield-check';

import { useSetShieldMode, useShieldMode } from '@/api/shield';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Toggle } from '@/components/ui/toggle';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useNarrowScreen } from '@/composables/useLayout';

const { t } = useI18n();
const { channelId, current } = useCurrentChannel();
const shield = useShieldMode(channelId);
const setShield = useSetShieldMode(channelId);
const narrow = useNarrowScreen();
const asking = ref(false);
const wanted = ref(false);

function change(on: boolean): void {
  if (!narrow.value) {
    setShield.mutate(on);

    return;
  }

  wanted.value = on;
  asking.value = true;
}

function confirm(): void {
  setShield.mutate(wanted.value);
  asking.value = false;
}
</script>

<template>
  <!-- The state comes from Twitch, so the button shows only once the real state is known. -->
  <Toggle
    v-if="shield.data.value !== undefined"
    variant="outline"
    data-slot="shield-toggle"
    :model-value="shield.data.value"
    :disabled="setShield.isPending.value"
    :title="t('frame.shield.for', { channel: current?.displayName ?? '' })"
    @update:model-value="change"
  >
    <ShieldCheck v-if="shield.data.value" class="size-5" aria-hidden="true" />
    <Shield v-else class="size-5" aria-hidden="true" />
    <!-- On a phone-wide pane only the icon shows; the words stay as its accessible name. -->
    <span class="@max-md:sr-only">{{
      shield.data.value ? t('frame.shield.on') : t('frame.shield.off')
    }}</span>
  </Toggle>
  <ConfirmDialog
    v-model:open="asking"
    :title="t(wanted ? 'frame.shield.confirmOn' : 'frame.shield.confirmOff', { channel: current?.displayName ?? '' })"
    :description="t(wanted ? 'frame.shield.effectOn' : 'frame.shield.effectOff')"
    :action="wanted ? t('frame.shield.turnOn') : t('frame.shield.turnOff')"
    :pending="setShield.isPending.value"
    @confirm="confirm"
  />
</template>
