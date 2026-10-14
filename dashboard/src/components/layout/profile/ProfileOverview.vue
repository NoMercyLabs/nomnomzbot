<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Overview tab (mockup profile.js panes().overview): Identity and Relationship as key-value lists and the
// latest 3 chat lines. A fact the server does not return is left out, never shown as a guess.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import ArrowRight from '~icons/nnz/arrow-right';

import type { CommunityMember } from '@/api/community';
import type { PersonProfile } from '@/api/people';
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import { tierNumber, type RoleKey } from '@/components/layout/profile/profileRoles';
import { Button } from '@/components/ui/button';
import { agoText, clockTime, shortDate } from '@/lib/time';

type ActivityLine = CommunityMember['recentActivity'][number];

const LATEST = 3;

const props = defineProps<{
  person: CommunityMember;
  profile: PersonProfile | null;
  roles: readonly RoleKey[];
}>();

defineEmits<{ showChat: [] }>();

const { t, locale } = useI18n();

type LinkedIdentity = PersonProfile['identity']['linkedIdentities'][number];

function present(item: KeyValueItem): boolean {
  return item.value !== undefined && item.value !== null && item.value !== '';
}

// A person can set a second pronoun set on pronouns.alejo.io; both show, the main one first.
function pronouns(main: string | null | undefined, alt: string | null | undefined): string | null {
  const sets = [main, alt].filter((value): value is string => Boolean(value));

  return sets.length > 0
    ? sets.join(' · ')
    : null;
}

// The other platforms this person signed in with, by name: "Kick: name, YouTube: name".
function linked(identities: readonly LinkedIdentity[]): string | null {
  const names = identities
    .filter((identity: LinkedIdentity) => identity.provider.toLowerCase() !== 'twitch')
    .map((identity: LinkedIdentity) =>
      `${identity.provider}: ${identity.providerDisplayName ?? identity.providerUsername}`);

  return names.length > 0
    ? names.join(', ')
    : null;
}

const identity = computed<KeyValueItem[]>(() => {
  const who = props.person;
  const known = props.profile?.identity;
  const follower = props.profile?.activity?.isFollower;

  return [
    { label: t('profile.facts.name'), value: `${who.displayName} (@${who.username})` },
    { label: t('profile.facts.pronouns'), value: pronouns(known?.pronoun, known?.altPronoun) },
    { label: t('profile.facts.linked'), value: linked(known?.linkedIdentities ?? []) },
    {
      label: t('profile.facts.manages'),
      value: known?.managementRole
        ? t(`profile.manager.${known.managementRole.toLowerCase()}`)
        : null,
    },
    {
      // memberSinceUtc is when this channel gave the person their role (ChannelMembership.GrantedAt).
      label: t('profile.facts.roleSince'),
      value: known?.memberSinceUtc
        ? shortDate(known.memberSinceUtc, locale.value)
        : null,
    },
    {
      label: t('profile.facts.following'),
      value: follower === undefined
        ? null
        : t(`profile.following.${follower}`),
    },
    {
      label: t('profile.facts.trust'),
      value: known
        ? t(`profile.standing.${known.communityStanding.toLowerCase()}`)
        : null,
    },
    { label: t('profile.facts.twitchId'), value: known?.twitchUserId },
  ].filter(present);
});

const roleText = computed<string>(() => {
  const names = props.roles.map((key: string) => t(`profile.roles.${key}`));

  return names.length > 0
    ? names.join(', ')
    : t('profile.roles.viewer');
});

const relationship = computed<KeyValueItem[]>(() => {
  const who = props.person;
  const first = props.profile?.identity.firstSeenUtc ?? who.firstSeen;
  const ago = agoText(who.lastSeen, new Date(), locale.value);
  const tier = tierNumber(props.profile?.identity.subTier);

  return [
    { label: t('profile.facts.roles'), value: roleText.value },
    {
      label: t('profile.facts.subscription'),
      value: tier === null
        ? null
        : t('profile.tier', { tier }),
    },
    {
      label: t('profile.facts.bestStreak'),
      value: props.profile?.streak
        ? t('profile.streakDays', Number(props.profile.streak.maxStreak))
        : null,
    },
    { label: t('profile.facts.firstSeen'), value: shortDate(first, locale.value) },
    { label: t('profile.facts.lastSeen'), value: `${shortDate(who.lastSeen, locale.value)} (${ago})` },
  ].filter(present);
});

const latest = computed<ActivityLine[]>(() => {
  const lines = [...props.person.recentActivity];

  lines.sort((a: ActivityLine, b: ActivityLine) => b.timestamp.localeCompare(a.timestamp));

  return lines.slice(0, LATEST);
});

const now = new Date();
</script>

<template>
  <div data-slot="profile-overview" class="grid gap-6">
    <div class="grid gap-6 sm:grid-cols-2">
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.sections.identity') }}</h3>
        <KeyValueList :items="identity" />
      </section>
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.sections.relationship') }}</h3>
        <KeyValueList :items="relationship" />
      </section>
    </div>

    <section class="grid gap-2">
      <div class="flex items-center justify-between gap-2">
        <h3 class="text-sm font-semibold">{{ $t('profile.chat.title') }}</h3>
        <Button
          v-if="latest.length > 0"
          data-profile-all-chat
          type="button"
          variant="ghost"
          size="sm"
          @click="$emit('showChat')"
        >
          {{ $t('profile.chat.all') }}
          <ArrowRight />
        </Button>
      </div>
      <p v-if="latest.length === 0" class="text-sm text-muted-foreground">
        {{ $t('profile.chat.empty') }}
      </p>
      <ul v-else class="grid gap-1">
        <li
          v-for="line in latest"
          :key="`${line.timestamp}-${line.content}`"
          data-profile-line
          class="grid grid-cols-[auto_minmax(0,1fr)] items-baseline gap-x-3 text-sm"
        >
          <span class="text-xs whitespace-nowrap text-muted-foreground tabular-nums">{{
            clockTime(line.timestamp, now, locale)
          }}</span>
          <span class="truncate" :title="line.content">{{ line.content }}</span>
        </li>
      </ul>
    </section>
  </div>
</template>
