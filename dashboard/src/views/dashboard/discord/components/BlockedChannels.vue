<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A greyed channel in a picker is one the bot cannot post in. Say so, name them, and say how to fix it in Discord.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { DiscordChannel } from '@/api/discord';

const props = defineProps<{ channels: DiscordChannel[] }>();

const { t } = useI18n();

const blocked = computed<string>(() =>
  props.channels
    .filter((channel) => !channel.canPost)
    .map((channel) => `#${channel.name}`)
    .join(', '),
);
</script>

<template>
  <p v-if="blocked !== ''" data-blocked-channels class="text-sm text-muted-foreground">
    {{ t('dashboard.discord.editor.blockedChannels', { names: blocked }) }}
  </p>
</template>
