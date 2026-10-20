<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What the bot did in Discord lately: one row per attempt, with the result and, on a failure, the reason.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useDiscordDispatchLog, type DiscordPost } from '@/api/discord';
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
        <SummaryRow :title="triggerLabel(t, post.triggerType)" :detail="detail(post)">
          <template #trailing>
            <DateTime :value="post.dispatchedAt" variant="relative" class="text-sm text-muted-foreground" />
          </template>
        </SummaryRow>
      </li>
    </ul>
  </PanelCard>
</template>
