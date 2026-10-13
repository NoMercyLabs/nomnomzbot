<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The public home page (V20). One primary action per view region: the hero and the closing call
// carry the pink button; the header and the hosting card use outline buttons.
import { useI18n } from 'vue-i18n';

import { Button } from '@/components/ui/button';

import DeploySection from './DeploySection.vue';
import HomeHeader from './HomeHeader.vue';
import OutcomeBlocks from './OutcomeBlocks.vue';
import ShellDemo from './ShellDemo.vue';

const integrations: string[] = ['spotify', 'discord', 'youtube', 'tts', 'obs'];
const { t } = useI18n();
</script>

<template>
  <div class="mx-auto flex w-full max-w-5xl flex-col gap-16 px-6 py-8">
    <HomeHeader />

    <!-- Spacing by proximity: 12 inside a group, 24 between groups, 48 to the demo, 96 between sections. -->
    <main class="flex flex-col gap-24">
      <section class="flex flex-col items-center gap-12 pt-8 text-center">
        <div class="flex flex-col items-center gap-6">
          <div class="flex flex-col items-center gap-3">
            <h1 class="max-w-2xl text-balance text-2xl font-semibold sm:text-4xl">
              {{ t('site.home.hero.title') }}
            </h1>
            <p class="max-w-2xl text-balance text-base text-muted-foreground">
              {{ t('site.home.hero.subtitle') }}
            </p>
          </div>
          <div class="flex flex-col items-center gap-3">
            <div class="flex flex-wrap justify-center gap-2">
              <Button as-child size="lg">
                <RouterLink :to="{ name: 'login' }">{{ t('site.home.connect') }}</RouterLink>
              </Button>
              <Button as-child size="lg" variant="ghost">
                <a :href="t('site.home.githubUrl')" target="_blank" rel="noopener">{{
                  t('site.home.viewSource')
                }}</a>
              </Button>
            </div>
            <p class="text-sm text-muted-foreground">{{ t('site.home.hero.finePrint') }}</p>
          </div>
        </div>
        <ShellDemo />
      </section>

      <div class="flex flex-col gap-12">
        <p class="text-center text-base text-muted-foreground">
          {{ t('site.home.audience.line') }}
        </p>
        <OutcomeBlocks />
      </div>
      <DeploySection />

      <section id="integrations" class="flex scroll-mt-6 flex-col items-center gap-3 text-center">
        <p class="text-sm text-muted-foreground">{{ t('site.home.integrations.title') }}</p>
        <ul class="flex flex-wrap justify-center gap-2">
          <li
            v-for="name in integrations"
            :key="name"
            class="rounded-[8px] border border-border bg-card px-3 py-1 text-sm font-medium"
          >
            {{ t(`site.home.integrations.${name}`) }}
          </li>
        </ul>
      </section>

      <section class="flex flex-col items-center gap-3 text-center">
        <h2 class="max-w-2xl text-balance text-2xl font-semibold">
          {{ t('site.home.cta.title') }}
        </h2>
        <p class="text-base text-muted-foreground">{{ t('site.home.cta.body') }}</p>
        <Button as-child size="lg">
          <RouterLink :to="{ name: 'login' }">{{ t('site.home.connect') }}</RouterLink>
        </Button>
      </section>
    </main>

    <footer
      class="flex flex-wrap items-center justify-between gap-2 border-t border-border pt-4 text-sm text-muted-foreground"
    >
      <span>{{ t('site.home.footer.text') }}</span>
      <a
        class="hover:text-foreground"
        :href="t('site.home.githubUrl')"
        target="_blank"
        rel="noopener"
      >{{ t('site.home.github') }}</a>
    </footer>
  </div>
</template>
