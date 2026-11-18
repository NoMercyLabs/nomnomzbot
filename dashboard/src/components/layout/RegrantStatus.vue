<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one line that tells the person where a Twitch permission re-grant stands: waiting for the
// approval, approved, or ended (expired, denied, could not be checked). Shows nothing before a code exists.
import type { RegrantState } from '@/api/twitchHealth';
import InlineNotice from '@/components/layout/InlineNotice.vue';

defineProps<{ state: RegrantState }>();
</script>

<template>
  <p v-if="state === 'waiting'" class="m-0 text-sm text-muted-foreground" data-slot="regrant-status">
    {{ $t('common.regrant.waiting') }}
  </p>
  <p v-else-if="state === 'approved'" class="m-0 text-sm" data-slot="regrant-status">
    {{ $t('common.regrant.approved') }}
  </p>
  <InlineNotice v-else-if="state !== 'idle'" tone="error" compact data-slot="regrant-status">
    {{ $t(`common.regrant.${state}`) }}
  </InlineNotice>
</template>
