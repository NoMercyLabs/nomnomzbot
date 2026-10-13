<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useRoute } from 'vue-router'

import { startSignIn } from '@/api/auth'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

const route = useRoute()

function signIn(): void {
  const returnTo = route.query.return_to
  startSignIn(typeof returnTo === 'string' && returnTo.startsWith('/') ? returnTo : '/app')
}
</script>

<template>
  <Card class="w-full max-w-sm">
    <CardHeader>
      <CardTitle>{{ $t('site.login.title') }}</CardTitle>
      <CardDescription>{{ $t('site.login.description') }}</CardDescription>
    </CardHeader>
    <CardContent>
      <Button class="w-full" @click="signIn">{{ $t('site.login.twitch') }}</Button>
    </CardContent>
  </Card>
</template>
