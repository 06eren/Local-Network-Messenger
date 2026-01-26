#!/usr/bin/env python3
import json
import os
import sys

SUSPICIOUS_EXT = {
    ".exe",
    ".bat",
    ".cmd",
    ".ps1",
    ".vbs",
    ".js",
    ".dll",
    ".scr",
    ".msi",
}

try:
    sys.stdin.reconfigure(encoding="utf-8", errors="replace")
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def classify(file_name: str, size_bytes: int) -> tuple[str, str]:
    if not file_name:
        return "unknown", "Dosya adi alinmadi."
    ext = os.path.splitext(file_name)[1].lower()
    if ext in SUSPICIOUS_EXT:
        return "needs-review", f"Uzanti {ext} ek inceleme gerektiriyor."
    if size_bytes > 50 * 1024 * 1024:
        return "needs-review", "Buyuk dosya, ekstra kontrol gerekli."
    return "clean", "Temiz"


def emit_response(msg_id: str, status: str, details: str, error) -> None:
    payload = {
        "status": status,
        "details": details,
        "error": error,
    }
    envelope = {
        "id": msg_id,
        "type": "scan.response",
        "payload": payload,
    }
    sys.stdout.write(json.dumps(envelope, ensure_ascii=True) + "\n")
    sys.stdout.flush()


def main() -> None:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            envelope = json.loads(line)
            msg_id = envelope.get("id", "")
            payload = envelope.get("payload") or {}
            if not isinstance(payload, dict):
                raise ValueError("Payload beklenenden farkli.")
            file_name = payload.get("fileName") or ""
            size_bytes_raw = payload.get("sizeBytes", 0)
            try:
                size_bytes = int(size_bytes_raw)
            except Exception:
                size_bytes = 0
            status, details = classify(file_name, size_bytes)
            emit_response(msg_id, status, details, None)
        except Exception as exc:  # noqa: BLE001
            msg_id = ""
            try:
                msg_id = json.loads(line).get("id", "")
            except Exception:
                pass
            emit_response(msg_id, "error", "Tarama basarisiz.", {"code": "SCAN_ERROR", "message": str(exc)})


if __name__ == "__main__":
    main()
