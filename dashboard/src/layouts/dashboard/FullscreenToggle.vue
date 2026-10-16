<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Fullscreen hides the browser's own bars and gives the page the whole screen. A browser without the
// Fullscreen API (an iPhone) gets no button instead of one that does nothing.
import { useEventListener } from '@vueuse/core';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import EnterFullscreen from '~icons/nnz/arrow-enter-fullscreen';
import ExitFullscreen from '~icons/nnz/arrow-exit-fullscreen';

import { Button } from '@/components/ui/button';

const { t } = useI18n();
const isSupported = document.fullscreenEnabled === true;
const isFullscreen = ref(Boolean(document.fullscreenElement));

// The browser ends fullscreen by itself too (the Escape key), so the state follows its event.
useEventListener(document, 'fullscreenchange', () => {
  isFullscreen.value = Boolean(document.fullscreenElement);
});

async function toggle(): Promise<void> {
  if (document.fullscreenElement) {
    await document.exitFullscreen();

    return;
  }

  await document.documentElement.requestFullscreen();
}
</script>

<template>
  <Button
    v-if="isSupported"
    variant="ghost"
    class="justify-start"
    data-slot="fullscreen-toggle"
    :aria-pressed="isFullscreen"
    @click="toggle"
  >
    <ExitFullscreen v-if="isFullscreen" aria-hidden="true" />
    <EnterFullscreen v-else aria-hidden="true" />
    {{ isFullscreen ? t('frame.profile.exitFullscreen') : t('frame.profile.fullscreen') }}
  </Button>
</template>
