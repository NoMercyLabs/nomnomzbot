<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The top of the profile (mockup profile.js .p-head + .stats): who this is, their roles, when they
// were last seen, and one stat for every number the API really returns. A stat with no data is left out.
// Inside the dialog the name is the dialog's title; on a page it is a plain heading.
import { injectDialogRootContext } from 'reka-ui';
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

export type ProfilePreview = Pick<CommunityMember, 'displayName' | 'username' | 'profileImageUrl'>;

type Stat = {
  key: string;
  label: string;
  value: string | number;
};

const props = defineProps<{
  person: CommunityMember | null;
  profile: PersonProfile | null;
  roles: readonly RoleKey[];
  preview?: ProfilePreview | null;
  // Inside a padded card the card owns the spacing.
  flush?: boolean;
}>();

const { t, n, locale } = useI18n();
const inDialog = injectDialogRootContext(null) !== null;

// The person as far as the header knows them: the full record, or the light preview while it loads.
const who = computed<ProfilePreview | null>(() => props.person ?? props.preview ?? null);
const title = computed<string>(() => who.value?.displayName ?? t('profile.title'));
const pronouns = computed<string | null>(() => props.profile?.identity.pronoun ?? null);
const lastSeen = computed<string>(() =>
  agoText(props.person?.lastSeen ?? undefined, new Date(), locale.value));

// How long this person has been around, at a glance: their Twitch account, their follow, their first sighting here.
const ages = computed<Stat[]>(() => {
  const identity = props.profile?.identity;
  const now = new Date();
  const moments = [
    { key: 'account', at: identity?.accountCreatedAtUtc },
    { key: 'followed', at: identity?.followedAtUtc },
    { key: 'firstSeen', at: identity?.firstSeenUtc ?? props.person?.firstSeen },
  ];

  return moments
    .map(({ key, at }) =>
      ({
        key,
        label: '',
        value: agoText(at ?? undefined, now, locale.value),
      }))
    .filter((age: Stat) =>
      age.value !== '')
    .map((age: Stat) =>
      ({ ...age, label: t(`profile.age.${age.key}`, { ago: age.value }) }));
});

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
  <div
    data-slot="profile-header"
    class="grid flex-none gap-4"
    :class="flush ? '' : ['px-6 pb-4', inDialog ? 'pt-2' : 'pt-6']"
  >
    <div class="flex items-center gap-3">
      <Avatar class="size-12">
        <AvatarImage v-if="who?.profileImageUrl" :src="who.profileImageUrl" :alt="title" />
        <AvatarFallback>{{ title.slice(0, 2).toUpperCase() }}</AvatarFallback>
      </Avatar>
      <div class="grid min-w-0 gap-1">
        <template v-if="inDialog">
          <DialogTitle class="truncate text-lg font-semibold">{{ title }}</DialogTitle>
          <DialogDescription class="sr-only">{{
            who ? `@${who.username}` : $t('profile.description')
          }}</DialogDescription>
        </template>
        <h2 v-else class="truncate text-lg font-semibold">{{ title }}</h2>
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
            {{ $t(role === 'subscriber'
              ? 'profile.roles.subscriberBadge'
              : `profile.roles.${role}`) }}
          </Badge>
          <span v-if="lastSeen" class="text-xs text-muted-foreground">{{
            $t('profile.lastSeen', { ago: lastSeen })
          }}</span>
          <span v-else-if="who" class="text-xs text-muted-foreground">{{
            $t('profile.handle', { name: who.username })
          }}</span>
        </div>
        <div
          v-if="ages.length > 0"
          data-profile-ages
          class="flex flex-wrap gap-x-3 text-xs text-muted-foreground"
        >
          <span v-for="age in ages" :key="age.key" :data-profile-age="age.key">{{
            age.label
          }}</span>
        </div>
      </div>
      <StatusBadge v-if="person?.isBanned" class="ml-auto" tone="bad" :label="$t('profile.banned')" />
    </div>

    <div
      v-if="stats.length > 0"
      class="grid grid-cols-[repeat(auto-fit,minmax(7rem,1fr))] gap-3 rounded-lg border border-border p-3"
    >
      <div v-for="stat in stats" :key="stat.key" data-profile-stat class="grid min-w-0 content-start gap-1">
        <b class="text-2xl font-semibold tabular-nums">{{
          typeof stat.value === 'number'
            ? n(stat.value)
            : stat.value
        }}</b>
        <span class="truncate text-xs text-muted-foreground">{{ stat.label }}</span>
      </div>
    </div>
  </div>
</template>
