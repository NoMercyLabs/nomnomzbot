<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One legal document (the Privacy Policy or the Terms of Service) as numbered sections. The words sit in this
// page's own i18n file; the company, address and law come from legalFacts, so each is named in one place.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import ReadAloud from '@/components/layout/ReadAloud.vue';
import { Button } from '@/components/ui/button';
import { shortDate } from '@/lib/time';
import { legalFacts, legalPaths } from './legalFacts';

type RawSection = {
  title: string;
  p?: string[];
  list?: string[];
};
type Section = {
  title: string;
  p: string[];
  list: string[];
};

const props = defineProps<{ doc: 'privacy' | 'terms' }>();

const { t, tm, rt, locale } = useI18n();

const effective = computed<string>(() => shortDate(legalFacts.effective, locale.value));

const other = computed<'privacy' | 'terms'>(() =>
  props.doc === 'privacy'
    ? 'terms'
    : 'privacy');

// Placeholders such as {company} take their words from legalFacts, so no document repeats a fact by hand.
function say(text: string): string {
  return rt(text, { ...legalFacts, law: t('site.legal.law') });
}

const sections = computed<Section[]>(() =>
  (tm(`site.legal.${props.doc}.sections`) as RawSection[]).map((raw) => ({
    title: say(raw.title),
    p: (raw.p ?? []).map(say),
    list: (raw.list ?? []).map(say),
  })),
);
</script>

<template>
  <div class="mx-auto flex w-full max-w-3xl flex-col gap-8 px-6 py-8">
    <header class="flex items-center justify-between gap-4">
      <Button as-child variant="ghost">
        <RouterLink to="/">
          <AppMark />
          {{ t('site.legal.back') }}
        </RouterLink>
      </Button>
      <div class="flex items-center gap-2">
        <ReadAloud />
        <LanguagePicker />
      </div>
    </header>

    <main class="flex flex-col gap-8">
      <div class="flex flex-col gap-2">
        <h1 class="text-2xl font-semibold sm:text-4xl">{{ t(`site.legal.${doc}.title`) }}</h1>
        <p class="text-sm text-muted-foreground">
          {{ t('site.legal.effective', { effective }) }}
        </p>
        <!-- English is the original; every other language is a translation and says which text wins. -->
        <p v-if="locale !== 'en'" class="text-sm text-muted-foreground">
          {{ t('site.legal.translation') }}
        </p>
      </div>

      <section
        v-for="(section, index) in sections"
        :key="section.title"
        :data-legal-section="index + 1"
        class="flex flex-col gap-3"
      >
        <h2 class="text-lg font-semibold">{{ `${index + 1}. ${section.title}` }}</h2>
        <p v-for="line in section.p" :key="line" class="text-base leading-relaxed">{{ line }}</p>
        <ul v-if="section.list?.length" class="flex list-disc flex-col gap-2 pl-6 text-base leading-relaxed">
          <li v-for="item in section.list" :key="item">{{ item }}</li>
        </ul>
      </section>
    </main>

    <footer class="border-t border-border pt-4 text-sm text-muted-foreground">
      <RouterLink class="hover:text-foreground" :to="`/${legalPaths[other]}`">{{ t(`site.legal.links.${other}`) }}</RouterLink>
    </footer>
  </div>
</template>
