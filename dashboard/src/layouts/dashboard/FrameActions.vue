<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Shield mode sits in every dashboard header (frame.css:60); the error inbox sits beside it.
import { useChannelAccess } from '@/api/access';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import ErrorInbox from './ErrorInbox.vue';
import ShieldToggle from './ShieldToggle.vue';

// Both tools are moderation tools: a person without the action behind them (a previewed viewer) sees neither.
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
</script>

<template>
  <template v-if="access.can('moderation:chat:settings:read')">
    <ShieldToggle />
    <ErrorInbox />
  </template>
</template>
