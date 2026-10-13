<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The home header. From md up it shows the links inline; below md they move into a side sheet,
// so a phone header is one line: the brand and one menu button.
import { Menu, X } from '@lucide/vue'
import { useI18n } from 'vue-i18n'

import AppMark from '@/components/layout/AppMark.vue'
import LanguagePicker from '@/components/layout/LanguagePicker.vue'
import { Button } from '@/components/ui/button'
import { Sheet, SheetClose, SheetContent, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'

type Link = { key: string; href: string; external?: boolean }

const { t } = useI18n()
const links: Link[] = [
  { key: 'site.home.nav.features', href: '#outcomes' },
  { key: 'site.home.nav.deploy', href: '#deploy' },
  { key: 'site.home.nav.integrations', href: '#integrations' },
  { key: 'site.home.github', href: t('site.home.githubUrl'), external: true },
]
</script>

<template>
  <header class="flex items-center gap-6">
    <span class="flex items-center gap-2 text-base font-semibold">
      <AppMark />
      {{ t('site.home.brand') }}
    </span>

    <nav :aria-label="t('site.home.nav.label')" class="hidden flex-1 items-center gap-1 text-sm md:flex">
      <a
        v-for="link in links"
        :key="link.key"
        class="rounded-[8px] px-2 py-1 text-muted-foreground hover:bg-muted hover:text-foreground"
        :href="link.href"
        v-bind="link.external ? { target: '_blank', rel: 'noopener' } : {}"
      >{{ t(link.key) }}</a>
    </nav>
    <div class="hidden items-center gap-2 md:flex">
      <LanguagePicker />
      <Button as-child variant="outline">
        <RouterLink :to="{ name: 'login' }">{{ t('site.home.connect') }}</RouterLink>
      </Button>
    </div>

    <Sheet>
      <SheetTrigger as-child>
        <Button variant="outline" size="icon" class="ml-auto md:hidden" :aria-label="t('site.home.nav.open')">
          <Menu aria-hidden="true" />
        </Button>
      </SheetTrigger>
      <SheetContent side="right" :show-close-button="false" class="gap-6 p-6">
        <SheetHeader class="flex-row items-center justify-between gap-2 p-0">
          <SheetTitle class="flex items-center gap-2">
            <AppMark />
            {{ t('site.home.brand') }}
          </SheetTitle>
          <SheetClose as-child>
            <Button variant="ghost" size="icon" :aria-label="t('site.home.nav.close')">
              <X aria-hidden="true" />
            </Button>
          </SheetClose>
        </SheetHeader>
        <nav :aria-label="t('site.home.nav.label')" class="flex flex-col gap-1 text-base">
          <SheetClose v-for="link in links" :key="link.key" as-child>
            <a
              class="rounded-[12px] px-3 py-2 hover:bg-muted"
              :href="link.href"
              v-bind="link.external ? { target: '_blank', rel: 'noopener' } : {}"
            >{{ t(link.key) }}</a>
          </SheetClose>
        </nav>
        <div class="flex flex-col gap-3">
          <LanguagePicker />
          <Button as-child variant="outline">
            <RouterLink :to="{ name: 'login' }">{{ t('site.home.connect') }}</RouterLink>
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  </header>
</template>
