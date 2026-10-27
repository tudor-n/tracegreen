import { Handle, type NodeProps, Position } from '@xyflow/react'
import { memo } from 'react'
import { scenarioCopy } from '@/copy/dashboard'
import { scenarioTitle } from '@/lib/scenario-display'
import { CO2E_UNIT, formatTonnes, formatTonnesDelta, type GhostRfNode } from '@/store'
import { ScenarioBadge } from './ScenarioBadge'

// A simulated change (B5), drawn beside its supplier with a dashed outline and a dashed edge into it, to fill
// NODE_SIZE.supplier. It shows the API's result as it is: the scenario's tonnes, the change against the supplier's
// tonnes now, and the MAC badge. Not selectable: the supplier stays the node the drawer is about.

function GhostNodeView({ data }: NodeProps<GhostRfNode>) {
  const { nodeId, change, result } = data.preview
  const title = scenarioTitle(change)
  return (
    <div className="size-full motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-right-6 motion-safe:duration-300">
      <div
        data-testid={`ghost-${nodeId}`}
        data-badge={result.badge}
        className="ghost-node flex size-full flex-col justify-between overflow-hidden rounded-lg px-2.5 py-2"
      >
        <div className="flex items-center justify-between gap-2">
          <p className="text-[10px] font-medium tracking-wide text-muted-foreground uppercase">{scenarioCopy.whatIf}</p>
          <ScenarioBadge badge={result.badge} />
        </div>
        <p className="truncate text-sm font-medium" title={title}>
          {title}
        </p>
        <div className="flex items-end justify-between gap-2">
          <p className="flex items-center gap-1 leading-none">
            <span className="text-lg font-semibold tabular-nums">{formatTonnes(result.carbonKg)}</span>
            <span className="text-xs text-muted-foreground">{CO2E_UNIT}</span>
          </p>
          <p className="truncate text-xs text-muted-foreground tabular-nums">
            {scenarioCopy.versusNow(`${formatTonnesDelta(result.delta.carbonKg)} ${CO2E_UNIT}`)}
          </p>
        </div>
      </div>
      <Handle type="source" position={Position.Right} isConnectable={false} className="opacity-0" />
    </div>
  )
}

export const GhostNode = memo(GhostNodeView)
