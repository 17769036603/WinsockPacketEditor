package com.xnas.wpe.battlepanel;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

/** Strict decoder for the local EBP2 worker protocol in mobile-battle-panel/PROTOCOL.md. */
final class BattleFrameProtocol {
    static final int MAX_PAYLOAD_BYTES = 1024;
    static final int V2_PAYLOAD_BYTES = 680;
    static final int SLOT_COUNT = 20;
    static final int SIDE_SLOT_COUNT = 10;
    static final long MAX_FRAME_AGE_MS = 5_000L;
    static final long MAX_CLOCK_SKEW_MS = 2_000L;

    static final int STATUS_NO_GAME = 0;
    static final int STATUS_LOCATING = 1;
    static final int STATUS_NOT_IN_BATTLE = 2;
    static final int STATUS_LIVE = 3;
    static final int STATUS_ROOT_DENIED = 4;
    static final int STATUS_READ_ERROR = 5;
    static final int STATUS_EXPIRED = 6;

    static final int SLOT_UNKNOWN = 0;
    static final int SLOT_EMPTY = 1;
    static final int SLOT_ALIVE = 2;
    static final int SLOT_DEAD = 3;
    static final int SLOT_ALIVE_SP_UNKNOWN = 4;

    private BattleFrameProtocol() {
    }

    static Frame parse(byte[] payload, long nowUnixMs) throws FrameException {
        if (payload == null || payload.length != V2_PAYLOAD_BYTES
                || payload.length > MAX_PAYLOAD_BYTES) {
            throw new FrameException("载荷长度不是版本 2 的 680 字节");
        }

        ByteBuffer input = ByteBuffer.wrap(payload).order(ByteOrder.BIG_ENDIAN);
        if (input.get() != 'E' || input.get() != 'B' || input.get() != 'P' || input.get() != '2') {
            throw new FrameException("帧标识不是 EBP2");
        }

        int status = input.get() & 0xff;
        int slotCount = input.get() & 0xff;
        if (status < STATUS_NO_GAME || status > STATUS_EXPIRED) {
            throw new FrameException("未知的总体状态码");
        }
        if (slotCount != SLOT_COUNT) {
            throw new FrameException("槽位数量不是 20");
        }
        if (input.getShort() != 0) {
            throw new FrameException("头部保留字段非零");
        }

        long pid = input.getInt() & 0xffff_ffffL;
        long startTicks = input.getLong();
        long battleId = input.getLong();
        long round = input.getInt() & 0xffff_ffffL;
        long timestampUnixMs = input.getLong();
        Slot[] slots = new Slot[SLOT_COUNT];
        for (int i = 0; i < slots.length; i++) {
            long position = input.getInt() & 0xffff_ffffL;
            int state = input.get() & 0xff;
            int reserved0 = input.get() & 0xff;
            int reserved1 = input.get() & 0xff;
            int reserved2 = input.get() & 0xff;
            long hp = input.getLong();
            long mp = input.getLong();
            long sp = input.getLong();

            if (state > SLOT_DEAD && state != SLOT_ALIVE_SP_UNKNOWN) {
                throw new FrameException("槽位状态码无效");
            }
            if (reserved0 != 0 || reserved1 != 0 || reserved2 != 0) {
                throw new FrameException("槽位保留字段非零");
            }
            if (state == SLOT_ALIVE_SP_UNKNOWN && sp != 0) {
                throw new FrameException("SP 未知槽位必须使用零哨兵");
            }
            slots[i] = new Slot(position, state, hp, mp, sp);
        }
        if (input.hasRemaining()) {
            throw new FrameException("版本 2 帧包含多余字段");
        }

        Frame frame = new Frame(status, pid, startTicks, battleId, round, timestampUnixMs, slots);
        if (status == STATUS_LIVE) {
            validateLiveFrame(frame, nowUnixMs);
        }
        return frame;
    }

    private static void validateLiveFrame(Frame frame, long nowUnixMs) throws FrameException {
        if (frame.pid == 0 || frame.startTicks == 0 || frame.battleId == 0 || frame.round == 0) {
            throw new FrameException("实时帧的进程或战斗身份无效");
        }
        if (frame.timestampUnixMs <= 0 || frame.timestampUnixMs > nowUnixMs + MAX_CLOCK_SKEW_MS) {
            throw new FrameException("实时帧时间戳无效");
        }
        if (!hasExpectedPositions(frame.slots)) {
            throw new FrameException("实时帧双方位置族不符合已验证阵型");
        }
        if (nowUnixMs - frame.timestampUnixMs > MAX_FRAME_AGE_MS) {
            throw new FrameException("实时帧已过期", true);
        }
    }

    private static boolean hasExpectedPositions(Slot[] slots) {
        if (slots.length != SLOT_COUNT) {
            return false;
        }
        final long ownMainBase;
        final long opposingMainBase;
        if (slots[0].position == 1L) {
            ownMainBase = 1L;
            opposingMainBase = 10_001L;
        } else if (slots[0].position == 10_001L) {
            ownMainBase = 10_001L;
            opposingMainBase = 1L;
        } else {
            return false;
        }
        return hasFormationPositions(slots, 0, ownMainBase)
                && hasFormationPositions(slots, SIDE_SLOT_COUNT, opposingMainBase);
    }

