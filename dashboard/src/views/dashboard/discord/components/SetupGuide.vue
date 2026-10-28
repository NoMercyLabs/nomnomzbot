<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// No server is linked yet: say what the bot does in Discord, then give the one action that starts the install.
import { useI18n } from 'vue-i18n';

import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';

import { useDiscordInstall } from '../useDiscordInstall';

const props = defineProps<{ channelId: string }>();

const { t } = useI18n();
const { can } = useCan();

const abilities = ['posts', 'roles', 'live'] as const;

const { url: startUrl, start: startInstall } = useDiscordInstall(() => props.channelId);
</script>

<template>
  <div class="flex flex-col gap-4">
    <ul data-bot-does class="flex flex-col gap-2 text-sm">
      <li v-for="ability in abilities" :key="ability">
        <span class="font-medium">{{ t(`dashboard.discord.setup.${ability}.title`) }}</span>
        <span class="text-muted-foreground"> {{ t(`dashboard.discord.setup.${ability}.detail`) }}</span>
      </li>
    </ul>
    <p class="text-sm text-muted-foreground">
      {{ t("dashboard.discord.setup.steps") }}
    </p>
    <div v-if="can('discord:connection:write')">
      <Button as-child>
        <a data-add-bot :href="startUrl" @click="startInstall">{{ t("dashboard.discord.setup.add") }}</a>
      </Button>
    </div>
  </div>
</template>
