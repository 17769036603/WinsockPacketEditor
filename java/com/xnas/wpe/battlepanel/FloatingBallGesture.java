package com.xnas.wpe.battlepanel;

/** Pure Java state for dragging the battle panel's floating ball. */
final class FloatingBallGesture {
    private boolean active;
    private boolean dragging;
    private int pointerId = -1;
    private int startX;
    private int startY;
    private int touchSlop;
    private int lastX;
    private int lastY;
    private float downRawX;
    private float downRawY;

    void begin(int pointerId, float rawX, float rawY, int originX, int originY,
               int touchSlop) {
        this.pointerId = pointerId;
        this.downRawX = rawX;
        this.downRawY = rawY;
        this.startX = originX;
        this.startY = originY;
        this.lastX = originX;
        this.lastY = originY;
        this.touchSlop = Math.max(0, touchSlop);
        this.dragging = false;
        this.active = true;
    }

    Result move(int pointerId, float rawX, float rawY,
                int minX, int maxX, int minY, int maxY) {
        return update(pointerId, rawX, rawY, minX, maxX, minY, maxY, false);
    }

    Result release(int pointerId, float rawX, float rawY,
                   int minX, int maxX, int minY, int maxY) {
        if (!matches(pointerId)) return null;
        update(pointerId, rawX, rawY, minX, maxX, minY, maxY, true);
        boolean shouldClick = !dragging;
        active = false;
        return new Result(lastX, lastY, dragging, shouldClick, false);
    }

    Result cancel() {
        active = false;
        pointerId = -1;
        return new Result(lastX, lastY, dragging, false, false);
    }

    boolean isActive() {
        return active;
    }

    private Result update(int pointerId, float rawX, float rawY,
                          int minX, int maxX, int minY, int maxY, boolean release) {
        if (!matches(pointerId)) return null;
        double dx = (double) rawX - downRawX;
        double dy = (double) rawY - downRawY;
        if (!dragging && Math.hypot(dx, dy) > touchSlop) {
            dragging = true;
        }
        if (dragging) {
            lastX = clamp((long) startX + Math.round(dx), minX, maxX);
            lastY = clamp((long) startY + Math.round(dy), minY, maxY);
        } else {
            lastX = startX;
            lastY = startY;
        }
        return new Result(lastX, lastY, dragging, release && !dragging, active);
    }

    private boolean matches(int candidatePointerId) {
        return active && pointerId == candidatePointerId;
    }

    private static int clamp(long value, int minimum, int maximum) {
        if (maximum < minimum) maximum = minimum;
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return (int) value;
    }

    static final class Result {
        final int x;
        final int y;
        final boolean dragging;
        final boolean shouldClick;
        final boolean active;

        Result(int x, int y, boolean dragging, boolean shouldClick, boolean active) {
            this.x = x;
            this.y = y;
            this.dragging = dragging;
            this.shouldClick = shouldClick;
            this.active = active;
        }
    }
}
