#!/usr/bin/env python3
"""WPE vision worker.

The process is intentionally line-oriented: one UTF-8 JSON object in, one
UTF-8 JSON object out.  Optional computer-vision imports are lazy so that the
protocol can still answer ``ping`` when the Python environment is incomplete.
"""

from __future__ import annotations

import argparse
import base64
import contextlib
import hashlib
import importlib.util
import io
import json
import logging
import logging.handlers
import os
import secrets
import time
import sys
import traceback
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple


LOGGER = logging.getLogger("wpe-vision-worker")
MAX_JSONL_REQUEST_BYTES = 32 * 1024 * 1024
WORKER_VERSION = "2026.8.7.2"


def module_available(name: str) -> bool:
    try:
        return importlib.util.find_spec(name) is not None
    except (ImportError, ModuleNotFoundError, ValueError):
        return False


def capabilities() -> Dict[str, Any]:
    return {
        "worker": True,
        "version": WORKER_VERSION,
        "protocol": "jsonl-v1",
        "rapidocr": module_available("rapidocr"),
        "onnxruntime": module_available("onnxruntime"),
        "airtest": module_available("airtest"),
        "operations": [
            "ping",
            "warmup",
            "ocr",
            "capture",
            "begin_input_session",
            "airtest_action",
        ],
    }


def response(request_id: Any, **values: Any) -> Dict[str, Any]:
    result = {"id": request_id, "ok": True}
    result.update(values)
    return result


def error_response(request_id: Any, message: str, available: bool = True) -> Dict[str, Any]:
    return response(
        request_id,
        ok=False,
        available=available,
        success=False,
        error=message,
    )


