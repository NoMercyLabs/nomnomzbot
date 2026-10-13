<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The detail beside the People list: what the bot knows about one person, read only.
// The row supplies the facts every person has. The profile endpoint adds the rest, but only
// for a person with an internal id; the full profile stays the ProfileDialog (UserName opens it).
import ArrowLeft from '~icons/nnz/arrow-left'
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { useViewerProfile, type CommunityRow, type PersonProfile } from '@/api/people'
import DetailPane from '@/components/layout/DetailPane.vue'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import StatStrip from '@/components/layout/StatStrip.vue'
import StatTile from '@/components/layout/StatTile.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import UserName from '@/components/layout/UserName.vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

const props = defineProps<{ person: CommunityRow }>()
defineEmits<{ back: [] }>()

const { t, n, locale } = useI18n()
const { channelId } = useCurrentChannel()

const profile = useViewerProfile(channelId, () => props.person.internalUserId)
const hasProfile: ComputedRef<boolean> = computed<boolean>(
  () => props.person.internalUserId !== null,
)
const profileState = usePanelState(profile, (data: PersonProfile | null) => data === null)

const moderationState: ComputedRef<PanelState> = computed<PanelState>(() => {
  if (!hasProfile.value) return 'empty'
  if (profileState.value === 'ready' && profile.data.value?.moderationHistory == null)
    return 'empty'
  return profileState.value
})
const moderationEmpty: ComputedRef<string> = computed<string>(() =>
  t(
    hasProfile.value
      ? 'dashboard.people.detail.moderation.empty'
      : 'dashboard.people.detail.noProfile',
  ),
)

// Twitch sends the tier as a plan code: 1000, 2000, 3000, or Prime.
function tierName(code: string | null): string | null {
  if (code === null) return null
  const level: RegExpMatchArray | null = /^([123])000$/.exec(code)
  if (level) return t('dashboard.people.detail.overview.tierLevel', { level: level[1] })
  return code.toLowerCase() === 'prime' ? t('dashboard.people.detail.overview.tierPrime') : code
}

function shortDate(value: string | null | undefined): string | null {
  if (!value) return null
  return new Date(value).toLocaleDateString(locale.value, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

const handle: ComputedRef<string> = computed<string>(() => `@${props.person.username}`)
const watchHours: ComputedRef<string> = computed<string>(() =>
  n(Number(props.person.watchHours), { maximumFractionDigits: 1 }),
)

const overview: ComputedRef<KeyValueItem[]> = computed<KeyValueItem[]>(() => {
  const items: KeyValueItem[] = [
    { label: t('dashboard.people.detail.overview.login'), value: `@${props.person.username}` },
    { label: t('dashboard.people.detail.overview.twitchId'), value: props.person.id },
    {
      label: t('dashboard.people.detail.overview.firstSeen'),
      value: shortDate(props.person.firstSeen),
    },
    {
      label: t('dashboard.people.detail.overview.lastSeen'),
      value: shortDate(props.person.lastSeen),
    },
  ]
  const data: PersonProfile | null | undefined = profile.data.value
  if (!data) return items
  return [
    ...items.slice(0, 2),
    {
      label: t('dashboard.people.detail.overview.pronoun'),
      value: data.identity.pronoun ?? t('dashboard.people.detail.overview.pronounUnset'),
    },
    {
      label: t('dashboard.people.detail.overview.standing'),
      value: data.identity.communityStanding,
    },
    { label: t('dashboard.people.detail.overview.tier'), value: tierName(data.identity.subTier) },
    ...(data.activity
      ? [
          {
            label: t('dashboard.people.detail.overview.follower'),
            value: t(
              data.activity.isFollower
                ? 'dashboard.people.detail.overview.yes'
                : 'dashboard.people.detail.overview.no',
            ),
          },
        ]
      : []),
    ...items.slice(2),
  ]
})

const moderation: ComputedRef<KeyValueItem[]> = computed<KeyValueItem[]>(() => {
  const history: PersonProfile['moderationHistory'] | undefined =
    profile.data.value?.moderationHistory
  if (!history) return []
  return [
    {
      label: t('dashboard.people.detail.moderation.timeouts'),
      value: n(Number(history.timeoutCount)),
    },
    { label: t('dashboard.people.detail.moderation.bans'), value: n(Number(history.banCount)) },
    {
      label: t('dashboard.people.detail.moderation.warnings'),
      value: n(Number(history.warningCount)),
    },
    {
      label: t('dashboard.people.detail.moderation.deleted'),
      value: n(Number(history.messagesDeletedCount)),
    },
    {
      label: t('dashboard.people.detail.moderation.lastAction'),
      value: history.lastActionType
        ? `${history.lastActionType} · ${shortDate(history.lastActionAt) ?? ''}`
        : null,
    },
  ]
})

const streak: ComputedRef<PersonProfile['streak'] | undefined> = computed(
  () => profile.data.value?.streak,
)
</script>

<template>
  <DetailPane data-person-detail>
    <template #header>
      <div class="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon-sm"
          class="md:hidden"
          :aria-label="$t('dashboard.people.back')"
          @click="$emit('back')"
        >
          <ArrowLeft />
        </Button>
        <Avatar class="size-12">
          <AvatarImage
            v-if="person.profileImageUrl"
            :src="person.profileImageUrl"
            :alt="person.displayName"
          />
          <AvatarFallback>{{ person.displayName.slice(0, 2).toUpperCase() }}</AvatarFallback>
        </Avatar>
        <div class="grid min-w-0 gap-0.5">
          <UserName class="truncate text-lg" :user-id="person.id" :name="person.displayName" />
          <span class="truncate text-sm text-muted-foreground">{{ handle }}</span>
        </div>
        <StatusBadge
          v-if="person.isBanned"
          class="ml-auto"
          tone="bad"
          :label="$t('dashboard.people.list.banned')"
        />
      </div>
    </template>

    <StatStrip>
      <StatTile
        :label="$t('dashboard.people.detail.stats.messages')"
        :value="Number(person.messageCount)"
      />
      <StatTile :label="$t('dashboard.people.detail.stats.watchHours')" :value="watchHours" />
      <StatTile
        :label="$t('dashboard.people.detail.stats.commands')"
        :value="Number(person.commandsUsed)"
      />
      <StatTile
        v-if="streak"
        :label="$t('dashboard.people.detail.stats.streak')"
        :value="Number(streak.currentStreak)"
        :context="
          $t('dashboard.people.detail.stats.streakContext', { max: Number(streak.maxStreak) })
        "
      />
    </StatStrip>

    <PanelCard tier="lead" pad :title="$t('dashboard.people.detail.overview.title')">
      <KeyValueList :items="overview" />
    </PanelCard>
    <PanelCard
      pad
      :title="$t('dashboard.people.detail.moderation.title')"
      :state="moderationState"
      :empty-text="moderationEmpty"
      :error-text="$t('dashboard.people.detail.profileError')"
      @retry="profile.refetch()"
    >
      <KeyValueList :items="moderation" />
    </PanelCard>
    <PanelCard
      tier="quiet"
      :title="$t('dashboard.people.detail.chat.title')"
      state="empty"
      :empty-text="$t('dashboard.people.detail.chat.empty')"
    />
    <PanelCard
      tier="quiet"
      :title="$t('dashboard.people.detail.loyalty.title')"
      state="empty"
      :empty-text="$t('dashboard.people.detail.loyalty.empty')"
    />
  </DetailPane>
</template>
