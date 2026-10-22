<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The /login route. While sign-in is closed (see isSignInOpen) it shows "Coming soon" and never asks the
// bot for its providers; with the flag on it is the full sign-in page, unchanged.
import { defineAsyncComponent } from 'vue';
import { useI18n } from 'vue-i18n';

import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { isSignInOpen } from '@/lib/flags';

import ComingSoonButton from '../components/ComingSoonButton.vue';

// The sign-in code loads only when sign-in is open; a closed build never fetches it.
const LoginView = defineAsyncComponent(() => import('./LoginView.vue'));
const open = isSignInOpen();
const { t } = useI18n();
</script>

<template>
  <LoginView v-if="open" />
  <main v-else class="relative flex min-h-svh flex-col items-center justify-center gap-6 p-6">
    <div class="absolute top-4 right-4"><LanguagePicker /></div>
    <h1 class="flex items-center gap-2 text-base font-semibold">
      <AppMark />
      {{ t('site.login.brand') }}
    </h1>
    <PanelCard
      :title="t('site.login.comingSoon.title')"
      :description="t('site.login.comingSoon.body')"
      tier="lead"
      pad
      class="w-full max-w-100"
    >
      <div class="flex flex-col items-center gap-4">
        <ComingSoonButton
          class="w-full"
          :label="t('site.login.comingSoon.label')"
          :note="t('site.login.comingSoon.note')"
          show-note
        />
        <Button as-child variant="ghost" class="w-full">
          <RouterLink to="/">{{ t('site.login.comingSoon.home') }}</RouterLink>
        </Button>
      </div>
    </PanelCard>
  </main>
</template>
