<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Every sign-in action on the home page. While sign-in is closed (see isSignInOpen) each one is the same
// disabled "Coming soon" button with its note; once it is open each one is a link to the sign-in page.
// A person who is already signed in gets "Open dashboard" instead, whatever the flag says.
import { computed, onMounted } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { restoreSession, useSignedIn } from '@/api/auth';
import { Button } from '@/components/ui/button';
import { isSignInOpen } from '@/lib/flags';

import ComingSoonButton from '../components/ComingSoonButton.vue';

type Props = InstanceType<typeof ComingSoonButton>['$props'];

defineProps<{
  variant?: Props['variant'];
  size?: Props['size'];
  showNote?: boolean;
}>();

const { t } = useI18n();
const open = isSignInOpen();
const signedIn = useSignedIn();

// Only a build that carries the dashboard can have a session. The static public site has no bot behind it,
// so it makes no request and never offers the button.
const hasDashboard: boolean = useRouter().hasRoute('dashboard-home');
const showDashboard = computed<boolean>(() => hasDashboard && signedIn.value);

// The home page has no guard, so a returning person's session is restored here, quietly.
onMounted(() => {
  if (hasDashboard) {
    void restoreSession();
  }
});
</script>

<template>
  <Button v-if="showDashboard" as-child :variant="variant" :size="size">
    <RouterLink to="/app">{{ t('site.home.openDashboard') }}</RouterLink>
  </Button>
  <Button v-else-if="open" as-child :variant="variant" :size="size">
    <RouterLink :to="{ name: 'login' }">{{ t('site.home.signIn') }}</RouterLink>
  </Button>
  <ComingSoonButton
    v-else
    :variant="variant"
    :size="size"
    :show-note="showNote"
    :label="t('site.home.comingSoon')"
    :note="t('site.home.comingSoonNote')"
  />
</template>
