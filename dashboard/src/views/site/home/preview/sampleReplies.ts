// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
