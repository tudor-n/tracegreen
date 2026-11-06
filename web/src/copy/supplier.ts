import type { NodeStatus } from '@/api/client'
import type { DeliveryFailure, PageFailure } from '@/lib/supplier-state'
import { type Lang, verifyCopy } from './verify'

// Every string the supplier reads on /supplier (F12), in Romanian (the default) and English. The questions, their
// answers, "skip" and the "please reconfirm" lines are those of /verify (./verify.ts), so a supplier who used the phone
// page reads the same words here. Both languages are typed by SupplierStrings, so a string missing in either one fails
// `pnpm typecheck`; the status record is keyed by the generated enum, so a new status does too.

interface Message {
  title: string
  body: string
}

export interface SupplierStrings {
  /** The page title, naming the buyer who sent the link. */
  title: (buyer: string) => string
  intro: string
  progress: (confirmed: number, total: number) => string
  empty: string
  /** The word next to each status colour, so colour is never the only cue. */
  status: Record<NodeStatus, string>
  open: string
  close: string
  send: string
  sending: string
  results: Record<PageFailure, Message>
  deliveryFailures: Record<DeliveryFailure, string>
  reload: string
}

const ro = verifyCopy.ro
const en = verifyCopy.en

export const supplierCopy = {
  ro: {
    title: (buyer: string) => `Livrările către ${buyer}`,
    intro: 'Confirmați cum a călătorit fiecare livrare și ce energie folosiți în producție. Toate livrările, la un singur link.',
    progress: (confirmed: number, total: number) =>
      `${confirmed} din ${total} ${total === 1 ? 'confirmată' : 'confirmate'}`,
    empty: 'Nu aveți nimic de confirmat acum.',
    status: { ESTIMATED: 'Așteaptă răspunsul dvs.', PARTIAL: 'Mai e un răspuns', VERIFIED: 'Confirmată' },
    open: 'Confirmați',
    close: 'Mai târziu',
    send: 'Trimite',
    sending: 'Se trimite…',
    results: {
      invalid: ro.results.invalid,
      expired: ro.results.expired,
      gone: {
        title: 'Livrarea nu mai este în listă',
        body: 'Livrarea pentru care a fost creat acest link nu mai apare la cumpărător. Cereți cumpărătorului un link nou.',
      },
      rateLimited: ro.results.rateLimited,
      offline: ro.results.offline,
    },
    deliveryFailures: {
      changed: 'Această livrare s-a schimbat între timp. Reîncărcați lista.',
      rateLimited: ro.results.rateLimited.body,
      offline: ro.results.offline.body,
    },
    reload: 'Reîncarcă lista',
  },
  en: {
    title: (buyer: string) => `Your deliveries to ${buyer}`,
    intro: 'Confirm how each delivery travelled and what powers your production. Every delivery, on one link.',
    progress: (confirmed: number, total: number) => `${confirmed} of ${total} confirmed`,
    empty: 'Nothing to confirm right now.',
    status: { ESTIMATED: 'Waiting for you', PARTIAL: 'One answer left', VERIFIED: 'Confirmed' },
    open: 'Confirm',
    close: 'Not now',
    send: 'Send',
    sending: 'Sending…',
    results: {
      invalid: en.results.invalid,
      expired: en.results.expired,
      gone: {
        title: 'The delivery is no longer listed',
        body: "The delivery this link was made for is no longer on the buyer's list. Ask the buyer for a new link.",
      },
      rateLimited: en.results.rateLimited,
      offline: en.results.offline,
    },
    deliveryFailures: {
      changed: 'This delivery changed in the meantime. Reload the list.',
      rateLimited: en.results.rateLimited.body,
      offline: en.results.offline.body,
    },
    reload: 'Reload the list',
  },
} satisfies Record<Lang, SupplierStrings>
