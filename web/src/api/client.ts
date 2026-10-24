import createClient from 'openapi-fetch'
import type { components, paths } from './schema'

// Typed by the generated schema (pnpm gen:api). Same origin: /api is proxied by Vite in dev and nginx in the demo image.
export const api = createClient<paths>()

// Aliases of generated types, so components import names instead of index paths. Never hand-write API shapes here.
type Schemas = components['schemas']
export type GraphSnapshot = Schemas['GraphSnapshot']
export type Buyer = Schemas['Buyer']
export type NodeView = Schemas['NodeView']
export type NodeStatus = Schemas['NodeStatus']
export type CarbonFigure = Schemas['CarbonFigure']
export type FactorLookup = Schemas['FactorLookup']
export type ClassificationView = Schemas['ClassificationView']
export type Kpis = Schemas['Kpis']
export type AuditEntry = Schemas['AuditEntry']
export type DomainEvent = Schemas['DomainEvent']
export type DomainEventType = DomainEvent['type']
export type IngestResult = Schemas['IngestResult']
export type ScenarioChange = Schemas['ScenarioChange']
export type ScenarioResult = Schemas['ScenarioResult']
export type ApiProblem = Schemas['ApiProblem']
export type ProblemCode = Schemas['ProblemCode']
export type HealthResponse = Schemas['HealthResponse']
export type CarbonBasis = Schemas['CarbonBasis']
export type Uncertainty = Schemas['Uncertainty']
export type MacBadge = Schemas['MacBadge']
export type TransportMode = Schemas['TransportMode']
export type EnergySource = Schemas['EnergySource']
