"""Standalone Tkinter enemy battle status panel."""
from __future__ import annotations

import argparse
import queue
import threading
import time
import tkinter as tk
from datetime import datetime
from tkinter import ttk

from battle_reader import ColdDiscoveryFailure, ReadFailure, sample

POLL_SECONDS = 1.5
COLD_DISCOVERY_RETRY_SECONDS = 10.0


def _retry_delay(error: Exception | None = None) -> float:
    return COLD_DISCOVERY_RETRY_SECONDS if isinstance(error, ColdDiscoveryFailure) else POLL_SECONDS
STALE_SECONDS = 5.0


def _slot_coordinates(position_ids: list[int]) -> list[tuple[float, float]]:
    """Arrange cards using the client's initRolePos coordinates (Y points up)."""
    layouts = {
        0: [(785, 95), (874, 140), (963, 185), (1052, 230), (1142, 275),
            (667, 162), (757, 207), (847, 252), (937, 297), (1027, 342)],
        10000: [(100, 285), (195, 335), (290, 385), (385, 435), (480, 485),
                (218, 218), (312, 268), (406, 318), (500, 368), (594, 418)],
    }
    for base, points in layouts.items():
        expected = [base + n for n in range(1, 6)] + [base + n for n in range(101, 106)]
        if position_ids == expected:
            min_x = min(x for x, _ in points)
            max_y = max(y for _, y in points)
            return [(20 + (x - min_x) * 2.1, 20 + (max_y - y) * 2.0) for x, y in points]
    raise ValueError("站位编码不在已确认布局内")


def _number_text(value: int | float) -> str:
    return str(int(value)) if isinstance(value, float) and value.is_integer() else str(value)


