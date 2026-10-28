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
