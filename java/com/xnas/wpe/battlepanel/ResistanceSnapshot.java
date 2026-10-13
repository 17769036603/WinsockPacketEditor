package com.xnas.wpe.battlepanel;

import java.math.BigDecimal;
import java.util.Collections;
import java.util.HashMap;
import java.util.Map;

final class ResistanceSnapshot {
    static final int FIRST_CODE = 200;
    static final int LAST_CODE = 212;
    private final long pid;
    private final long startTicks;
    private final long battleId;
    final long sampleRound;
    private final Map<Long, Map<Integer, Double>> values;

    ResistanceSnapshot(long pid, long startTicks, long battleId, long sampleRound,
                       Map<Long, Map<Integer, Double>> values) {
        this.pid = pid;
        this.startTicks = startTicks;
        this.battleId = battleId;
        this.sampleRound = sampleRound;
        Map<Long, Map<Integer, Double>> copy = new HashMap<>();
        for (Map.Entry<Long, Map<Integer, Double>> entry : values.entrySet()) {
            copy.put(entry.getKey(), Collections.unmodifiableMap(new HashMap<>(entry.getValue())));
        }
        this.values = Collections.unmodifiableMap(copy);
    }

    boolean matches(BattleFrameProtocol.Frame frame) {
        return frame != null && frame.status == BattleFrameProtocol.STATUS_LIVE
                && pid == frame.pid && startTicks == frame.startTicks && battleId == frame.battleId;
    }

    String value(long position, int code) {
        if (code < FIRST_CODE || code > LAST_CODE) return "未提供";
        Double raw = values.getOrDefault(position, Collections.emptyMap()).get(code);
        if (raw == null || !Double.isFinite(raw)) return "未提供";
        return BigDecimal.valueOf(raw).movePointLeft(1).stripTrailingZeros().toPlainString() + "%";
    }

    public static void main(String[] args) {
        long now = System.currentTimeMillis();
        BattleFrameProtocol.Frame frame = frame(now);
        Map<Integer, Double> player = new HashMap<>();
        player.put(200, 0.0);
        player.put(201, -12.5);
        player.put(202, 277.0);
        player.put(203, 24.0);
        player.put(204, 20.0);
        player.put(205, 1040.0);
        Map<Long, Map<Integer, Double>> values = new HashMap<>();
        values.put(1L, player);
        ResistanceSnapshot snapshot = new ResistanceSnapshot(123L, 456L, 789L, 3L, values);
        assert snapshot.matches(frame);
        assert "0%".equals(snapshot.value(1L, 200));
        assert "-1.25%".equals(snapshot.value(1L, 201));
        assert "27.7%".equals(snapshot.value(1L, 202));
        assert "2.4%".equals(snapshot.value(1L, 203));
        assert "2%".equals(snapshot.value(1L, 204));
        assert "104%".equals(snapshot.value(1L, 205));
        assert "未提供".equals(snapshot.value(1L, 206));
        assert "未提供".equals(snapshot.value(1L, 214));
        assert !snapshot.matches(frame(123L, 124L, 789L, now));
        assert !snapshot.matches(frame(123L, 456L, 790L, now));
        System.out.println("ResistanceSnapshot assertions passed");
    }

    private static BattleFrameProtocol.Frame frame(long timestamp) {
        return frame(123L, 456L, 789L, timestamp);
    }

    private static BattleFrameProtocol.Frame frame(long pid, long ticks, long battle, long timestamp) {
        BattleFrameProtocol.Slot[] slots = new BattleFrameProtocol.Slot[BattleFrameProtocol.SLOT_COUNT];
        for (int i = 0; i < slots.length; i++) {
            long position = i < 5 ? i + 1 : i < 10 ? i + 96 : i < 15 ? i + 9991 : i + 10086;
            slots[i] = new BattleFrameProtocol.Slot(position, BattleFrameProtocol.SLOT_UNKNOWN, 0, 0, 0);
        }
        return new BattleFrameProtocol.Frame(BattleFrameProtocol.STATUS_LIVE, pid, ticks, battle,
                3L, timestamp, slots);
    }
}