class Panel:
    def __init__(self, root: tk.Tk, serial: str, package: str) -> None:
        self.root, self.serial, self.package = root, serial, package
        self.events: queue.Queue = queue.Queue()
        self.stop_event = threading.Event()
        self.thread: threading.Thread | None = None
        self.run_id = 0
        self.latest: dict | None = None
        self.latest_at = 0.0
        root.title("战斗状态面板")
        root.geometry("1320x850")
        root.minsize(1180, 700)
        top = ttk.Frame(root, padding=10)
        top.pack(fill="x")
        self.start_button = ttk.Button(top, text="启动读取", command=self.start)
        self.start_button.pack(side="left")
        self.stop_button = ttk.Button(top, text="停止", command=self.stop, state="disabled")
        self.stop_button.pack(side="left", padx=(6, 12))
        self.status = tk.StringVar(value="已停止")
        ttk.Label(top, textvariable=self.status).pack(side="left")
        self.meta = tk.StringVar(value="BattleId: —    Round: —    更新时间: —")
        ttk.Label(root, textvariable=self.meta, padding=(10, 0, 10, 4)).pack(fill="x")
        self.note = tk.StringVar(value="双方竖向阵列 · 共二十个槽位同时显示 · HP 气血 / MP 法力 / SP 速度")
        ttk.Label(root, textvariable=self.note, padding=(10, 0, 10, 8)).pack(fill="x")
        viewport = ttk.Frame(root, padding=(10, 0, 10, 10))
        viewport.pack(fill="both", expand=True)
        viewport.columnconfigure(0, weight=1, uniform="sides")
        viewport.columnconfigure(1, weight=1, uniform="sides")
        viewport.rowconfigure(0, weight=1)
        self.side_widgets = {}
        self.side_position_orders = {"对方": [], "本方": []}
        self.side_mapping_sessions = {"对方": None, "本方": None}
        self.side_slots = {}
        self.side_panels = {}
        self.side_frames = {}
        for column, side in enumerate(("本方", "对方")):
            heading = "我方" if side == "本方" else "敌方"
            panel = ttk.LabelFrame(viewport, text=heading, padding=8)
            panel.grid(row=0, column=column, sticky="nsew", padx=(0, 8 if column == 0 else 0))
            panel.columnconfigure(0, weight=1, uniform=f"{side}-slots")
            panel.columnconfigure(1, weight=1, uniform=f"{side}-slots")
            for row in range(6):
                panel.rowconfigure(row, weight=1, uniform=f"{side}-rows")
            ttk.Label(panel, text="主单位", anchor="center").grid(row=0, column=0, sticky="ew", pady=(0, 4))
            ttk.Label(panel, text="宠物", anchor="center").grid(row=0, column=1, sticky="ew", pady=(0, 4))
            self.side_widgets[side] = panel
            self.side_panels[side] = panel
            self.side_slots[side] = []
            self.side_frames[side] = []
        # Compatibility aliases point to the opponent side's existing cards.
        self.slot_vars: list[tk.StringVar] = self.side_slots["对方"]
        self.slot_frames: list[ttk.LabelFrame] = self.side_frames["对方"]
        self.position_session: tuple | None = None
        self.position_mapping_session: tuple | None = None
        self.position_order: list[int] = []
        for side in ("本方", "对方"):
            panel = self.side_panels[side]
            for index in range(10):
                frame = ttk.LabelFrame(panel, text=f"槽位 {index + 1}", padding=5)
                frame.configure(width=260, height=96)
                frame.grid_propagate(False)
                value = tk.StringVar(value="等待数据")
                ttk.Label(frame, textvariable=value, justify="left", anchor="w", wraplength=240).pack(fill="both", expand=True)
                self.side_slots[side].append(value)
                self.side_frames[side].append(frame)
        self._place_slots([*range(10001, 10006), *range(10101, 10106)])
        self._place_side_slots("本方", [*range(1, 6), *range(101, 106)])
        root.after(150, self._drain)
        root.after(500, self._freshness)
        root.protocol("WM_DELETE_WINDOW", self.close)
        root.after(100, self.start)

    def _place_slots(self, position_ids: list[int]) -> None:
        self._place_side_slots("对方", position_ids)

    def _place_side_slots(self, side: str, position_ids: list[int]) -> None:
        points = _slot_coordinates(position_ids)
        frames = self.side_frames[side]
        for column, start in enumerate((0, 5)):
            indices = range(start, start + 5)
            # Turn each game's five-slot lane into a vertical column while
            # keeping the initRolePos order from top to bottom.
            ordered = sorted(indices, key=lambda index: points[index][0])
            for row, index in enumerate(ordered, start=1):
                group = "主位" if column == 0 else "宠物位"
                frames[index].configure(text=f"{group} {index % 5 + 1} · 位置 {position_ids[index]}")
                frames[index].grid(row=row, column=column, sticky="nsew", padx=4, pady=4)

    def start(self) -> None:
        if self.thread and self.thread.is_alive():
            return
        self.run_id += 1
        run_id = self.run_id
        stop_event = threading.Event()
        self.stop_event = stop_event
        self.start_button.configure(state="disabled")
        self.stop_button.configure(state="normal")
        self.status.set("正在发现")
        self.thread = threading.Thread(target=self._loop, args=(run_id, stop_event), daemon=True)
        self.thread.start()

    def stop(self) -> None:
        thread = self.thread
        if thread and thread.is_alive():
            self.stop_event.set()
            self.start_button.configure(state="disabled")
            self.stop_button.configure(state="disabled")
            self.status.set("正在停止")
            self._finish_stop_when_idle(self.run_id, thread)
            return
        self._mark_stopped()

    def _finish_stop_when_idle(self, run_id: int, thread: threading.Thread) -> None:
        if run_id != self.run_id:
            return
        if thread.is_alive():
            self.root.after(50, self._finish_stop_when_idle, run_id, thread)
            return
        self._mark_stopped()

    def _mark_stopped(self) -> None:
        self.latest = None
        self.latest_at = 0.0
        self.start_button.configure(state="normal")
        self.stop_button.configure(state="disabled")
        self.status.set("已停止")
        self.meta.set("BattleId: —    Round: —    更新时间: —")
        self.position_session = None
        self.position_mapping_session = None
        for side in ("本方", "对方"):
            self.side_position_orders[side].clear()
            self.side_mapping_sessions[side] = None
            for var in self.side_slots[side]:
                var.set("已停止，数据非实时")

    def _loop(self, run_id: int, stop_event: threading.Event) -> None:
        while not stop_event.is_set():
            try:
                result = sample(self.serial, self.package, stop_event=stop_event)
                delay = _retry_delay()
            except ColdDiscoveryFailure as exc:
                result = {"state": "failed", "message": str(exc) or "读取失败"}
                delay = _retry_delay(exc)
            except Exception as exc:  # UI reports failure and never preserves values as live.
                result = {"state": "failed", "message": str(exc) or "读取失败"}
                delay = _retry_delay(exc)
            if stop_event.is_set():
                break
            result["run_id"] = run_id
            self.events.put(result)
            stop_event.wait(delay)

    def _drain(self) -> None:
        try:
            while True:
                result = self.events.get_nowait()
                self._render(result)
        except queue.Empty:
            pass
        self.root.after(150, self._drain)

    def _render(self, result: dict) -> None:
        if result.get("run_id") != self.run_id:
            return
        if self.stop_event.is_set():
            return
        self.latest = result
        self.latest_at = time.monotonic()
        state = result.get("state", "failed")
        self.status.set({"discovering": "正在发现", "not_in_battle": "未进战", "ok": "实时", "failed": "读取失败", "dead": "已死亡"}.get(state, state))
        self.meta.set(f"BattleId: {result.get('battleId', '—')}    Round: {result.get('round', '—')}    更新时间: {datetime.now().strftime('%H:%M:%S')}")
        if state == "not_in_battle":
            self.position_session = None
            self.position_mapping_session = None
            for side in ("本方", "对方"):
                self.side_position_orders[side].clear()
                self.side_mapping_sessions[side] = None
                self._show_side_reason(side, result.get("message", "未进入战斗"))
            return
        if state != "ok":
            label = result.get("message") or ("未进入战斗" if state == "not_in_battle" else "等待唯一的角色位置映射")
            # No valid current frame is active. Retain the last confirmed
            # mapping until a later successful frame proves identity changed.
            self.position_session = None
            for side in ("本方", "对方"):
                self._show_side_reason(side, label)
            return
        identity = result.get("identity") or {}
        session = (identity.get("pid"), identity.get("startTicks"), identity.get("exe"), result.get("battleId"))
        if self.position_mapping_session != session:
            # Identity/BattleId changes are only accepted from a successful,
            # bound frame. Transient failures cannot reshuffle raw IDs.
            for side in ("本方", "对方"):
                self.side_position_orders[side] = []
                self.side_mapping_sessions[side] = session
            self.position_mapping_session = session
        self.position_session = session
        for side, ids_key, slots_key in (("对方", "positionIds", "slots"), ("本方", "ownPositionIds", "ownSlots")):
            raw_ids = result.get(ids_key, [])
            if (not isinstance(raw_ids, list) or len(raw_ids) != 10 or
                    any(not isinstance(value, int) or isinstance(value, bool) or value <= 0 for value in raw_ids)):
                self.status.set("读取失败")
                self.position_session = None
                for target in ("本方", "对方"):
                    self._show_side_reason(target, "position ID 列表无效")
                return
            try:
                self._place_side_slots(side, raw_ids)
            except ValueError as error:
                self.status.set("读取失败")
                self.position_session = None
                for target in ("本方", "对方"):
                    self._show_side_reason(target, str(error))
                return
            self.side_position_orders[side] = list(raw_ids)
            slots = result.get(slots_key, {})
            for index, var in enumerate(self.side_slots[side]):
                raw_id = raw_ids[index]
                role = slots.get(raw_id)
                if role is None:
                    var.set("空槽")
                elif not role.get("alive"):
                    var.set(f"单位 {role['roleId']}\n已死亡")
                else:
                    var.set(f"单位 {role['roleId']}\nHP {_number_text(role['hp'])}   MP {_number_text(role['mp'])}\nSP 速度 {_number_text(role['speed'])}")

    def _show_reason(self, reason: str) -> None:
        self._show_side_reason("对方", reason)

    def _show_side_reason(self, side: str, reason: str) -> None:
        for var in self.side_slots[side]:
            var.set(reason)

    def _freshness(self) -> None:
        if (self.thread and self.thread.is_alive() and not self.stop_event.is_set()
                and self.latest and self.latest.get("state") == "ok" and self.latest_at
                and time.monotonic() - self.latest_at > STALE_SECONDS):
            self.status.set("数据过期")
            for side in ("本方", "对方"):
                self._show_side_reason(side, "数据过期，等待新读取")
        self.root.after(500, self._freshness)

    def close(self) -> None:
        self.stop()
        self.root.destroy()


def main() -> None:
    parser = argparse.ArgumentParser(description="独立只读战斗状态面板")
    parser.add_argument("--serial", default="", help="ADB device serial; omitted requires exactly one online device")
    parser.add_argument("--package", default="com.gdoo.xxxy2026", help="Game package name")
    args = parser.parse_args()
    root = tk.Tk()
    Panel(root, args.serial, args.package)
    root.mainloop()


if __name__ == "__main__":
    main()
