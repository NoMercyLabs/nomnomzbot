<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The top of the profile dialog (mockup profile.js .p-head + .stats): who this is, their roles, when they
// were last seen, and one stat for every number the API really returns. A stat with no data is left out.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { CommunityMember } from '@/api/community';
import type { PersonProfile } from '@/api/people';
import PlatformLogo from '@/components/layout/PlatformLogo.vue';
import { roleIcons, type RoleKey } from '@/components/layout/profile/profileRoles';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { DialogDescription, DialogTitle } from '@/components/ui/dialog';
import { agoText } from '@/lib/time';

type Stat = {
  key: string;
  label: string;
  value: string | number;
};

const props = defineProps<{
  person: CommunityMember | null;
  profile: PersonProfile | null;
  roles: readonly RoleKey[];
}>();

const { t, n, locale } = useI18n();

const title = computed<string>(() => props.person?.displayName ?? t('profile.title'));
const pronouns = computed<string | null>(() => props.profile?.identity.pronoun ?? null);
const lastSeen = computed<string>(() => agoText(props.person?.lastSeen, new Date(), locale.value));

const stats = computed<Stat[]>(() => {
  const who = props.person;

  if (!who)
    return [];

  const list: Stat[] = [
    {
      key: 'messages',
      label: t('profile.stats.messages'),
      value: Number(who.messageCount),
    },
    {
      key: 'watchHours',
      label: t('profile.stats.watchHours'),
      value: n(Number(who.watchHours), { maximumFractionDigits: 1 }),
    },
    {
      key: 'commands',
      label: t('profile.stats.commands'),
      value: Number(who.commandsUsed),
    },
  ];
  const redemptions = props.profile?.activity?.totalRedemptions;
  const streak = props.profile?.streak?.currentStreak;

  if (redemptions !== undefined) {
    list.push({
      key: 'redemptions',
      label: t('profile.stats.redemptions'),
      value: Number(redemptions),
    });
  }

  if (streak !== undefined) {
    list.push({
      key: 'streak',
      label: t('profile.stats.streak'),
      value: Number(streak),
    });
  }

  return list;
});
</script>

<template>
  <div data-slot="profile-header" class="grid flex-none gap-4 px-6 pt-6 pr-14 pb-4">
    <div class="flex items-center gap-3">
      <Avatar class="size-12">
        <AvatarImage v-if="person?.profileImageUrl" :src="person.profileImageUrl" :alt="title" />
        <AvatarFallback>{{ title.slice(0, 2).toUpperCase() }}</AvatarFallback>
      </Avatar>
      <div class="grid min-w-0 gap-1">
        <DialogTitle class="truncate text-lg font-semibold">{{ title }}</DialogTitle>
        <DialogDescription class="sr-only">{{
          person ? `@${person.username}` : $t('profile.description')
        }}</DialogDescription>
        <div class="flex flex-wrap items-center gap-2">
          <Badge v-if="pronouns" variant="outline">{{ pronouns }}</Badge>
          <Badge
            v-for="role in roles"
            :key="role"
            data-profile-role
            variant="secondary"
            class="bg-muted text-muted-foreground"
          >
            <!-- The stored subscription is Twitch's (Helix seed and reconcile); a YouTube member is not the same. -->
            <PlatformLogo v-if="role === 'subscriber'" data-profile-platform platform="twitch" mono />
            <component :is="roleIcons[role]" v-else aria-hidden="true" />
            {{ $t(`profile.roles.${role}`) }}
          </Badge>
          <span v-if="lastSeen" class="text-xs text-muted-foreground">{{
            $t('profile.lastSeen', { ago: lastSeen })
          }}</span>
          <span v-else-if="person" class="text-xs text-muted-foreground">{{
            $t('profile.handle', { name: person.username })
          }}</span>
        </div>
      </div>
      <StatusBadge v-if="person?.isBanned" class="ml-auto" tone="bad" :label="$t('profile.banned')" />
    </div>

    <div
      v-if="stats.length > 0"
      class="grid auto-cols-fr grid-flow-col gap-3 rounded-lg border border-border p-3"
    >
      <div v-for="stat in stats" :key="stat.key" data-profile-stat class="grid min-w-0 content-start gap-1">
        <b class="text-2xl font-semibold tabular-nums">{{
          typeof stat.value === 'number'
            ? n(stat.value)
            : stat.value
        }}</b>
        <span class="text-xs text-muted-foreground">{{ stat.label }}</span>
      </div>
    </div>
  </div>
</template>
