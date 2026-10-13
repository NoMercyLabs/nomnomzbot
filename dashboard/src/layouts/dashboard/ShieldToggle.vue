<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import Shield from '~icons/nnz/shield';
import ShieldCheck from '~icons/nnz/shield-check';

import { useSetShieldMode, useShieldMode } from '@/api/shield';
import { Toggle } from '@/components/ui/toggle';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const { t } = useI18n();
const { channelId, current } = useCurrentChannel();
const shield = useShieldMode(channelId);
const setShield = useSetShieldMode(channelId);
</script>

<template>
  <!-- The state comes from Twitch, so the button shows only once the real state is known. -->
  <Toggle
    v-if="shield.data.value !== undefined"
    variant="outline"
    size="sm"
    data-slot="shield-toggle"
    :model-value="shield.data.value"
    :disabled="setShield.isPending.value"
    :title="t('frame.shield.for', { channel: current?.displayName ?? '' })"
    @update:model-value="(on: boolean) => setShield.mutate(on)"
  >
    <ShieldCheck v-if="shield.data.value" aria-hidden="true" />
    <Shield v-else aria-hidden="true" />
    <!-- On a phone-wide pane only the icon shows; the words stay as its accessible name. -->
    <span class="@max-md:sr-only">{{
      shield.data.value ? t('frame.shield.on') : t('frame.shield.off')
    }}</span>
  </Toggle>
</template>
