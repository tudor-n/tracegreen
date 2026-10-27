import { type CSSProperties, useState } from 'react'
import { cn } from '@/lib/utils'

// The Logo 5 wordmark: "Trace" over a dotted amber trace, "Green" over a solid line cut 45° from amber to green.
// Live text in Geist 600, every measurement in em, so `size` is the only knob. Variants follow the handoff's logo
// motion: `intro` marches the dots, then draws the line in (2.6 s, click replays); `loader` repeats the tracing beat
// for as long as it is shown, with grey dots under "Green"; `static` is the resting logo. Reduced motion shows the
// resting logo (index.css).

export type WordmarkVariant = 'intro' | 'loader' | 'static'

interface WordmarkProps {
  /** Font size in px. The full wordmark is for 17 px and up; use the app icon below that. */
  size?: number
  variant?: WordmarkVariant
  /** The accessible name; null hides the mark from screen readers where the text beside it already says it. */
  label?: string | null
  className?: string
}

export function Wordmark({ size = 22, variant = 'static', label = 'TraceGreen', className }: WordmarkProps) {
  const [run, setRun] = useState(0)
  const style = { fontSize: size } as CSSProperties
  const replay = variant === 'intro' ? () => setRun((n) => n + 1) : undefined
  return (
    <span
      key={run}
      className={cn('wordmark', className)}
      data-variant={variant}
      style={style}
      onClick={replay}
      {...(label === null ? { 'aria-hidden': true } : { role: 'img', 'aria-label': label })}
    >
      <span className="wm-trace" aria-hidden>
        Trace
        <svg className="wm-dots">
          <line x1=".04em" y1="50%" x2="100%" y2="50%" />
        </svg>
      </span>
      <span className="wm-green" aria-hidden>
        <span className="wm-seg wm-gr">
          Gr
          <i className="wm-bar" />
        </span>
        <span className="wm-seg wm-een">
          een
          <i className="wm-bar" />
        </span>
        {variant === 'loader' && (
          <svg className="wm-dots wm-pending">
            <line x1=".17em" y1="50%" x2="100%" y2="50%" />
          </svg>
        )}
      </span>
    </span>
  )
}
