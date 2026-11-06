import { QRCodeSVG } from 'qrcode.react'
import { useEffect, useRef, useState } from 'react'
import type { RequestLinkResponse } from '@/api/client'
import { linkCopy } from '@/copy/dashboard'
import { formatDateTime, formatTimeLeft } from '@/store'

// The verification link for the supplier's phone: the QR code, the printed URL (the Playwright smoke reads it from
// `qr-url`), the time left, ruling 6's hotspot note, and a warning when the API built the link from a loopback address.
// The token is part of the URL, so the link lives in the drawer's state only: never logged, never put in the store.

export function LinkCard({ link }: { link: RequestLinkResponse }) {
  const card = useRef<HTMLElement>(null)
  // The card replaces the request card in the panel; bring it into view when the panel is scrolled down.
  useEffect(() => {
    card.current?.scrollIntoView({ block: 'nearest' })
  }, [link])

  return (
    <section ref={card} data-testid="qr-card" className="flex flex-col items-center gap-2.5 rounded-2xl bg-grouped p-[18px] text-center">
      <h3 className="self-start text-[15px] font-semibold">{linkCopy.title}</h3>
      {/* Dark on white in either theme, with a quiet zone, so phone cameras read it. */}
      <div className="rounded-xl bg-white p-1.5 shadow-[0_0_0_0.5px_rgb(0_0_0/0.14)]">
        <QRCodeSVG value={link.url} size={176} marginSize={2} title={linkCopy.qr} />
      </div>
      <p className="text-[13px] text-ink-2">{linkCopy.scan}</p>
      <p data-testid="qr-url" className="w-full font-mono text-[11px] break-all select-all">
        {link.url}
      </p>
      <Expiry expiresAt={link.expiresAt} />
      {link.loopback && (
        <p role="alert" data-testid="qr-loopback" className="rounded-lg bg-hotspot-tint px-2.5 py-1.5 text-xs text-hotspot-ink">
          {linkCopy.loopback}
        </p>
      )}
      <p className="text-xs text-muted-foreground">{linkCopy.hotspot}</p>
    </section>
  )
}

/** The time left, ticking every second on its own so the QR code is not redrawn with it. */
function Expiry({ expiresAt }: { expiresAt: string }) {
  const now = useNow(1000)
  const left = Date.parse(expiresAt) - now
  const expired = left <= 0
  return (
    <p data-testid="qr-expiry" data-expired={expired} className="text-xs font-medium tabular-nums">
      <time dateTime={expiresAt} title={formatDateTime(expiresAt)}>
        {expired ? linkCopy.expired : linkCopy.expiresIn(formatTimeLeft(left))}
      </time>
    </p>
  )
}

function useNow(everyMs: number): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), everyMs)
    return () => clearInterval(timer)
  }, [everyMs])
  return now
}