def _number(value: Any, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _box_from_points(points: Any) -> Tuple[int, int, int, int]:
    try:
        flat = [(int(round(float(p[0]))), int(round(float(p[1])))) for p in points]
    except (TypeError, ValueError, IndexError):
        return 0, 0, 0, 0
    if not flat:
        return 0, 0, 0, 0
    left = min(p[0] for p in flat)
    top = min(p[1] for p in flat)
    right = max(p[0] for p in flat)
    bottom = max(p[1] for p in flat)
    return left, top, max(0, right - left), max(0, bottom - top)


def _rapidocr_output(result: Any) -> Tuple[Iterable[Any], Iterable[Any], Iterable[Any]]:
    """Read both current RapidOCROutput and the older mapping-shaped result."""
    if hasattr(result, "boxes"):
        boxes = getattr(result, "boxes", None)
        texts = getattr(result, "txts", None)
        scores = getattr(result, "scores", None)
        return (
            [] if boxes is None else boxes,
            [] if texts is None else texts,
            [] if scores is None else scores,
        )
    if isinstance(result, dict):
        boxes: List[Any] = []
        texts: List[Any] = []
        scores: List[Any] = []
        for item in result.values():
            if not isinstance(item, dict):
                continue
            boxes.append(item.get("dt_boxes", []))
            texts.append(item.get("rec_txt", ""))
            scores.append(item.get("score", 0.0))
        return boxes, texts, scores
    return [], [], []


class VisionWorker:
    def __init__(self) -> None:
        self._ocr_by_key: Dict[Tuple[str, str], Any] = {}
        self._model_signatures: Dict[str, str] = {}
        self._airtest_devices: Dict[str, Any] = {}
        self._input_sessions: Dict[str, Tuple[str, float]] = {}
        self._last_input_at: Dict[str, float] = {}

    def handle(self, request: Dict[str, Any]) -> Dict[str, Any]:
        request_id = request.get("id")
        method = str(request.get("method", "")).strip().lower()
        if method == "ping":
            return response(request_id, available=True, capabilities=capabilities())
        if method == "warmup":
            return self.warmup(request_id, request)
        if method == "ocr":
            return self.ocr(request_id, request)
        if method == "capture":
            return self.capture(request_id, request)
        if method == "begin_input_session":
            return self.begin_input_session(request_id, request)
        if method == "airtest_action":
            return self.airtest_action(request_id, request)
        return error_response(request_id, "Unknown worker method: " + method)

    @staticmethod
    def _resolve_model_directory(model_directory: str) -> Path:
        configured = str(model_directory or "").strip()
        path = Path(configured or "models/ocr").expanduser()
        if not path.is_absolute():
            path = Path(__file__).resolve().parent / path
        return path.resolve()

    @staticmethod
    def _validate_model_manifest(path: Path) -> Tuple[bool, str]:
        manifest_path = path / "models.manifest.json"
        if not manifest_path.is_file():
            return True, "OCR models found; manifest not present (file presence checked only)."
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            entries = manifest.get("files")
            if not isinstance(entries, list) or not entries:
                return False, "OCR model manifest is empty."
            root = path.resolve()
            for entry in entries:
                if not isinstance(entry, dict):
                    return False, "OCR model manifest contains an invalid entry."
                relative = str(entry.get("path", "")).strip()
                expected_hash = str(entry.get("sha256", "")).strip().lower()
                expected_size = int(entry.get("size", -1))
                candidate = (path / relative).resolve()
                if root not in candidate.parents or not candidate.is_file():
                    return False, "OCR model manifest references a missing file: {}".format(relative)
                if candidate.stat().st_size != expected_size:
                    return False, "OCR model size check failed: {}".format(relative)
                digest = hashlib.sha256(candidate.read_bytes()).hexdigest().lower()
                if digest != expected_hash:
                    return False, "OCR model integrity check failed: {}".format(relative)
            return True, "OCR models found and SHA-256 manifest verified."
        except (OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
            return False, "OCR model manifest could not be verified: {}".format(exc)

    @staticmethod
    def _validate_model_directory(model_directory: str) -> Path:
        path = VisionWorker._resolve_model_directory(model_directory)
        if not path.is_dir():
            raise RuntimeError(
                "RapidOCR model directory does not exist: {}".format(path)
            )
        model_files = sorted(path.glob("*.onnx"))
        if len(model_files) < 3:
            raise RuntimeError(
                "RapidOCR model directory is incomplete; expected bundled det/cls/rec ONNX files: {}".format(
                    path
                )
            )
        manifest_ok, manifest_message = VisionWorker._validate_model_manifest(path)
        if not manifest_ok:
            raise RuntimeError(manifest_message)
        return path

    @staticmethod
    def _normalize_language(language: str) -> str:
        value = str(language or "").strip().lower()
        if value in {"en", "eng", "english"} or (
            "en" in value and not any(item in value for item in ("chi", "zh", "ch"))
        ):
            return "en"
        return "ch"

    def _get_ocr(self, model_directory: str, language: str) -> Any:
        if not module_available("rapidocr"):
            raise RuntimeError("RapidOCR is not installed in the Python environment.")
        if not module_available("onnxruntime"):
            raise RuntimeError("ONNX Runtime is not installed in the Python environment.")
        from rapidocr import RapidOCR

        model_path = self._resolve_model_directory(model_directory)
        language_key = self._normalize_language(language)
        cache_key = (str(model_path).lower(), language_key)
        signature = self._model_signature(model_path)
        if cache_key in self._ocr_by_key and self._model_signatures.get(str(model_path)) == signature:
            return self._ocr_by_key[cache_key]
        model_path = self._validate_model_directory(model_directory)
        params: Dict[str, Any] = {}
        params["Global.model_root_dir"] = str(model_path)
        params["Rec.lang_type"] = language_key
        # RapidOCR has changed optional constructor parameters between minor
        # releases.  The default engine remains the stable compatibility path.
        try:
            with contextlib.redirect_stdout(sys.stderr):
                engine = RapidOCR(params=params)
        except (TypeError, KeyError):
            with contextlib.redirect_stdout(sys.stderr):
                engine = RapidOCR(params={"Global.model_root_dir": str(model_path)})
        self._ocr_by_key[cache_key] = engine
        self._model_signatures[str(model_path)] = self._model_signature(model_path)
        return engine

    @staticmethod
    def _model_signature(path: Path) -> str:
        if not path.is_dir():
            return str(path)
        try:
            entries = []
            for candidate in sorted(path.glob("*.onnx")):
                stat = candidate.stat()
                entries.append("{}:{}:{}".format(candidate.name, stat.st_size, stat.st_mtime_ns))
            manifest = path / "models.manifest.json"
            if manifest.is_file():
                stat = manifest.stat()
                entries.append("{}:{}:{}".format(manifest.name, stat.st_size, stat.st_mtime_ns))
            return "|".join(entries)
        except OSError:
            return str(path) + ":unreadable"

    def warmup(self, request_id: Any, request: Dict[str, Any]) -> Dict[str, Any]:
        started = time.perf_counter()
        if not module_available("rapidocr") or not module_available("onnxruntime"):
            return error_response(
                request_id,
                "RapidOCR and ONNX Runtime are required for Python OCR.",
                available=False,
            )
        try:
            model_path = self._validate_model_directory(
                str(request.get("model_directory", "")).strip()
            )
            self._get_ocr(
                str(model_path),
                str(request.get("language", "")),
            )
            elapsed = int(round((time.perf_counter() - started) * 1000.0))
            _, manifest_message = self._validate_model_manifest(model_path)
            diagnostics = {
                "elapsed_ms": elapsed,
                "model_directory": str(model_path),
                "cache_hit": True,
                "operation": "warmup",
            }
            LOGGER.info("warmup id=%s elapsed_ms=%s model=%s", request_id, elapsed, model_path)
            return response(
                request_id,
                available=True,
                success=True,
                warmup_ms=elapsed,
                model_manifest=manifest_message,
                diagnostics=diagnostics,
            )
        except Exception as exc:
            LOGGER.exception("Worker warmup failed")
            return error_response(request_id, "Python Worker warmup failed: " + str(exc))

    @staticmethod
    def _adaptive_threshold(array: Any, window_size: int, offset: int) -> Any:
        import numpy as np

        height, width = array.shape[:2]
        window = max(3, min(51, int(window_size or 15)))
        if window % 2 == 0:
            window += 1
        half = window // 2
        integral = np.pad(array.astype(np.int64), ((1, 0), (1, 0)), mode="constant")
        integral = integral.cumsum(axis=0).cumsum(axis=1)
        y0 = np.maximum(0, np.arange(height) - half)
        y1 = np.minimum(height - 1, np.arange(height) + half) + 1
        x0 = np.maximum(0, np.arange(width) - half)
        x1 = np.minimum(width - 1, np.arange(width) + half) + 1
        sums = (
            integral[y1[:, None], x1[None, :]]
            - integral[y0[:, None], x1[None, :]]
            - integral[y1[:, None], x0[None, :]]
            + integral[y0[:, None], x0[None, :]]
        )
        counts = (y1 - y0)[:, None] * (x1 - x0)[None, :]
        threshold = sums / counts - int(offset or 0)
        return np.where(array >= threshold, 255, 0).astype(np.uint8)

    @staticmethod
    def _preprocess_image(image: Any, request: Dict[str, Any]) -> Tuple[Any, int, int]:
        from PIL import ImageEnhance, ImageFilter, ImageOps
        import numpy as np

        original_width, original_height = image.size
        scale_factor = max(1, min(4, int(request.get("scale_factor", 2))))
        grayscale = bool(
            request.get("convert_to_grayscale", True)
            or request.get("use_binary_threshold", False)
            or request.get("use_adaptive_threshold", False)
            or request.get("invert", False)
            or request.get("use_denoise", False)
            or request.get("use_sharpen", False)
        )
        contrast = max(0.1, min(5.0, _number(request.get("contrast"), 1.0)))
        if grayscale:
            image = ImageOps.grayscale(image)
        if contrast != 1.0:
            image = ImageEnhance.Contrast(image).enhance(contrast)
        if bool(request.get("invert", False)):
            image = ImageOps.invert(image)
        if bool(request.get("use_denoise", False)):
            image = image.filter(ImageFilter.MedianFilter(size=3))
        if bool(request.get("use_sharpen", False)):
            image = image.filter(ImageFilter.SHARPEN)
        if bool(request.get("use_adaptive_threshold", False)) or bool(
            request.get("use_binary_threshold", False)
        ):
            array = np.asarray(image.convert("L"), dtype=np.uint8)
            if bool(request.get("use_adaptive_threshold", False)):
                array = VisionWorker._adaptive_threshold(
                    array,
                    int(request.get("adaptive_threshold_window_size", 15)),
                    int(request.get("adaptive_threshold_offset", 8)),
                )
            else:
                threshold = max(0, min(255, int(request.get("binary_threshold", 160))))
                array = np.where(array >= threshold, 255, 0).astype(np.uint8)
            image = Image.fromarray(array, mode="L")
        if scale_factor != 1:
            image = image.resize(
                (original_width * scale_factor, original_height * scale_factor),
                resample=0,
            )
        return image, original_width, original_height

    def ocr(self, request_id: Any, request: Dict[str, Any]) -> Dict[str, Any]:
        started = time.perf_counter()
        image_data = request.get("image_base64")
        if not isinstance(image_data, str) or not image_data:
            return error_response(request_id, "OCR requires image_base64.")
        if not module_available("rapidocr") or not module_available("onnxruntime"):
            return error_response(
                request_id,
                "RapidOCR and ONNX Runtime are required for Python OCR.",
                available=False,
            )

        try:
            from PIL import Image
            import numpy as np

            raw = base64.b64decode(image_data, validate=True)
            image = Image.open(io.BytesIO(raw)).convert("RGB")
            image, original_width, original_height = self._preprocess_image(image, request)
            image = image.convert("RGB")
            max_side = max(128, min(4096, int(request.get("max_image_side", 960))))
            if max(image.size) > max_side:
                ratio = max_side / float(max(image.size))
                image = image.resize(
                    (max(1, int(image.width * ratio)), max(1, int(image.height * ratio)))
                )
            # RapidOCR follows the usual OpenCV BGR array convention.
            array = np.asarray(image)[:, :, ::-1].copy()
            model_directory = str(request.get("model_directory", "")).strip()
            resolved_model_directory = self._resolve_model_directory(model_directory)
            cache_key = (
                str(resolved_model_directory).lower(),
                self._normalize_language(str(request.get("language", ""))),
            )
            cache_hit = (
                cache_key in self._ocr_by_key
                and self._model_signatures.get(str(resolved_model_directory))
                == self._model_signature(resolved_model_directory)
            )
            engine = self._get_ocr(
                model_directory,
                str(request.get("language", "")),
            )
            recognition_threshold = max(
                0.0, min(1.0, _number(request.get("recognition_threshold"), 0.5))
            )
            detection_threshold = max(
                0.0, min(1.0, _number(request.get("detection_threshold"), 0.3))
            )
            with contextlib.redirect_stdout(sys.stderr):
                try:
                    result = engine(
                        array,
                        text_score=recognition_threshold,
                        box_thresh=detection_threshold,
                    )
                except TypeError:
                    result = engine(array)
            boxes, texts, scores = _rapidocr_output(result)
            x_scale = original_width / float(max(1, image.width))
            y_scale = original_height / float(max(1, image.height))
            whitelist = str(request.get("character_whitelist", "") or "")
            blacklist = set(str(request.get("character_blacklist", "") or ""))
            output_boxes: List[Dict[str, Any]] = []
            output_texts: List[str] = []
            output_scores: List[float] = []
            for index, text in enumerate(texts):
                value = str(text or "").strip()
                if whitelist:
                    value = "".join(character for character in value if character in whitelist)
                if blacklist:
                    value = "".join(character for character in value if character not in blacklist)
                score = _number(scores[index] if index < len(scores) else 0.0)
                if not value or score < recognition_threshold:
                    continue
                points = boxes[index] if index < len(boxes) else []
                x, y, width, height = _box_from_points(points)
                x = int(round(x * x_scale))
                y = int(round(y * y_scale))
                width = int(round(width * x_scale))
                height = int(round(height * y_scale))
                output_boxes.append(
                    {
                        "text": value,
                        "confidence": score,
                        "x": x,
                        "y": y,
                        "width": width,
                        "height": height,
                    }
                )
                output_texts.append(value)
                output_scores.append(score)
            confidence = sum(output_scores) / len(output_scores) if output_scores else 0.0
            elapsed = int(round((time.perf_counter() - started) * 1000.0))
            diagnostics = {
                "elapsed_ms": elapsed,
                "model_cache_hit": cache_hit,
                "model_directory": str(resolved_model_directory),
                "source_size": [original_width, original_height],
                "result_count": len(output_boxes),
                "operation": "ocr",
            }
            LOGGER.info(
                "ocr id=%s elapsed_ms=%s cache_hit=%s result_count=%s",
                request_id,
                elapsed,
                cache_hit,
                len(output_boxes),
            )
            return response(
                request_id,
                available=True,
                success=bool(output_texts),
                text="\n".join(output_texts),
                confidence=confidence,
                boxes=output_boxes,
                engine="RapidOCR/ONNX Runtime",
                error="" if output_texts else "RapidOCR returned no text.",
                diagnostics=diagnostics,
            )
        except Exception as exc:  # the protocol must survive one bad frame
            LOGGER.exception("OCR request failed")
            return error_response(request_id, "Python OCR failed: " + str(exc))

    def _airtest_device(self, handle: Any) -> Any:
        if not module_available("airtest"):
            raise RuntimeError("Airtest is not installed in the Python environment.")
        key = str(int(handle))
        if key not in self._airtest_devices:
            from airtest.core.api import connect_device

            self._airtest_devices[key] = connect_device(
                "Windows:///{}?foreground=False".format(key)
            )
        return self._airtest_devices[key]

    def capture(self, request_id: Any, request: Dict[str, Any]) -> Dict[str, Any]:
        if not module_available("airtest"):
            return error_response(request_id, "Airtest is not installed.", available=False)
        try:
            import numpy as np
            from PIL import Image

            device = self._airtest_device(request.get("window_handle"))
            with contextlib.redirect_stdout(sys.stderr):
                frame = device.snapshot()
            if frame is None:
                raise RuntimeError("Airtest returned an empty screenshot.")
            array = np.asarray(frame)
            if array.ndim < 3 or array.shape[2] < 3:
                raise RuntimeError("Airtest returned a frame with an unsupported shape.")
            region = request.get("region")
            if isinstance(region, dict):
                x = max(0, int(region.get("x", 0)))
                y = max(0, int(region.get("y", 0)))
                width = max(1, int(region.get("width", array.shape[1] - x)))
                height = max(1, int(region.get("height", array.shape[0] - y)))
                if x + width > array.shape[1] or y + height > array.shape[0]:
                    raise RuntimeError(
                        "Airtest frame is smaller than the requested client region; check DPI or window scaling."
                    )
                array = array[y : y + height, x : x + width]
            image = Image.fromarray(array[:, :, :3])
            output = io.BytesIO()
            image.save(output, format="PNG")
            return response(
                request_id,
                available=True,
                success=True,
                image_base64=base64.b64encode(output.getvalue()).decode("ascii"),
                engine="Airtest/Windows",
                diagnostics={"operation": "capture", "source_size": list(np.asarray(frame).shape[:2])},
            )
        except Exception as exc:
            LOGGER.exception("Airtest capture failed")
            return error_response(request_id, "Airtest capture failed: " + str(exc))

    def begin_input_session(self, request_id: Any, request: Dict[str, Any]) -> Dict[str, Any]:
        if not bool(request.get("allow_system_input", False)):
            return error_response(
                request_id,
                "Airtest input is disabled until the run is explicitly confirmed.",
            )
        try:
            handle = int(request["window_handle"])
            if handle <= 0:
                raise ValueError("A valid target window handle is required.")
            self._airtest_device(handle)
            token = secrets.token_urlsafe(32)
            self._input_sessions[str(handle)] = (token, time.monotonic() + 15.0)
            return response(
                request_id,
                available=True,
                success=True,
                authorization_token=token,
                expires_in_seconds=15,
                engine="Airtest/Windows",
            )
        except Exception as exc:
            LOGGER.exception("Airtest input session failed")
            return error_response(request_id, "Airtest input session failed: " + str(exc))

    def airtest_action(self, request_id: Any, request: Dict[str, Any]) -> Dict[str, Any]:
        try:
            handle = int(request["window_handle"])
            session = self._input_sessions.get(str(handle))
            if session is None or session[1] < time.monotonic():
                self._input_sessions.pop(str(handle), None)
                return error_response(request_id, "Airtest input authorization is missing or expired.")
            if not secrets.compare_digest(str(request.get("authorization_token", "")), session[0]):
                return error_response(request_id, "Airtest input authorization is invalid.")
            now = time.monotonic()
            if now - self._last_input_at.get(str(handle), 0.0) < 0.08:
                return error_response(request_id, "Airtest input rate limit exceeded.")
            action = str(request.get("action", "")).strip().lower()
            if action not in {"touch", "swipe", "scroll", "keyevent"}:
                return error_response(request_id, "Unsupported Airtest action: " + action)
            self._input_sessions.pop(str(handle), None)
            self._last_input_at[str(handle)] = now
            device = self._airtest_device(request.get("window_handle"))
            if action == "touch":
                x = int(request["x"])
                y = int(request["y"])
                if x < 0 or y < 0 or x > 100000 or y > 100000:
                    return error_response(request_id, "Airtest touch coordinates are outside the safety limit.")
                touch_kwargs: Dict[str, Any] = {}
                button = str(request.get("button", "left")).lower()
                if button not in {"left", "right"}:
                    return error_response(request_id, "Unsupported Airtest mouse button: " + button)
                touch_kwargs["button"] = button
                if request.get("times"):
                    touch_kwargs["times"] = max(1, min(3, int(request.get("times"))))
                device.touch((x, y), **touch_kwargs)
            elif action == "swipe":
                points = [int(request["x1"]), int(request["y1"]), int(request["x2"]), int(request["y2"])]
                if any(point < 0 or point > 100000 for point in points):
                    return error_response(request_id, "Airtest swipe coordinates are outside the safety limit.")
                device.swipe(
                    (points[0], points[1]),
                    (points[2], points[3]),
                    duration=max(0.05, min(5.0, float(request.get("duration", 0.5)))),
                )
            elif action == "scroll":
                amount = max(-10000, min(10000, int(request.get("amount", 0))))
                device.scroll((0, amount))
            elif action == "keyevent":
                key = str(request.get("key", "")).strip()
                if not key or len(key) > 64:
                    return error_response(request_id, "Airtest keyevent is invalid.")
                device.keyevent(key)
            return response(
                request_id,
                available=True,
                success=True,
                engine="Airtest/Windows",
                diagnostics={"operation": "airtest_action", "action": action},
            )
        except Exception as exc:
            LOGGER.exception("Airtest action failed")
            return error_response(request_id, "Airtest action failed: " + str(exc))


def run_jsonl() -> int:
    worker = VisionWorker()
    for line in sys.stdin:
        if not line.strip():
            continue
        request_id: Any = None
        try:
            if len(line.encode("utf-8")) > MAX_JSONL_REQUEST_BYTES:
                raise ValueError("JSONL request exceeds the 32 MiB safety limit.")
            request = json.loads(line)
            if not isinstance(request, dict):
                raise ValueError("A JSONL request must be an object.")
            request_id = request.get("id")
            result = worker.handle(request)
        except Exception as exc:
            LOGGER.error("Protocol request failed: %s", exc)
            result = error_response(request_id, "Worker protocol error: " + str(exc))
        encoded_result = (
            json.dumps(result, ensure_ascii=False, separators=(",", ":")) + "\n"
        ).encode("utf-8")
        sys.stdout.buffer.write(encoded_result)
        sys.stdout.buffer.flush()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="WPE JSONL vision worker")
    parser.add_argument("--jsonl", action="store_true", help="serve JSONL on stdin/stdout")
    args = parser.parse_args()
    logging.basicConfig(stream=sys.stderr, level=logging.WARNING)
    try:
        log_root = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "XNAS" / "WPE" / "vision-worker" / "logs"
        log_root.mkdir(parents=True, exist_ok=True)
        file_handler = logging.handlers.RotatingFileHandler(
            log_root / "worker.log",
            maxBytes=512 * 1024,
            backupCount=2,
            encoding="utf-8",
        )
        file_handler.setLevel(logging.INFO)
        LOGGER.setLevel(logging.INFO)
        LOGGER.addHandler(file_handler)
    except (OSError, ValueError):
        LOGGER.warning("Unable to initialize the bounded worker log file.")
    return run_jsonl() if args.jsonl else run_jsonl()


if __name__ == "__main__":
    raise SystemExit(main())
