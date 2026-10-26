<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Data tab: what the bot keeps for this person beyond chat. Their consent and permits, the lines the bot
// uses for them, their command use, the quotes about them and any free-form data the channel saved.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useChannelAccess } from '@/api/access';
import { useRevokePermit } from '@/api/permits';
import { teamRoleOf, teamRoles } from '@/api/permissions';
import type { PersonProfile } from '@/api/people';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import ProfileShoutoutLine from '@/components/layout/profile/ProfileShoutoutLine.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { shortDate } from '@/lib/time';

type Quote = PersonProfile['recentQuotes'][number];
type Permit = PersonProfile['permits']['activePermits'][number];

// This DTO sends its enums as numbers, in the server's order (AuthorizationEnums.cs): grant type Role is 0.
const ROLE_GRANT = 0;

const props = defineProps<{
  profile: PersonProfile;
  twitchUserId: string;
  name: string;
  // The header counter: Overview and Data show this one number, not a second count of the usage rows.
  commandsUsed: number;
}>();

const { t, n, locale } = useI18n();

function dateOrNull(value: string | null | undefined): string | null {
  return value
    ? shortDate(value, locale.value)
    : null;
}

const consent = computed<KeyValueItem[]>(() => {
  const permits = props.profile.permits;
  // The API leaves a never-asked consent out of the answer, so missing and null both mean not asked.
  const granted = permits.ageConsentGranted ?? null;

  return [
    {
      label: t('profile.data.ageConsent'),
      value: granted === null
        ? t('profile.data.notAsked')
        : t(`profile.yesNo.${granted}`),
    },
    { label: t('profile.data.ageConsentOn'), value: dateOrNull(permits.ageConsentConfirmedUtc) },
    {
      label: t('profile.data.analytics'),
      value: props.profile.activity?.isAnalyticsOptedOut
        ? t('profile.data.analyticsOff')
        : t('profile.data.analyticsOn'),
    },
  ];
});

// What a permit grants, in words: a role by its name, a single action by its key, and when it ends.
function permitText(permit: Permit): string {
  const role =
    permit.grantType === ROLE_GRANT && permit.grantedRole !== null
      ? teamRoles[Number(permit.grantedRole)]?.toLowerCase()
      : undefined;
  const what = role
    ? t('profile.data.permitRole', { role: t(`profile.manager.${role}`) })
    : t('profile.data.permitAction', { action: permit.capabilityActionKey ?? '' });

  return permit.expiresAt
    ? t('profile.data.permitUntil', { what, date: shortDate(permit.expiresAt, locale.value) })
    : what;
}

const permits = computed<Permit[]>(() => props.profile.permits.activePermits);

// A revoked permit can be issued again, so the button has no confirm.
const { channelId } = useCurrentChannel();
const revoke = useRevokePermit(channelId);
const access = useChannelAccess(channelId);
const canIssue = computed<boolean>(() => access.can('permit:issue'));

// The bot matches a permit by the role's name or the action's key.
function grantOf(permit: Permit): string {
  return permit.grantType === ROLE_GRANT
    ? teamRoleOf(permit.grantedRole ?? 0)
    : (permit.capabilityActionKey ?? '');
}

function revokeOne(permit: Permit): void {
  revoke.mutate(
    {
      userId: permit.userId,
      grants: grantOf(permit),
      twitchUserId: props.twitchUserId,
    },
    {
      onSuccess: () => toast.success(t('profile.data.permitRevoked', { name: props.name })),
      onError: () => toast.error(t('profile.data.permitRevokeFailed', { name: props.name })),
    },
  );
}

const lines = computed<KeyValueItem[]>(() => [
  { label: t('profile.data.ttsVoice'), value: props.profile.overrides.ttsVoice?.voiceId },
]);

const commands = computed<KeyValueItem[]>(() => {
  const usage = props.profile.commandUsage;

  return [
    { label: t('profile.data.commandsUsed'), value: n(props.commandsUsed) },
    { label: t('profile.data.commandLast'), value: dateOrNull(usage.lastUsedUtc) },
    {
      label: t('profile.data.songRequests'),
      value: props.profile.activity
        ? n(Number(props.profile.activity.totalSongRequests))
        : null,
    },
  ];
});

const quotes = computed<Quote[]>(() => props.profile.recentQuotes);
const moreQuotes = computed<number>(
  () => Number(props.profile.totalQuoteCount) - quotes.value.length,
);
</script>

<template>
  <div data-slot="profile-data" class="grid gap-6">
    <div class="grid gap-6 sm:grid-cols-2">
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t("profile.data.consent") }}</h3>
        <KeyValueList :items="consent" />
        <h3 class="pt-2 text-sm font-semibold">{{ $t("profile.data.permits") }}</h3>
        <p v-if="permits.length === 0" class="text-sm text-muted-foreground">
          {{ $t("profile.data.noPermits") }}
        </p>
        <ul v-else data-profile-permits class="grid gap-1 text-sm">
          <li v-for="permit in permits" :key="permit.id" class="flex items-center gap-2">
            <span class="grid min-w-0 flex-1">
              <span>{{ permitText(permit) }}</span>
              <span v-if="permit.reason" class="text-xs text-muted-foreground">{{
                permit.reason
              }}</span>
            </span>
            <Button
              v-if="canIssue"
              variant="outline"
              size="sm"
              :data-permit-revoke="permit.id"
              :disabled="revoke.isPending.value"
              @click="revokeOne(permit)"
            >
              {{ $t("profile.data.permitRevoke") }}
            </Button>
          </li>
        </ul>
      </section>
      <section class="grid content-start gap-2">
        <h3 class="text-sm font-semibold">{{ $t("profile.data.commands") }}</h3>
        <KeyValueList :items="commands" />
      </section>
    </div>

    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t("profile.data.lines") }}</h3>
      <h4 class="text-sm text-muted-foreground">{{ $t("profile.data.shoutout") }}</h4>
      <ProfileShoutoutLine
        :twitch-user-id="twitchUserId"
        :name="name"
        :line="profile.overrides.shoutoutMessageTemplate ?? null"
      />
      <KeyValueList :items="lines" />
    </section>

    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t("profile.data.quotes") }}</h3>
      <p v-if="quotes.length === 0" class="text-sm text-muted-foreground">
        {{ $t("profile.data.noQuotes") }}
      </p>
      <ul v-else data-profile-quotes class="grid gap-1 text-sm">
        <li
          v-for="quote in quotes"
          :key="quote.id"
          class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3"
        >
          <span class="text-muted-foreground tabular-nums">{{
            $t("profile.data.quoteNumber", { number: quote.number })
          }}</span>
          <span class="wrap-anywhere">{{ quote.text }}</span>
        </li>
        <li v-if="moreQuotes > 0" class="text-muted-foreground">
          {{ $t("profile.data.moreQuotes", { count: moreQuotes }, moreQuotes) }}
        </li>
      </ul>
    </section>

    <section class="grid content-start gap-2">
      <h3 class="text-sm font-semibold">{{ $t("profile.data.saved") }}</h3>
      <KeyValueList
        v-if="profile.freeFormData.length > 0"
        :items="profile.freeFormData.map((datum) => ({ label: datum.key, value: datum.value }))"
      />
      <p v-else class="text-sm text-muted-foreground">{{ $t("profile.data.noSaved") }}</p>
    </section>
  </div>
</template>
