import { Handle, type NodeProps, Position } from '@xyflow/react'
import { memo } from 'react'
import { nodeCopy } from '@/copy/dashboard'
import { useChangeCount } from '@/hooks/useChangeCount'
import { aiMappedLabel, isPlaceholder, shownFigure } from '@/lib/node-display'
import { formatTonnes, type SupplierRfNode } from '@/store'
import { FactorInfo } from './FactorInfo'
import { StatusDot } from './StatusDot'

// One supplier on the canvas, a white card drawn to fill NODE_SIZE.supplier: status dot, name and category on the
// left, tonnes and the status word on the right. The selection ring and its glow follow `data-status`, the red outline
// follows `data-hotspot` (index.css); the Playwright smoke reads the same attributes. When the supplier verifies, the
// ring opens to a halo and a green-tint sweep crosses the card. It enters from the left on mount, which for an existing
// id happens only once: React Flow keeps the component across updates. The AI label and a placeholder factor stay on
// the card itself in a small third line, not only in the "i" (CLAUDE.md, PRIORITY-CHANGES.md §2).

function SupplierNodeView({ data, selected }: NodeProps<SupplierRfNode>) {
  const { node, hotspot } = data
  const { factors } = shownFigure(node)
  const ai = aiMappedLabel(node.classification)
  const where = `${node.classification.categoryLabel} · ${node.country}`
  const statusChanges = useChangeCount(node.status)
  const placeholder = factors.some(isPlaceholder)
  return (
    <div
      data-testid={`node-${node.id}`}
      data-status={node.status}
      data-hotspot={hotspot}
      data-selected={selected}
      className="supplier-node flex size-full items-center gap-[11px] rounded-[var(--r-card)] px-3.5"
    >
      {statusChanges > 0 && node.status === 'VERIFIED' && <span key={statusChanges} className="verify-fx" aria-hidden />}
      <StatusDot status={node.status} />
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <p className="truncate text-sm font-semibold" title={node.label}>
          {node.label}
        </p>
        <p className="truncate text-xs text-mute" title={where}>
          {where}
        </p>
        {(ai !== null || placeholder) && (
          <p className="flex min-w-0 gap-1.5 text-[11px] leading-tight">
            {ai !== null && <span className="truncate text-mute">{ai}</span>}
            {placeholder && <span className="flex-none font-semibold text-amber-ink">{nodeCopy.placeholder}</span>}
          </p>
        )}
      </div>
      <div className="flex flex-none flex-col items-end gap-0.5">
        <p className="flex items-center gap-1">
          <span className="text-[15px] font-semibold tracking-[-0.01em]">{nodeCopy.tonnes(formatTonnes(node.carbonKg))}</span>
          <FactorInfo factors={factors} />
        </p>
        <p className="status-word text-[11px] font-semibold" data-status={node.status}>
          {nodeCopy.status[node.status]}
        </p>
      </div>
      {hotspot && <span className="sr-only">, {nodeCopy.hotspot}</span>}
      <Handle type="source" position={Position.Right} isConnectable={false} className="opacity-0" />
    </div>
  )
}

export const SupplierNode = memo(SupplierNodeView)
