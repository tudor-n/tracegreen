import { Handle, type NodeProps, Position } from '@xyflow/react'
import { memo } from 'react'
import { nodeCopy } from '@/copy/dashboard'
import { aiMappedLabel, isPlaceholder, shownFigure } from '@/lib/node-display'
import { CO2E_UNIT, formatTonnes, type SupplierRfNode } from '@/store'
import { FactorInfo } from './FactorInfo'
import { StatusChip } from './StatusChip'

// One supplier on the canvas, a white card drawn to fill NODE_SIZE.supplier. The status chip, the selection ring and its
// glow follow `data-status`, the red outline follows `data-hotspot` (index.css); the Playwright smoke reads the same
// attributes. It enters from the left on mount,
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
      className="supplier-node flex size-full flex-col justify-between rounded-[var(--r-card)] px-3.5 py-2.5"
    >
      <div className="flex items-center gap-1.5">
        <p className="min-w-0 flex-1 truncate text-[13px] font-semibold" title={node.label}>
          {node.label}
        </p>
        <FactorInfo factors={factors} />
      </div>
      <p className="truncate text-xs text-mute" title={where}>
        {where}
      </p>
      <div className="flex items-center justify-between gap-2">
        <p className="flex items-baseline gap-1 leading-none">
          <span className="font-heading text-[22px] font-semibold tracking-[-0.02em]">{formatTonnes(node.carbonKg)}</span>
          <span className="text-xs font-medium text-mute">{CO2E_UNIT}</span>
        </p>
        <StatusChip status={node.status} />
      </div>
      <div className="flex min-h-[18px] items-center gap-1.5 overflow-hidden">
        {hotspot && <span className="tag tag-hotspot">{nodeCopy.hotspotTag}</span>}
        {factors.some(isPlaceholder) && <span className="tag tag-estimated">{nodeCopy.placeholder}</span>}
        {ai !== null && <span className="tag truncate">{ai}</span>}
      </div>
      {hotspot && <span className="sr-only">, {nodeCopy.hotspot}</span>}
      <Handle type="source" position={Position.Right} isConnectable={false} className="opacity-0" />
    </div>
  )
}

export const SupplierNode = memo(SupplierNodeView)
