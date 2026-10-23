# WPE Python Vision Worker

The worker is a local JSONL process. It receives one JSON object per line on
stdin and writes one JSON object per line to stdout. Logs always go to stderr.

The C# front end sends captured PNG frames to `ocr`. The worker uses RapidOCR
with the CPU ONNX Runtime backend. The bundled `models/ocr` directory is
required for offline OCR; missing or incomplete model directories fail closed
instead of downloading models at runtime. OCR preprocessing, thresholds,
character filters, and source-coordinate box mapping are sent over JSONL.

`capture` is available through the C# Airtest capture source. Airtest input is
available only for an explicitly confirmed run: the front end requests a
short-lived, one-shot worker authorization token bound to the target window,
then sends one action with that token. A bare `airtest_action` request is
rejected; action rate, coordinates and duration are bounded.

Install into the per-user environment used by the desktop app:

```powershell
powershell.exe -ExecutionPolicy Bypass -File ..\tools\Install-VisionWorker.ps1
```

The protocol remains usable for `ping` even when the optional packages are not
installed; OCR/capture responses then report `available=false`. `warmup`
validates the model manifest and initializes RapidOCR without recognizing a
frame. The installer checks the pinned dependency set, all three bundled ONNX
files, their SHA-256 manifest, warmup, and a synthetic OCR smoke test.

Worker diagnostics are written to stderr and to a bounded rotating file under
`%LOCALAPPDATA%\XNAS\WPE\vision-worker\logs\worker.log`.