    private static boolean hasFormationPositions(Slot[] slots, int offset, long mainBase) {
        long petBase = mainBase + 100L;
        for (int i = 0; i < 5; i++) {
            if (slots[offset + i].position != mainBase + i
                    || slots[offset + 5 + i].position != petBase + i) {
                return false;
            }
        }
        return true;
    }

    static final class Frame {
        final int status;
        final long pid;
        final long startTicks;
        final long battleId;
        final long round;
        final long timestampUnixMs;
        /** Own side first, then opposing side; each side is main slots then pets. */
        final Slot[] slots;

        Frame(int status, long pid, long startTicks, long battleId, long round,
              long timestampUnixMs, Slot[] slots) {
            this.status = status;
            this.pid = pid;
            this.startTicks = startTicks;
            this.battleId = battleId;
            this.round = round;
            this.timestampUnixMs = timestampUnixMs;
            this.slots = slots;
        }

        boolean sameProcessAndBattle(Frame other) {
            return sameProcess(other) && battleId == other.battleId;
        }

        boolean sameProcess(Frame other) {
            return other != null && pid == other.pid && startTicks == other.startTicks;
        }
    }

    static boolean shouldRetainLastLiveFrame(Frame current, Frame incoming) {
        if (current == null || current.status != STATUS_LIVE || incoming == null
                || (incoming.status != STATUS_LOCATING
                && incoming.status != STATUS_READ_ERROR
                && incoming.status != STATUS_EXPIRED)) {
            return false;
        }
        return current.sameProcess(incoming);
    }

    static final class Slot {
        final long position;
        final int state;
        final long hp;
        final long mp;
        final long sp;

        Slot(long position, int state, long hp, long mp, long sp) {
            this.position = position;
            this.state = state;
            this.hp = hp;
            this.mp = mp;
            this.sp = sp;
        }
    }

    static final class FrameException extends Exception {
        final boolean expired;

        FrameException(String message) {
            this(message, false);
        }

        FrameException(String message, boolean expired) {
            super(message);
            this.expired = expired;
        }
    }

    public static void main(String[] args) throws Exception {
        boolean assertionsEnabled = false;
        assert assertionsEnabled = true;
        if (!assertionsEnabled) {
            throw new IllegalStateException("Use java -ea to run parser assertions");
        }

        long nowUnixMs = System.currentTimeMillis();
        byte[] payload = assertionFrame(nowUnixMs);
        setAssertionSlot(payload, SLOT_ALIVE_SP_UNKNOWN, 0L);
        Frame unknownSp = parse(payload, nowUnixMs);
        assert unknownSp.pid == 123L;
        assert unknownSp.startTicks == 456L;
        assert unknownSp.battleId == 789L;
        assert unknownSp.timestampUnixMs == nowUnixMs;
        assert unknownSp.slots[0].position == 1L;
        assert unknownSp.slots[0].state == SLOT_ALIVE_SP_UNKNOWN;
        assert unknownSp.slots[0].hp == 100L && unknownSp.slots[0].mp == 200L;
        assert unknownSp.slots[0].sp == 0L;

        byte[] nonzeroSentinel = assertionFrame(nowUnixMs);
        setAssertionSlot(nonzeroSentinel, SLOT_ALIVE_SP_UNKNOWN, 1L);
        expectAssertionRejection(nonzeroSentinel, nowUnixMs);

        byte[] unknownState = assertionFrame(nowUnixMs);
        setAssertionSlot(unknownState, 5, 0L);
        expectAssertionRejection(unknownState, nowUnixMs);

        byte[] alive = assertionFrame(nowUnixMs);
        setAssertionSlot(alive, SLOT_ALIVE, 12_345L);
        Frame verified = parse(alive, nowUnixMs);
        assert verified.slots[0].state == SLOT_ALIVE;
        assert verified.slots[0].sp == 12_345L;
        System.out.println("BattleFrameProtocol assertions passed");
    }

    private static byte[] assertionFrame(long timestampUnixMs) {
        ByteBuffer output = ByteBuffer.allocate(V2_PAYLOAD_BYTES).order(ByteOrder.BIG_ENDIAN);
        output.put(new byte[]{'E', 'B', 'P', '2'});
        output.put((byte) STATUS_LIVE);
        output.put((byte) SLOT_COUNT);
        output.putShort((short) 0);
        output.putInt(123);
        output.putLong(456L);
        output.putLong(789L);
        output.putInt(1);
        output.putLong(timestampUnixMs);
        for (int i = 0; i < SLOT_COUNT; i++) {
            output.putInt((int) assertionPosition(i));
            output.put((byte) SLOT_UNKNOWN);
            output.put(new byte[3]);
            output.putLong(100L);
            output.putLong(200L);
            output.putLong(300L);
        }
        return output.array();
    }

    private static long assertionPosition(int index) {
        if (index < 5) return index + 1L;
        if (index < 10) return 101L + index - 5;
        if (index < 15) return 10_001L + index - 10;
        return 10_101L + index - 15;
    }

    private static void setAssertionSlot(byte[] payload, int state, long sp) {
        ByteBuffer output = ByteBuffer.wrap(payload).order(ByteOrder.BIG_ENDIAN);
        output.put(44, (byte) state);
        output.putLong(64, sp);
    }

    private static void expectAssertionRejection(byte[] payload, long nowUnixMs)
            throws Exception {
        try {
            parse(payload, nowUnixMs);
        } catch (FrameException expected) {
            return;
        }
        throw new AssertionError("invalid slot was accepted");
    }
}
