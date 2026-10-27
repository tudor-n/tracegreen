import { type EdgeProps, getBezierPath } from '@xyflow/react'
import { memo } from 'react'
import type { SupplierRfEdge } from '@/store'

// A supply line into the buyer. Width encodes tonnes (selectors' edgeWidth), colour the supplier's status: amber while
// estimated, green with a glow once verified, amber with a dashed green overlay while partly confirmed (index.css).

function StatusEdgeView({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, data }: EdgeProps<SupplierRfEdge>) {
  const [path] = getBezierPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition })
  const status = data?.status ?? 'ESTIMATED'
  const width = data?.width ?? 1.5
  return (
    <g className="supply-line" data-status={status}>
      <path className="supply-line-base" d={path} fill="none" style={{ strokeWidth: width }} />
      {status === 'PARTIAL' && (
        <path className="supply-line-proof" d={path} fill="none" style={{ strokeWidth: width }} />
      )}
    </g>
  )
}

export const StatusEdge = memo(StatusEdgeView)
