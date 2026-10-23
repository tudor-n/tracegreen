import { Handle, type NodeProps, Position } from '@xyflow/react'
import { CircleCheckIcon } from 'lucide-react'
import { memo } from 'react'
import { nodeCopy } from '@/copy/dashboard'
import { aiMappedLabel, isPlaceholder, shownFigure } from '@/lib/node-display'
import { CO2E_UNIT, formatTonnes, type SupplierRfNode } from '@/store'
import { FactorInfo } from './FactorInfo'

// One supplier on the canvas, drawn to fill NODE_SIZE.supplier. Tone and pattern follow `data-status`, the ring follows
// `data-hotspot` (index.css); the Playwright smoke reads the same attributes. It enters with a short slide on mount,
// which for an existing id happens only once: React Flow keeps the component across updates. A number that rests on a
// PLACEHOLDER factor says so on the node itself, not only in the "i" (PRIORITY-CHANGES.md §2).

function SupplierNodeView({ data, selected }: NodeProps<SupplierRfNode>) {
  const { node, hotspot } = data
  const { factors } = shownFigure(node)
  const ai = aiMappedLabel(node.classification)
  const where = `${node.country} · ${node.classification.categoryLabel}`
  return (
    <div
      data-testid={`node-${node.id}`}
      data-status={node.status}
      data-hotspot={hotspot}
      data-selected={selected}
      className="supplier-node flex size-full flex-col justify-between overflow-hidden rounded-lg px-2.5 py-2 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-left-6 motion-safe:duration-500"
    >
      <div className="flex items-center gap-1.5">
        <p className="min-w-0 flex-1 truncate text-sm font-medium" title={node.label}>
          {node.label}
        </p>
        <FactorInfo factors={factors} />
      </div>
      <div className="flex items-center gap-1.5">
        <p className="min-w-0 flex-1 truncate text-xs text-muted-foreground" title={where}>
          {where}
        </p>
        {factors.some(isPlaceholder) && (
          <span className="shrink-0 rounded bg-destructive/10 px-1 text-[10px] font-semibold text-destructive">
            {nodeCopy.placeholder}
          </span>
        )}
      </div>
      <div className="flex items-end justify-between gap-2">
        <p className="flex items-center gap-1 leading-none">
          <span className="text-lg font-semibold tabular-nums">{formatTonnes(node.carbonKg)}</span>
          <span className="text-xs text-muted-foreground">{CO2E_UNIT}</span>
          {node.status === 'VERIFIED' && <CircleCheckIcon className="size-4 text-(--status-verified)" aria-hidden />}
        </p>
        {ai !== null && (
          <span className="shrink-0 truncate rounded-full bg-background/70 px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
            {ai}
          </span>
        )}
      </div>
      <span className="sr-only">
        {nodeCopy.status[node.status]}
        {hotspot && `, ${nodeCopy.hotspot}`}
      </span>
      <Handle type="source" position={Position.Right} isConnectable={false} className="opacity-0" />
    </div>
  )
}

export const SupplierNode = memo(SupplierNodeView)
