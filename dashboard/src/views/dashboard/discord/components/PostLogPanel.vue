<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What the bot did in Discord lately: one row per attempt, with the result and, on a failure, the reason.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useDiscordDispatchLog,
  useDiscordTriggers,
  type DiscordPost,
  type DiscordTrigger,
} from '@/api/discord';
import DateTime from '@/components/layout/DateTime.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { triggerLabel } from '../triggers';

const props = defineProps<{ connectionId: string }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const query = useDiscordDispatchLog(channelId, () => props.connectionId);
const state = usePanelState(query, (rows: DiscordPost[]): boolean =>
  rows.length === 0);
const rows = computed<DiscordPost[]>(() => query.data.value ?? []);
const triggersQuery = useDiscordTriggers(channelId);
const offered = computed<DiscordTrigger[]>(() => triggersQuery.data.value ?? []);

const known = ['sent', 'failed', 'skipped', 'skipped_dupe'];

function statusLabel(status: string): string {
  return known.includes(status)
    ? t(`dashboard.discord.posts.status.${status}`)
    : status;
}

function detail(post: DiscordPost): string {
  return post.error ?? statusLabel(post.status);
}
</script>

<template>
  <PanelCard
    tier="quiet"
    :title="t('dashboard.discord.posts.title')"
    :description="t('dashboard.discord.posts.subtitle')"
    :state="state"
    :empty-text="t('dashboard.discord.posts.empty')"
    :error-text="t('dashboard.discord.posts.error')"
    @retry="query.refetch()"
  >
    <ul>
      <li v-for="post in rows" :key="post.id" data-post-row>
        <SummaryRow :title="triggerLabel(t, post.triggerType, offered)" :detail="detail(post)">
          <template #trailing>
            <DateTime :value="post.dispatchedAt" variant="relative" class="text-sm text-muted-foreground" />
          </template>
        </SummaryRow>
      </li>
    </ul>
  </PanelCard>
</template>
