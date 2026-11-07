import type { EnergyCertType, EnergySource, PlausibilityCode, ShippingDocType, TransportMode } from '@/api/client'
import type { DispatchDateProblem } from '@/lib/evidence'

// Every string the supplier reads on /verify/:nodeId, in Romanian (the default) and English. The page renders them as
// plain text and asks for no personal data. Both languages are typed by VerifyStrings, so a string missing in either
// one fails `pnpm typecheck`; the option and warning records are keyed by the generated enums, so a new mode, energy
// source, plausibility code, shipping document or certificate type does too.

export type Lang = 'ro' | 'en'
export const DEFAULT_LANG: Lang = 'ro'

interface Message {
  title: string
  body: string
}

/** The outcomes the page can end on: done, or one of the error states (401, 409, 410 twice, 422 terms, 429, network). */
export type VerifyResult =
  | 'thanks'
  | 'alreadyUsed'
  | 'expired'
  | 'invoiceChanged'
  | 'invalid'
  | 'termsChanged'
  | 'rateLimited'
  | 'offline'

/**
 * The optional "Add proof" section under the two questions (F7), shared with /supplier. It names a document and its
 * number, never promises an outcome, and says what is wrong with a number in the supplier's language (the API's own
 * messages are English and are not shown). Each number field is labelled by its document or certificate type.
 */
