package com.xnas.wpe.suite;

import android.content.Context;
import android.content.Intent;

/**
 * 把目标游戏切回前台。
 *
 * <p>各模块停止时都要在游戏的 GL 线程上执行清理脚本（恢复钩子、写绑定、确认恢复）。
 * 助手在前台时游戏处于暂停状态，GL 回调不执行，清理就会一直停在"清理中"。
 * 停止前先把游戏切回前台，清理通常能在 1-2 秒内完成。</p>
 */
public final class GameForeground {

    private GameForeground() {
    }

    public static void bringToFront(Context context) {
        if (context == null) return;
        try {
            String target = TargetGame.getSelectedPackage(context);
            Intent launch = context.getPackageManager().getLaunchIntentForPackage(target);
            if (launch == null) return;
            launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
            context.startActivity(launch);
        } catch (RuntimeException ignored) {
            // 目标未安装或系统拒绝启动时保持原行为，不影响停止流程。
        }
    }
}
