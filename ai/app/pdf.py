"""The text layer of a PDF invoice (F9), read with pypdf. A PDF is hostile input: the decoded
file is capped, encrypted files are refused, at most MAX_PAGES pages are read, the text is
capped, and parsing runs under a deadline so a crafted file cannot hold the service. A scan has
no text layer and is refused; this service does no OCR."""

from __future__ import annotations

import base64
import binascii
import io
import logging

from pypdf import PdfReader
from pypdf.errors import PyPdfError

from app.structured import call_with_deadline

log = logging.getLogger(__name__)

MAX_PDF_BYTES = 3 * 1024 * 1024
MAX_PAGES = 10
MAX_TEXT_CHARS = 20_000
PARSE_DEADLINE_S = 5.0


class PdfRefused(Exception):
    """Why a PDF cannot be read: `code` is "unreadable", "no_text" or "too_large"."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code


def decode(pdf_base64: str) -> bytes:
    try:
        data = base64.b64decode(pdf_base64, validate=True)
    except (binascii.Error, ValueError) as error:
        raise PdfRefused("unreadable", "The file is not valid base64.") from error
    if len(data) > MAX_PDF_BYTES:
        raise PdfRefused("too_large", f"The PDF is over {MAX_PDF_BYTES // (1024 * 1024)} MB.")
    if not data.startswith(b"%PDF-"):
        raise PdfRefused("unreadable", "The file is not a PDF.")
    return data


def text_of(data: bytes) -> tuple[str, int]:
    """The text layer of the first pages, whitespace kept per line, capped, and how many pages
    were read."""

    def read() -> tuple[str, int]:
        reader = PdfReader(io.BytesIO(data), strict=False)
        if reader.is_encrypted:
            raise PdfRefused("unreadable", "The PDF is encrypted.")
        parts: list[str] = []
        size = 0
        for page in reader.pages[:MAX_PAGES]:
            text = page.extract_text() or ""
            parts.append(text)
            size += len(text)
            if size >= MAX_TEXT_CHARS:
                break
        return "\n".join(parts)[:MAX_TEXT_CHARS], len(parts)

    try:
        text, pages = call_with_deadline(read, PARSE_DEADLINE_S)
    except PdfRefused:
        raise
    except TimeoutError as error:
        raise PdfRefused("unreadable", "The PDF took too long to read.") from error
    except (PyPdfError, ValueError, KeyError, TypeError, AttributeError, IndexError) as error:
        log.warning("PDF not readable: %s", type(error).__name__)
        raise PdfRefused("unreadable", "The PDF could not be read.") from error
    if not text.strip():
        raise PdfRefused(
            "no_text", "The PDF has no text layer (a scan?); import the e-Factura XML instead."
        )
    return text, pages