export interface ProofStrings {
  /** The disclosure's label; the section starts collapsed and empty. */
  toggle: string
  /** One line on why a number helps. */
  why: string
  docTypes: Record<ShippingDocType, string>
  /** The small control that picks another document, shown when more than one fits the transport answer. */
  otherDoc: string
  dispatchDate: string
  certTypes: Record<EnergyCertType, string>
  /** The small control that picks another certificate type. */
  otherCert: string
  /** The demo-tools button that fills in sample numbers (shown only when the API runs with demo tools). */
  demoFill: string
  /** The tag next to a sample number, so nobody takes it for a supplier's real document. */
  demoTag: string
  problems: {
    docIdShape: Record<ShippingDocType, string>
    checkDigit: string
    dispatchDate: Record<DispatchDateProblem, string>
    certIdShape: string
  }
  /** Shown under the questions while a proof field has a problem, which holds the answers back. */
  fixToSend: string
  /** Shown under the proof fields when the API refused the proof sent (InvalidEvidence); the answers were not sent. */
  refused: string
}

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
  proof: ProofStrings
  /**
   * The frame around the supplier terms. The terms themselves (title, key points, the checkbox statement with its link,
   * and the sections) come from the API, which hashes exactly that text, so none of it is written here.
   */
  terms: {
    version: (version: string) => string
    /** Read by screen readers after the link in the checkbox statement. */
    opensInNewTab: string
    /** Shown under the submit button while the terms are not ticked. */
    tickToSubmit: string
  }
  /** The printable terms page /terms/supplier. */
  termsPage: {
    loading: string
    fingerprint: string
    print: string
    failed: Message
  }
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
      termsChanged: {
        title: 'Termenii au fost actualizați',
        body: 'Citiți versiunea nouă a termenilor, apoi trimiteți din nou răspunsurile.',
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
        MASS_BELOW_INVOICE:
          'Greutatea confirmată este mai mică decât greutatea mărfii de pe factură. Dacă nu este corect, anunțați persoana de contact de la cumpărător.',
      },
    },
    proof: {
      toggle: 'Adăugați o dovadă (opțional)',
      why: 'Un număr de document permite ca răspunsul să fie luat în calcul integral.',
      docTypes: {
        containerNumber: 'Număr de container',
        airWaybill: 'Scrisoare de transport aerian (AWB)',
        billOfLading: 'Conosament (B/L)',
        cmr: 'Scrisoare de trăsură CMR',
        eTransportUit: 'Cod UIT e-Transport',
        railConsignment: 'Scrisoare de trăsură feroviară (CIM/SMGS)',
      },
      otherDoc: 'Alt document',
      dispatchDate: 'Data expedierii',
      certTypes: {
        guaranteeOfOrigin: 'Garanție de origine',
        iRec: 'I-REC',
        chinaGec: 'China GEC',
        taiwanTRec: 'Taiwan T-REC',
        ppa: 'Contract de achiziție de energie (PPA)',
        onSite: 'Producție proprie la fața locului',
      },
      otherCert: 'Alt tip',
      demoFill: 'Completați cu valori demo',
      demoTag: 'valori demo',
      problems: {
        docIdShape: {
          containerNumber: 'Un număr de container are 4 litere, ultima fiind U, J sau Z, și 7 cifre, de exemplu CSQU3054383.',
          airWaybill: 'Un număr AWB are 11 cifre, de exemplu 020-12345675.',
          billOfLading: 'Numărul conosamentului are între 6 și 20 de caractere (litere, cifre, - sau /).',
          cmr: 'Numărul CMR are între 4 și 20 de caractere (litere, cifre, - sau /).',
          eTransportUit: 'Codul UIT are între 8 și 20 de caractere (litere, cifre, - sau /).',
          railConsignment: 'Numărul scrisorii de trăsură are între 4 și 20 de caractere (litere, cifre, - sau /).',
        },
        checkDigit: 'Cifra de control nu se potrivește. Verificați dacă numărul este scris corect.',
        dispatchDate: {
          invalid: 'Introduceți o dată completă.',
          future: 'Data este în viitor.',
          tooEarly: 'Data este cu peste un an înaintea facturii.',
        },
        certIdShape: 'Folosiți între 4 și 64 de caractere (litere, cifre, - _ . / :).',
      },
      fixToSend: 'Corectați câmpurile dovezii de mai sus sau lăsați-le goale pentru a trimite.',
      refused:
        'Dovada nu a putut fi acceptată, așa că răspunsurile nu au fost trimise. Verificați câmpurile de mai sus sau ștergeți-le, apoi trimiteți din nou.',
    },
    terms: {
      version: (version: string) => `Versiunea ${version}`,
      opensInNewTab: 'se deschide într-o filă nouă',
      tickToSubmit: 'Bifați caseta de mai sus pentru a trimite răspunsurile.',
    },
    termsPage: {
      loading: 'Se încarcă termenii…',
      fingerprint: 'Amprenta SHA-256',
      print: 'Tipăriți sau salvați',
      failed: {
        title: 'Termenii nu au putut fi încărcați',
        body: 'Verificați conexiunea la internet și încercați din nou.',
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
      termsChanged: {
        title: 'The terms have been updated',
        body: 'Read the new version of the terms, then send your answers again.',
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
        MASS_BELOW_INVOICE:
          'The confirmed weight is lower than the weight of the goods on the invoice. If that is not right, please tell your contact at the buyer.',
      },
    },
    proof: {
      toggle: 'Add proof (optional)',
      why: 'A document number lets your answer count in full.',
      docTypes: {
        containerNumber: 'Container number',
        airWaybill: 'Air waybill (AWB)',
        billOfLading: 'Bill of lading',
        cmr: 'CMR consignment note',
        eTransportUit: 'e-Transport UIT code',
        railConsignment: 'Rail consignment note (CIM/SMGS)',
      },
      otherDoc: 'Other document',
      dispatchDate: 'Dispatch date',
      certTypes: {
        guaranteeOfOrigin: 'Guarantee of origin',
        iRec: 'I-REC',
        chinaGec: 'China GEC',
        taiwanTRec: 'Taiwan T-REC',
        ppa: 'Power purchase agreement (PPA)',
        onSite: 'Own generation on site',
      },
      otherCert: 'Other type',
      demoFill: 'Fill demo values',
      demoTag: 'demo values',
      problems: {
        docIdShape: {
          containerNumber: 'A container number is 4 letters, the last one U, J or Z, and 7 digits, like CSQU3054383.',
          airWaybill: 'An air waybill number is 11 digits, like 020-12345675.',
          billOfLading: 'A bill of lading number is 6 to 20 characters (letters, digits, - or /).',
          cmr: 'A CMR number is 4 to 20 characters (letters, digits, - or /).',
          eTransportUit: 'A UIT code is 8 to 20 characters (letters, digits, - or /).',
          railConsignment: 'A consignment note number is 4 to 20 characters (letters, digits, - or /).',
        },
        checkDigit: 'The check digit does not match. Look for a typo in the number.',
        dispatchDate: {
          invalid: 'Enter a full date.',
          future: 'This date is in the future.',
          tooEarly: 'This date is more than a year before the invoice.',
        },
        certIdShape: 'Use 4 to 64 characters (letters, digits, - _ . / :).',
      },
      fixToSend: 'Correct the proof fields above, or leave them empty, to send.',
      refused: 'The proof could not be accepted, so your answers were not sent. Check the fields above or clear them, then send again.',
    },
    terms: {
      version: (version: string) => `Version ${version}`,
      opensInNewTab: 'opens in a new tab',
      tickToSubmit: 'Tick the box above to send your answers.',
    },
    termsPage: {
      loading: 'Loading the terms…',
      fingerprint: 'SHA-256 fingerprint',
      print: 'Print or save',
      failed: {
        title: 'The terms could not be loaded',
        body: 'Check your internet connection and try again.',
      },
    },
  },
} satisfies Record<Lang, VerifyStrings>
