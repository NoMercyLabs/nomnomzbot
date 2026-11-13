<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The page behind an invite link (/invite/<code>). Anyone can read what the invite gives. A visitor who is
// not signed in gets one button that starts the Twitch sign-in with the invite; the bot redeems it on the
// channel that sign-in sets up. A signed-in streamer redeems it on their own channel from here.
import { useQuery } from '@tanstack/vue-query';
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';

import { restoreSession, useSignedIn } from '@/api/auth';
import { publicInviteQuery } from '@/api/billing';
import { signInUrl } from '@/api/signIn';
import PanelCard from '@/components/layout/PanelCard.vue';
import PlatformLogo from '@/components/layout/PlatformLogo.vue';
import { Button } from '@/components/ui/button';
import { rememberInviteGift } from '@/composables/useInviteOutcome';

import PublicTaskShell from '../components/PublicTaskShell.vue';
import TaskActions from '../components/TaskActions.vue';
import InviteRedeem from './components/InviteRedeem.vue';

const route = useRoute();
const { t } = useI18n();
const signedIn = useSignedIn();

const code = computed(() => String(route.params.code));
const invite = useQuery(computed(() => publicInviteQuery(code.value)));

// The page waits for the session check, so a signed-in streamer never sees the sign-in button first.
const sessionChecked = ref(false);

onMounted(async () => {
  await restoreSession();

  sessionChecked.value = true;
});

const pending = computed(() => invite.isPending.value || !sessionChecked.value);
const usable = computed(() => invite.data.value?.isUsable === true);

// What the invite gives, in words: the tier and its length, or the founders badge when there is no tier.
const gift = computed<string>(() => {
  const found = invite.data.value;
  const tier = found?.tierDisplayName;

  if (!found || !tier)
    return t('site.invite.gift.badge');

  if (found.forever)
    return t('site.invite.gift.forever', { tier });

  const months = Number(found.months);

  return t('site.invite.gift.months', { tier, count: months }, months);
});

const title = computed<string>(() => {
  if (pending.value || usable.value || invite.isError.value)
    return t('site.invite.title');

  return t('site.invite.unusable.title');
});

const description = computed<string>(() => {
  if (pending.value)
    return t('site.invite.loading');

  if (invite.isError.value)
    return t('site.invite.failed');

  return usable.value
    ? t('site.invite.gives', { gift: gift.value })
    : t('site.invite.unusable.body');
});

function signIn(): void {
  rememberInviteGift(gift.value);
  window.location.assign(signInUrl('twitch', '/app', 'broadcaster', code.value));
}
</script>

<template>
  <PublicTaskShell :brand="$t('site.invite.brand')">
    <PanelCard
      :title="title"
      :description="description"
      heading="h1"
      tier="lead"
      pad
      class="w-full"
    >
      <p v-if="pending" role="status" class="text-sm text-muted-foreground">
        {{ $t('site.invite.loading') }}
      </p>
      <template v-else-if="usable">
        <InviteRedeem v-if="signedIn" :code="code" :gift="gift" />
        <TaskActions v-else>
          <Button data-action="sign-in" @click="signIn">
            <PlatformLogo platform="twitch" mono />
            {{ $t('site.invite.signIn') }}
          </Button>
        </TaskActions>
      </template>
      <TaskActions v-else>
        <Button as-child variant="outline">
          <RouterLink to="/" data-action="home">{{ $t('site.invite.home') }}</RouterLink>
        </Button>
      </TaskActions>
    </PanelCard>
  </PublicTaskShell>
</template>
