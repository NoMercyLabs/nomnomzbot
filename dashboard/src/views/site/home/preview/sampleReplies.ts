// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { merge } from '@/lib/array';

import { analyticsReplies } from './replies/analytics';
import { baseReplies } from './replies/base';
import { commandReplies } from './replies/commands';
import { economyReplies } from './replies/economy';
import { integrationReplies } from './replies/integrations';
import { moderationReplies } from './replies/moderation';
import { musicReplies } from './replies/music';
import { pipelineReplies } from './replies/pipelines';
import { ttsReplies } from './replies/tts';
import { widgetReplies } from './replies/widgets';

// Every answer the fake data layer can give: the sample channel and, per area, the records its real page reads.
// All of it is invented sample data (see sampleChannel.ts); nothing is read from a real channel.
export const sampleReplies = merge(
  baseReplies,
  commandReplies,
  pipelineReplies,
  moderationReplies,
  widgetReplies,
  analyticsReplies,
  economyReplies,
  musicReplies,
  ttsReplies,
  integrationReplies,
);
