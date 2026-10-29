import type { EnergySource, PlausibilityCode, TransportMode } from '@/api/client'

// Every string the supplier reads on /verify/:nodeId, in Romanian (the default) and English. The page renders them as
// plain text and asks for no personal data. Both languages are typed by VerifyStrings, so a string missing in either
// one fails `pnpm typecheck`; the option and warning records are keyed by the generated enums, so a new mode, energy
// source or plausibility code does too.

export type Lang = 'ro' | 'en'
export const DEFAULT_LANG: Lang = 'ro'

interface Message {
  title: string
  body: string
}

/** The outcomes the page can end on: done, or one of the error states (401, 409, 410 twice, 429, network). */
export type VerifyResult = 'thanks' | 'alreadyUsed' | 'expired' | 'invoiceChanged' | 'invalid' | 'rateLimited' | 'offline'

export interface VerifyStrings {
  title: string
  /** Who asks, from the link's context. */
  intro: (buyer: string) => string
  labels: { supplier: string; item: string; invoice: string; route: string }
  /** The toggle names the other language in that language, so either reader can find it. */
  language: { switchTo: string; switchLabel: string }
  loading: string
  transport: { question: string; options: Record<TransportMode, string> }
  energy: { question: string; options: Record<EnergySource, string> }
  skip: string
  /** Shown instead of a working submit button when both questions are skipped. */
  answerOne: string
  submit: string
  submitting: string
  results: Record<VerifyResult, Message>
  retry: string
  /** The "please reconfirm" banner for plausibility warnings. It never blocks (D2). */
  reconfirm: { title: string; warnings: Record<PlausibilityCode, string> }
}

export const verifyCopy = {
  ro: {
    title: 'Confirmați livrarea',
    intro: (buyer: string) => `${buyer} vă roagă să confirmați două detalii despre această livrare.`,
    labels: { supplier: 'Furnizor', item: 'Articol', invoice: 'Factura', route: 'Traseu' },
    language: { switchTo: 'English', switchLabel: 'Afișează pagina în engleză' },
    loading: 'Se încarcă…',
    transport: {
      question: 'Cum a fost transportată această livrare?',
      options: { road: 'Rutier', rail: 'Feroviar', sea: 'Maritim', air: 'Aerian' },
    },
    energy: {
      question: 'Ce energie folosiți în producție?',
      options: { grid: 'Rețeaua electrică', renewable: 'Majoritar regenerabilă', unknown: 'Nu știu' },
    },
    skip: 'Sari peste',
    answerOne: 'Răspundeți la cel puțin o întrebare.',
    submit: 'Trimite',
    submitting: 'Se trimite…',
    results: {
      thanks: {
        title: 'Mulțumim',
        body: 'Răspunsurile au fost trimise cumpărătorului. Puteți închide această pagină.',
      },
      alreadyUsed: {
        title: 'Acest link a fost deja folosit',
        body: 'Răspunsurile pentru acest articol au fost deja trimise. Fiecare link funcționează o singură dată.',
      },
      expired: {
        title: 'Acest link a expirat',
        body: 'Cereți cumpărătorului un link nou.',
      },
      invoiceChanged: {
        title: 'Factura s-a modificat',
        body: 'Acest link a fost creat pentru o versiune anterioară a facturii. Cereți cumpărătorului un link nou.',
      },
      invalid: {
        title: 'Acest link nu este valid',
        body: 'Verificați dacă ați deschis linkul complet sau cereți cumpărătorului unul nou.',
      },
      rateLimited: {
        title: 'Prea multe încercări',
        body: 'Așteptați un minut, apoi încercați din nou.',
      },
      offline: {
        title: 'Fără conexiune',
        body: 'Verificați conexiunea la internet și încercați din nou.',
      },
    },
    retry: 'Încercați din nou',
    reconfirm: {
      title: 'Vă rugăm să reconfirmați',
      warnings: {
        AIR_HEAVY:
          'Transportul aerian este neobișnuit pentru o livrare atât de grea. Dacă marfa nu a fost transportată pe calea aerului, anunțați persoana de contact de la cumpărător.',
        SEA_TOO_FAST:
          'Datele de livrare par prea apropiate pentru transport maritim. Dacă marfa nu a fost transportată pe mare, anunțați persoana de contact de la cumpărător.',
      },
    },
  },
  en: {
    title: 'Confirm the delivery',
    intro: (buyer: string) => `${buyer} asks you to confirm two details about this delivery.`,
    labels: { supplier: 'Supplier', item: 'Item', invoice: 'Invoice', route: 'Route' },
    language: { switchTo: 'Română', switchLabel: 'Show this page in Romanian' },
    loading: 'Loading…',
    transport: {
      question: 'How did this shipment travel?',
      options: { road: 'Road', rail: 'Rail', sea: 'Sea', air: 'Air' },
    },
    energy: {
      question: 'What powers your production?',
      options: { grid: 'Grid', renewable: 'Mostly renewable', unknown: "Don't know" },
    },
    skip: 'Skip',
    answerOne: 'Answer at least one question.',
    submit: 'Submit',
    submitting: 'Sending…',
    results: {
      thanks: {
        title: 'Thank you',
        body: 'Your answers have been sent to the buyer. You can close this page.',
      },
      alreadyUsed: {
        title: 'This link has already been used',
        body: 'The answers for this item were already sent. Each link works once.',
      },
      expired: {
        title: 'This link has expired',
        body: 'Ask the buyer for a new link.',
      },
      invoiceChanged: {
        title: 'The invoice has changed',
        body: 'This link was made for an earlier version of the invoice. Ask the buyer for a new link.',
      },
      invalid: {
        title: 'This link is not valid',
        body: 'Check that you opened the whole link, or ask the buyer for a new one.',
      },
      rateLimited: {
        title: 'Too many attempts',
        body: 'Wait a minute, then try again.',
      },
      offline: {
        title: 'No connection',
        body: 'Check your internet connection and try again.',
      },
    },
    retry: 'Try again',
    reconfirm: {
      title: 'Please reconfirm',
      warnings: {
        AIR_HEAVY:
          'Air freight is unusual for a shipment this heavy. If the goods did not travel by air, please tell your contact at the buyer.',
        SEA_TOO_FAST:
          'The delivery dates look too short for sea freight. If the goods did not travel by sea, please tell your contact at the buyer.',
      },
    },
  },
} satisfies Record<Lang, VerifyStrings>
