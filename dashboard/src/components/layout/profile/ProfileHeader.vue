<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
import {
  isRegular,
  roleIcons,
  type RoleKey,
  twitchStanding,
} from '@/components/layout/profile/profileRoles';
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

const rootClass = computed<string | string[]>(() => {
  if (props.flush)
    return '';

  return ['px-6 pb-4', inDialog
    ? 'pt-2'
    : 'pt-4'];
});

// The person as far as the header knows them: the full record, or the light preview while it loads.
const who = computed<ProfilePreview | null>(() => props.person ?? props.preview ?? null);
const title = computed<string>(() => who.value?.displayName ?? t('profile.title'));
// A second set from pronouns.alejo.io shows after the main one; a set a moderator typed says so.
const pronouns = computed<string | null>(() => {
  const identity = props.profile?.identity;
  const sets = [identity?.pronoun, identity?.altPronoun].filter((value): value is string =>
    Boolean(value),
  );

  if (sets.length === 0)
    return null;

  const text = sets.join(' · ');

  return identity?.pronounIsManualOverride
    ? t('profile.facts.setByChannel', { value: text })
    : text;
});
// The bot treats an account under a week old as new (ChatSafetyMeter), so a moderator sees the same flag.
const NEW_ACCOUNT_DAYS = 7;
const newAccount = computed<boolean>(() => {
  const created = props.profile?.identity.accountCreatedAtUtc;

  if (!created)
    return false;

  return Date.now() - new Date(created).getTime() < NEW_ACCOUNT_DAYS * 86_400_000;
});
const standing = computed(() => twitchStanding(props.profile));
const regular = computed(() => isRegular(props.profile));

// How long this person has been around, at a glance: their Twitch account, their follow, their first sighting here.
const ages = computed<Stat[]>(() => {
  const identity = props.profile?.identity;
  const now = new Date();
  const moments = [
    { key: 'lastSeen', at: props.person?.lastSeen },
    { key: 'account', at: identity?.accountCreatedAtUtc },
    { key: 'followed', at: identity?.followedAtUtc },
    { key: 'firstSeen', at: identity?.firstSeenUtc ?? props.person?.firstSeen },
  ];

  return moments
    .map(({ key, at }) => ({
      key,
      label: '',
      value: agoText(at ?? undefined, now, locale.value),
    }))
    .filter((age: Stat) => age.value !== '')
    .map((age: Stat) => ({ ...age, label: t(`profile.age.${age.key}`) }));
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
  <div data-slot="profile-header" class="grid flex-none gap-4" :class="rootClass">
    <div class="flex items-center gap-3">
      <Avatar class="size-12">
        <AvatarImage v-if="who?.profileImageUrl" :src="who.profileImageUrl" :alt="title" />
        <AvatarFallback>{{ title.slice(0, 2).toUpperCase() }}</AvatarFallback>
      </Avatar>
      <div class="grid min-w-0 gap-1">
        <!-- Who first, then when, as a quiet list under the avatar (owner 2026-10-14: "pretty stuffed").
             Pronouns sit by the name: they belong to the person, not a platform. -->
        <div class="flex min-w-0 items-baseline gap-2">
          <template v-if="inDialog">
            <DialogTitle class="truncate text-lg font-semibold">{{ title }}</DialogTitle>
            <DialogDescription class="sr-only">{{
              who ? `@${who.username}` : $t("profile.description")
            }}</DialogDescription>
          </template>
          <h2 v-else class="truncate text-lg font-semibold">{{ title }}</h2>
          <span
            v-if="pronouns"
            data-profile-pronouns
            class="flex-none text-sm text-muted-foreground"
          >{{ pronouns }}</span>
        </div>
        <div
          v-if="roles.length > 0 || standing || regular || newAccount"
          class="flex flex-wrap items-center gap-1.5"
        >
          <!-- The one flag worth a moderator's eye: the account is under a week old. -->
          <Badge v-if="newAccount" data-profile-new-account variant="destructive">
            {{ $t("profile.newAccount") }}
          </Badge>
          <!-- The account status leads, then the roles (owner 2026-10-14). The Twitch logo and each role's own
               Twitch badge keep their colors, so where a badge comes from reads first. -->
          <Badge
            v-if="standing"
            data-profile-standing
            variant="secondary"
            class="bg-muted text-muted-foreground"
          >
            <PlatformLogo data-profile-platform platform="twitch" />
            {{ $t(`profile.twitchStanding.${standing}`) }}
          </Badge>
          <!-- The bot's own Regular tier, earned in this channel. Twitch's default account type is no mark. -->
          <Badge
            v-if="regular"
            data-profile-regular
            variant="secondary"
            class="bg-muted text-muted-foreground"
          >
            {{ $t("profile.trustTier.Regular") }}
          </Badge>
          <Badge
            v-for="role in roles"
            :key="role"
            data-profile-role
            variant="secondary"
            class="bg-muted text-muted-foreground"
          >
            <!-- The stored subscription is Twitch's (Helix seed and reconcile); a YouTube member is not the same.
                 A mod is a mod per platform: the badge carries each platform's logo in its own color, no tint
                 (owner 2026-10-14). -->
            <PlatformLogo
              v-if="role === 'subscriber' || role === 'moderator'"
              data-profile-platform
              platform="twitch"
            />
            <component
              :is="roleIcons[role].icon"
              v-else
              :class="roleIcons[role].color"
              aria-hidden="true"
            />
            {{
              $t(role === "subscriber" ? "profile.roles.subscriberBadge" : `profile.roles.${role}`)
            }}
          </Badge>
        </div>
      </div>
      <StatusBadge
        v-if="person?.isBanned"
        class="ml-auto"
        tone="bad"
        :label="$t('profile.banned')"
      />
    </div>

    <!-- The moments are one quiet line, so the header stays short (owner: "too much on the header"). -->
    <dl
      v-if="ages.length > 0"
      data-profile-ages
      class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground"
    >
      <div
        v-for="age in ages"
        :key="age.key"
        :data-profile-age="age.key"
        class="flex min-w-0 gap-1"
      >
        <dt>{{ age.label }}</dt>
        <dd class="m-0 font-medium text-foreground">{{ age.value }}</dd>
      </div>
    </dl>
    <p v-else-if="who" class="text-xs text-muted-foreground">
      {{ $t("profile.handle", { name: who.username }) }}
    </p>

    <!-- What they did here: one tight row that scrolls sideways on a phone. -->
    <section v-if="stats.length > 0" data-profile-activity>
      <div class="flex gap-4 overflow-x-auto scrollbar-none">
        <div
          v-for="stat in stats"
          :key="stat.key"
          data-profile-stat
          class="grid flex-none content-start gap-0.5"
        >
          <span class="text-xs text-muted-foreground">{{ stat.label }}</span>
          <b class="text-base font-semibold tabular-nums">{{
            typeof stat.value === "number" ? n(stat.value) : stat.value
          }}</b>
        </div>
      </div>
    </section>
  </div>
</template>
