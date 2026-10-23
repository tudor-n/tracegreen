import { InfoIcon } from 'lucide-react'
import type { KeyboardEvent, MouseEvent } from 'react'
import type { FactorLookup } from '@/api/client'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { nodeCopy } from '@/copy/dashboard'
import { formatFactorValue } from '@/store'

// The "i" beside a number: the factor rows behind it, each with value, unit, source, year and row label. Sources are
// printed exactly as the API sends them, PLACEHOLDER included.

// Inside a node, a click or Enter on the icon would also select the node; the tooltip is all it should open.
const stopClick = (event: MouseEvent) => event.stopPropagation()
const stopSelectKeys = (event: KeyboardEvent) => {
  if (event.key === 'Enter' || event.key === ' ') event.stopPropagation()
}

export function FactorInfo({ factors }: { factors: readonly FactorLookup[] }) {
  return (
    <Tooltip>
      <TooltipTrigger
        aria-label={nodeCopy.factorInfo}
        className="nodrag nopan inline-flex shrink-0 rounded-full text-muted-foreground outline-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring"
        onClick={stopClick}
        onKeyDown={stopSelectKeys}
      >
        <InfoIcon className="size-3.5" aria-hidden />
      </TooltipTrigger>
      <TooltipContent side="right" className="max-w-sm flex-col items-start gap-2 text-left">
        <p className="font-semibold">{nodeCopy.factorsHeading}</p>
        {factors.length === 0 ? (
          <p>{nodeCopy.noFactors}</p>
        ) : (
          <ul className="flex flex-col gap-1.5">
            {factors.map((factor, i) => (
              <li key={i} className="flex flex-col">
                <span className="font-medium">{factor.rowLabel}</span>
                <span className="opacity-80">
                  {factor.source} · {factor.year}
                </span>
                <span className="tabular-nums opacity-80">
                  {formatFactorValue(factor.value)} {factor.unit}
                </span>
              </li>
            ))}
          </ul>
        )}
      </TooltipContent>
    </Tooltip>
  )
}
