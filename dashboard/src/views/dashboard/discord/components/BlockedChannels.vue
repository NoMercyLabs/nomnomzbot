<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A picker lists channels the bot cannot post in yet too. Name them, and say how the bot gets access.
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
