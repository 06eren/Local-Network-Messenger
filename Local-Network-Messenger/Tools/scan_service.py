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


def emit_response(msg_id: str, resp_type: str, status: str, details: str, error) -> None:
    payload = {
        "status": status,
        "details": details,
        "error": error,
    }
    envelope = {
        "id": msg_id,
        "type": resp_type,
        "payload": payload,
    }
    sys.stdout.write(json.dumps(envelope, ensure_ascii=True) + "\n")
    sys.stdout.flush()


def analyze_network(peer_count: int, avg_ping_ms, loss_percent) -> tuple[str, str]:
    if peer_count <= 0:
        return "bekleniyor", "Agda aktif cihaz yok."

    ping = float(avg_ping_ms) if avg_ping_ms is not None else 0.0
    loss = float(loss_percent) if loss_percent is not None else 0.0
    score = 100.0
    if ping > 180:
        score -= 45
    elif ping > 90:
        score -= 25
    elif ping > 50:
        score -= 10

    if loss > 12:
        score -= 50
    elif loss > 5:
        score -= 25
    elif loss > 2:
        score -= 10

    if score >= 80:
        return "iyi", "Ag stabil."
    if score >= 55:
        return "orta", "Ag kullanilabilir."
    return "zayif", "Agda kayip/gecikme yuksek."


def main() -> None:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        msg_type = ""
        try:
            envelope = json.loads(line)
            msg_id = envelope.get("id", "")
            msg_type = envelope.get("type", "")
            payload = envelope.get("payload") or {}
            if not isinstance(payload, dict):
                raise ValueError("Payload beklenenden farkli.")
            if msg_type == "net.analysis.request":
                peer_count_raw = payload.get("peerCount", 0)
                try:
                    peer_count = int(peer_count_raw)
                except Exception:
                    peer_count = 0
                avg_ping = payload.get("averagePingMs")
                loss = payload.get("lossPercent")
                status, details = analyze_network(peer_count, avg_ping, loss)
                emit_response(msg_id, "net.analysis.response", status, details, None)
            else:
                file_name = payload.get("fileName") or ""
                size_bytes_raw = payload.get("sizeBytes", 0)
                try:
                    size_bytes = int(size_bytes_raw)
                except Exception:
                    size_bytes = 0
                status, details = classify(file_name, size_bytes)
                emit_response(msg_id, "scan.response", status, details, None)
        except Exception as exc:  # noqa: BLE001
            msg_id = ""
            try:
                msg_id = json.loads(line).get("id", "")
            except Exception:
                pass
            resp_type = "net.analysis.response" if msg_type == "net.analysis.request" else "scan.response"
            emit_response(msg_id, resp_type, "error", "Tarama basarisiz.", {"code": "SCAN_ERROR", "message": str(exc)})


if __name__ == "__main__":
    main()
