<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The small print of a sign-in: one sentence that names the terms and the privacy policy as links to the real
// legal pages, so a visitor can read them before agreeing.
import { legalPaths } from '@/views/site/legal/legalFacts';
</script>

<template>
  <i18n-t
    keypath="site.login.terms"
    tag="p"
    class="text-center text-xs text-balance text-muted-foreground"
  >
    <template #terms>
      <RouterLink :to="`/${legalPaths.terms}`" class="underline underline-offset-4 hover:text-foreground">
        {{ $t('site.login.termsLink') }}
      </RouterLink>
    </template>
    <template #privacy>
      <RouterLink :to="`/${legalPaths.privacy}`" class="underline underline-offset-4 hover:text-foreground">
        {{ $t('site.login.privacyLink') }}
      </RouterLink>
    </template>
  </i18n-t>
</template>
