<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
