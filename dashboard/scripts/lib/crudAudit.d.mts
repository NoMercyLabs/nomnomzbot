// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

export type CrudOperation = 'create' | 'edit' | 'delete';

export interface CrudEndpoint {
  key: string;
  path: string;
  verb: 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  operation: CrudOperation;
  called: boolean;
  inferred: boolean;
  files: string[];
}

export interface OperationCounts {
  offered: number;
  called: number;
}

export interface CrudEntity {
  entity: string;
  modules: string[];
  rows: CrudEndpoint[];
  perOperation: Record<CrudOperation, OperationCounts | null>;
  gaps: CrudOperation[];
}

export interface CrudTotals {
  entities: number;
  endpointsOffered: number;
  endpointsCalled: number;
  operationsOffered: number;
  operationsCalled: number;
}

export const operations: CrudOperation[];
export function auditCrud(): CrudEntity[];
export function totals(entities: CrudEntity[]): CrudTotals;
