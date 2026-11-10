<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// The YouTube members tab of the People page: who pays for a membership, their level and since when.
// A channel that is not connected, did not allow the list, or has no memberships gets the reason instead.
import type { InfiniteData } from '@tanstack/vue-query';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';

import { useYouTubeMembers, type YouTubeMember, type YouTubeMembers } from '@/api/youtubeMembers';
import PanelCard from '@/components/layout/PanelCard.vue';
import ShowMoreFooter from '@/components/layout/ShowMoreFooter.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

const BLOCKED = ['not_connected', 'not_allowed', 'not_enabled'] as const;

const { t, n } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const page = 'dashboard.people.youtube';

const members = useYouTubeMembers(channelId);
const first = computed(() => members.data.value?.pages[0] ?? null);
const blocked = computed(() => BLOCKED.find((access) => access === first.value?.access) ?? null);
const rows = computed<YouTubeMember[]>(() =>
  (members.data.value?.pages ?? []).flatMap((answer) => answer?.members ?? []));
const total = computed(() => Number(first.value?.total ?? 0));
const remaining = computed(() => Math.max(total.value - rows.value.length, 1));
const state = usePanelState(
  members,
  (answer: InfiniteData<YouTubeMembers | null, string | null>) =>
    answer.pages[0]?.access === 'allowed' && (answer.pages[0].members ?? []).length === 0,
);
const description = computed(() =>
  blocked.value === null && state.value === 'ready'
    ? `${t(`${page}.count`, { count: n(total.value) }, total.value)} · ${t(`${page}.description`)}`
    : t(`${page}.description`));

const initials = (member: YouTubeMember): string => member.displayName.slice(0, 2).toUpperCase();
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col p-4 lg:p-6">
    <PanelCard
      data-youtube-members
      :title="t(`${page}.title`)"
      :description="description"
      :state="state"
      :empty-text="t(`${page}.empty`)"
      :error-text="t(`${page}.error`)"
      @retry="members.refetch()"
    >
      <div v-if="blocked" data-youtube-blocked class="grid justify-items-start gap-3 px-2 py-3">
        <p class="text-sm text-foreground">{{ t(`${page}.blocked.${blocked}`) }}</p>
        <Button v-if="blocked !== 'not_enabled'" as-child variant="outline" size="sm">
          <RouterLink :to="{ name: 'dashboard-settings-integrations' }">
            {{ t(`${page}.blocked.open`) }}
          </RouterLink>
        </Button>
      </div>
      <Table v-else>
        <TableHeader>
          <TableRow class="hover:bg-transparent">
            <TableHead>{{ t(`${page}.columns.member`) }}</TableHead>
            <TableHead>{{ t(`${page}.columns.level`) }}</TableHead>
            <TableHead class="max-md:hidden">{{ t(`${page}.columns.since`) }}</TableHead>
            <TableHead class="text-right">{{ t(`${page}.columns.months`) }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="(member, index) in rows"
            :key="member.channelId ?? `${member.displayName}-${index}`"
            data-youtube-member
          >
            <TableCell class="font-medium">
              <span class="flex min-w-0 items-center gap-2">
                <Avatar class="size-7">
                  <AvatarImage
                    v-if="member.profileImageUrl"
                    :src="member.profileImageUrl"
                    :alt="member.displayName"
                  />
                  <AvatarFallback>{{ initials(member) }}</AvatarFallback>
                </Avatar>
                <span class="truncate">{{ member.displayName }}</span>
              </span>
            </TableCell>
            <TableCell>{{ member.levelName ?? '' }}</TableCell>
            <TableCell class="max-md:hidden">
              {{ member.memberSince ? time.date(member.memberSince) : '' }}
            </TableCell>
            <TableCell class="text-right tabular-nums">{{ n(Number(member.totalMonths)) }}</TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <template v-if="members.hasNextPage.value" #footer>
        <ShowMoreFooter :remaining="remaining" @more="members.fetchNextPage()" />
      </template>
    </PanelCard>
  </div>
</template>
