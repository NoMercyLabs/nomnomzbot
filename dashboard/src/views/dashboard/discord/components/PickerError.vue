<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Says why a Discord picker has nothing to pick: the bot could not read the server's channels or roles.
// A setup reason the server names by code gets its own plain line; the raw server text never shows.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { ApiError } from '@/api/errors';
import { errorText } from '@/lib/errors';

const props = defineProps<{ error: Error | null }>();

const { t } = useI18n();

const setupReasons = new Map([
  ['DISCORD_BOT_TOKEN_NOT_CONFIGURED', 'dashboard.discord.editor.pickerNoBotToken'],
  ['DISCORD_NOT_CONNECTED', 'dashboard.discord.editor.pickerNotConnected'],
]);

const setupReason = computed(() => {
  const code = props.error instanceof ApiError
    ? props.error.serverCode
    : undefined;

  return code === undefined
    ? undefined
    : setupReasons.get(code);
});
</script>

<template>
  <p v-if="error" role="alert" data-picker-error class="text-sm text-destructive">
    <template v-if="setupReason">
      {{ t(setupReason) }}
    </template>
    <template v-else>
      {{ t('dashboard.discord.editor.pickerError') }} {{ errorText(error, t) }}
    </template>
  </p>
</template>
