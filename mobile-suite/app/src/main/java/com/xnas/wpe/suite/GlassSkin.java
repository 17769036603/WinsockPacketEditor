package com.xnas.wpe.suite;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.graphics.drawable.StateListDrawable;
import android.view.View;

/**
 * 统一的磨砂玻璃外观工具（UI_STYLE.md 第一种方案：克制的 BlurView 式磨砂玻璃）。
 *
 * <p>只生成背景 Drawable 与前景色，供现有控件复用；不改任何布局参数、尺寸、内边距、
 * 文本、控件树或事件监听。所有表面都沿用现有深色海军蓝底色与青绿色强调色：
 * 半透明蓝色调填充 + 发丝级描边 + 底部柔和阴影。
 *
 * <p>关于虚化：本应用的玻璃表面都绘制在不透明纯色底之上（页面根背景或侧栏底色），
 * 表面下方没有需要采样的应用自身内容，因此真正的背景模糊与这里的半透明填充在视觉上
 * 等价，而且 {@code View.setBackgroundBlurRadius} 只能虚化窗口之后的内容，无法虚化
 * 同级 View。按 UI_STYLE.md 允许的降级路径，这里使用磨砂色填充、细描边与柔和阴影；
 * 悬浮窗绘制在游戏画面之上，背景不受本应用控制，故维持原有更高不透明度的配色。
 */
public final class GlassSkin {
    /** 主要文字色，沿用现有取值。 */
    public static final int TEXT = Color.rgb(239, 246, 248);
    /** 次要文字色，沿用现有取值。 */
    public static final int TEXT_MUTED = Color.rgb(156, 179, 189);
    /** 禁用文字色：比次要文字更暗，保证禁用态可辨。 */
    public static final int TEXT_DISABLED = Color.rgb(120, 140, 152);
    /** 青绿色强调色，沿用现有取值。 */
    public static final int ACCENT = Color.rgb(74, 201, 176);

    // ===== 统一设计变量：所有页面共用，避免同类元素出现多种取值 =====
    /** 页面底色：所有 Activity 与系统栏统一使用，切页不再出现色差接缝。 */
    public static final int PAGE_BG = Color.rgb(12, 22, 31);
    /** 内容网格：左右 16dp、上下 12dp。 */
    public static final int PADDING_H_DP = 16;
    public static final int PADDING_V_DP = 12;
    /** 圆角三档：控件 8dp、容器 12dp、胶囊 20dp。 */
    public static final int RADIUS_CONTROL_DP = 8;
    public static final int RADIUS_CONTAINER_DP = 12;
    public static final int RADIUS_PILL_DP = 20;
    /** 按钮两档高度：主操作 48dp、紧凑 40dp。 */
    public static final int BUTTON_HEIGHT_DP = 48;
    public static final int BUTTON_HEIGHT_COMPACT_DP = 40;
    /** 字号层级：页面标题 18sp、顶栏标题 14sp、分组与正文 12sp、按钮 13sp、辅助 10sp。 */
    public static final float TEXT_PAGE_TITLE_SP = 18f;
    public static final float TEXT_TITLE_SP = 14f;
    public static final float TEXT_SECTION_SP = 12f;
    public static final float TEXT_BODY_SP = 12f;
    public static final float TEXT_BUTTON_SP = 13f;
    public static final float TEXT_BUTTON_COMPACT_SP = 12f;
    public static final float TEXT_HINT_SP = 10f;
    /** 数字等宽字形：速度、编号、坐标等会变化的数值用它避免宽度抖动。 */
    public static final android.graphics.Typeface NUMERIC = android.graphics.Typeface.MONOSPACE;
    /**
     * 语义动作色：启动/继续用绿，停止/恢复用红。
     * 旧的停止红 `rgb(126,76,76)` 饱和度只有约 40%，混进深蓝底后渲染成近中性暗灰（实测 #4B3D45），
     * 与禁用态难以区分，故改用更高饱和的红；白字对比度仍约 6:1。
     * 不加 final：常量会被内联进各模块，改色后旧字面量会留在已编译的调用方里。
     */
    public static int ACTION_START = Color.rgb(51, 147, 131);
    public static int ACTION_STOP = Color.rgb(206, 94, 94);

    /** 开始/启动按钮：实心青绿渐变，与设置页「连接游戏」一致，按下压暗一档。 */
    private static final int START_FILL_TOP = 0xFF52BDA9;
    private static final int START_FILL_BOTTOM = 0xFF339383;
    private static final int START_FILL_TOP_PRESSED = 0xFF41A794;
    private static final int START_FILL_BOTTOM_PRESSED = 0xFF2C7F72;
    private static final int START_STROKE = 0x59FFFFFF;

    /** 页面底色：从左上到右下的极轻渐变，避免整屏纯色的"死黑"。 */
    private static final int PAGE_TOP = 0xFF0E1F2B;
    private static final int PAGE_BOTTOM = 0xFF0A131B;

    /** 玻璃填充：带蓝色调的半透明深色，上浅下深形成极轻的表面高光。 */
    private static final int FILL_TOP = 0xE62F5669;
    private static final int FILL_BOTTOM = 0xD6214154;
    /** 选中/强调表面：略亮一档。 */
    private static final int FILL_STRONG_TOP = 0xED3A5F74;
    private static final int FILL_STRONG_BOTTOM = 0xE3284C60;
    /** 输入框/下拉框：略暗一档，与按钮区分。 */
    private static final int FILL_INPUT_TOP = 0xD41C3747;
    private static final int FILL_INPUT_BOTTOM = 0xCC28485B;
    /** 顶部信息条：比页面底色略亮的半透明带。 */
    private static final int FILL_HEADER_TOP = 0xE01D3B4C;
    private static final int FILL_HEADER_BOTTOM = 0xD2162F3D;
    /** 禁用表面：低对比的中性蓝灰。 */
    private static final int FILL_DISABLED_TOP = 0x8C1B303D;
    private static final int FILL_DISABLED_BOTTOM = 0x80162833;
    /**
     * 被着色按钮保留原按钮色相的比例。真机采样确认：比例过低会把"红=停止/绿=开始"冲淡成中性深灰；
     * 提到 0.72 后主操作能读成"实心"着色，同时正文对比度仍保持 6:1 以上。
     */
    private static final float TINT_RATIO = 0.72f;

    /** 主操作按钮：实心青绿，按下时压暗一档。 */
    private static final int ACCENT_FILL_TOP = 0xE641B5A1;
    private static final int ACCENT_FILL_BOTTOM = 0xE62F8B7B;

    private static final int STROKE_SOFT = 0x4D93AEBE;
    private static final int STROKE = 0x7A4AC9B0;
    private static final int STROKE_STRONG = 0xB34AC9B0;
    private static final int STROKE_DISABLED = 0x2E7C93A0;
    /** 表面顶部 1dp 内高光，制造玻璃的"上边缘反光"。 */
    private static final int HIGHLIGHT = 0x1AFFFFFF;
    /** 底部柔和阴影，范围小、强度低。 */
    private static final int SHADOW = 0x2E00070D;

    private GlassSkin() {
    }

    /** 页面背景：极轻的对角渐变，替代整屏纯色填充；不改变任何控件位置。 */
    public static Drawable page(Context context) {
        GradientDrawable fill = new GradientDrawable(
                GradientDrawable.Orientation.TL_BR, new int[]{PAGE_TOP, PAGE_BOTTOM});
        fill.setShape(GradientDrawable.RECTANGLE);
        return fill;
    }

    /** 标准玻璃表面：输入框之外的面板、下拉弹层等；带一点强调色，和页面底色拉开层次。 */
    public static Drawable surface(Context context, int radiusDp) {
        return glass(context, radiusDp,
                blend(FILL_TOP, ACCENT, 0.05f),
                blend(FILL_BOTTOM, ACCENT, 0.05f),
                STROKE, true);
    }

    /** 选中/强调表面：描边稍强，用于现有选中项。 */
    public static Drawable selected(Context context, int radiusDp) {
        return glass(context, radiusDp, FILL_STRONG_TOP, FILL_STRONG_BOTTOM, STROKE_STRONG, true);
    }

    /** 红色选中标记：用于需要一眼分辨的多选状态（如星级预设）。 */
    public static Drawable selectedDanger(Context context, int radiusDp) {
        return glass(context, radiusDp, 0xFFD4514F, 0xFFA53232, 0x99F2A0A0, true);
    }

    /**
     * 侧栏选中项：在强调玻璃表面上再叠一条左侧青绿指示条，强化"当前页"的层级。
     * 指示条用 LayerDrawable 的 layer 尺寸与重力定位，不改变条目本身的尺寸或内边距。
     */
    public static Drawable selectedNav(Context context, int radiusDp) {
        float radius = dp(context, radiusDp);
        GradientDrawable glow = new GradientDrawable();
        glow.setShape(GradientDrawable.RECTANGLE);
        glow.setColor(0x1F4AC9B0);
        glow.setCornerRadius(radius);

        GradientDrawable fill = new GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                new int[]{FILL_STRONG_TOP, FILL_STRONG_BOTTOM});
        fill.setShape(GradientDrawable.RECTANGLE);
        fill.setCornerRadius(radius);
        fill.setStroke(Math.max(1, dp(context, 1)), STROKE_STRONG);

        GradientDrawable bar = new GradientDrawable();
        bar.setShape(GradientDrawable.RECTANGLE);
        bar.setColor(ACCENT);
        bar.setCornerRadius(Math.max(1, dp(context, 1)));

        int offset = Math.max(1, dp(context, 1));
        LayerDrawable layered = new LayerDrawable(new Drawable[]{glow, fill, bar});
        layered.setLayerInset(0, 0, offset, 0, 0);
        layered.setLayerInset(1, 0, 0, 0, offset);
        layered.setLayerGravity(2, android.view.Gravity.START | android.view.Gravity.CENTER_VERTICAL);
        layered.setLayerWidth(2, Math.max(2, dp(context, 2)));
        layered.setLayerHeight(2, Math.max(6, dp(context, 18)));
        layered.setLayerInset(2, Math.max(1, dp(context, 1)), 0, 0, 0);
        return layered;
    }

    /** 输入框与下拉框表面。 */
    public static Drawable input(Context context, int radiusDp) {
        return glass(context, radiusDp, FILL_INPUT_TOP, FILL_INPUT_BOTTOM, STROKE_SOFT, true);
    }

    /** 顶部信息条：半透明底加低调细描边，底部阴影比普通表面深一档，滚动时层次更清楚。 */
    public static Drawable header(Context context) {
        GradientDrawable fill = new GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM, new int[]{FILL_HEADER_TOP, FILL_HEADER_BOTTOM});
        fill.setShape(GradientDrawable.RECTANGLE);
        fill.setStroke(Math.max(1, dp(context, 1)), STROKE_SOFT);
        GradientDrawable near = new GradientDrawable();
        near.setShape(GradientDrawable.RECTANGLE);
        near.setColor(0x3800060C);
        GradientDrawable far = new GradientDrawable();
        far.setShape(GradientDrawable.RECTANGLE);
        far.setColor(0x1C00060C);
        int one = Math.max(1, dp(context, 1));
        int two = Math.max(2, dp(context, 2));
        LayerDrawable layered = new LayerDrawable(new Drawable[]{far, near, fill});
        layered.setLayerInset(0, 0, two, 0, 0);
        layered.setLayerInset(1, 0, one, 0, 0);
        layered.setLayerInset(2, 0, 0, 0, two);
        return layered;
    }

    /**
     * 状态胶囊：保留调用方传入的状态色相（运行中/等待中/错误等），只把填充改为玻璃质感，
     * 描边沿用状态色，确保状态含义不变且始终可辨。
     */
    public static Drawable status(Context context, int radiusDp, int statusColor) {
        return glass(context, radiusDp,
                blend(FILL_TOP, statusColor, 0.22f),
                blend(FILL_BOTTOM, statusColor, 0.22f),
                withAlpha(statusColor, 0x99), true);
    }

    /** 操作按钮：默认青绿强调色，含按下与禁用状态。 */
    public static Drawable action(Context context, int radiusDp) {
        return actionStates(context, radiusDp, ACCENT, false);
    }

    /** 操作按钮：保留调用方原有的按钮色相（停止/危险为红色、动作为绿色等），
     * 只替换材质，避免改变按钮原有语义。开始/启动类统一使用实心青绿。
     */
    public static Drawable actionTinted(Context context, int radiusDp, int baseColor) {
        if (baseColor == ACTION_START) {
            return startStates(context, radiusDp);
        }
        return actionStates(context, radiusDp, baseColor, true);
    }

    /** 开始/启动按钮的三种状态：实心青绿渐变、按下压暗、禁用走中性暗色。 */
    private static Drawable startStates(Context context, int radiusDp) {
        StateListDrawable states = new StateListDrawable();
        states.addState(new int[]{-android.R.attr.state_enabled},
                glass(context, radiusDp, FILL_DISABLED_TOP, FILL_DISABLED_BOTTOM, STROKE_DISABLED, true));
        states.addState(new int[]{android.R.attr.state_pressed},
                glass(context, radiusDp, START_FILL_TOP_PRESSED, START_FILL_BOTTOM_PRESSED, START_STROKE, false));
        states.addState(new int[]{},
                glass(context, radiusDp, START_FILL_TOP, START_FILL_BOTTOM, START_STROKE, true));
        return states;
    }

    /**
     * 提醒态玻璃表面：填充保持深色以保证文字可读，只用指定色描边提示需要处理的状态，
     * 用于把授权等状态反馈压缩到已有的入口控件上，不再单独占用一整行。
     */
    public static Drawable attention(Context context, int radiusDp, int accentColor) {
        return glass(context, radiusDp, FILL_TOP, FILL_BOTTOM, withAlpha(accentColor, 0xB4), true);
    }

    /** 按钮文字色：可用与禁用两态，保证禁用状态继续可辨。 */
    public static ColorStateList actionText() {
        return new ColorStateList(
                new int[][]{new int[]{-android.R.attr.state_enabled}, new int[]{}},
                new int[]{TEXT_DISABLED, TEXT});
    }

    /**
     * 替换背景并保持控件原有内边距不变（包含原背景 drawable 贡献的内边距），
     * 因此控件的位置与尺寸不会因为换肤而改变。
     */
    public static void apply(View view, Drawable drawable) {
        int left = view.getPaddingLeft();
        int top = view.getPaddingTop();
        int right = view.getPaddingRight();
        int bottom = view.getPaddingBottom();
        view.setBackground(drawable);
        view.setPadding(left, top, right, bottom);
    }

    private static Drawable actionStates(Context context, int radiusDp, int baseColor, boolean tinted) {
        // 主操作（未着色）走实心青绿，和次级按钮拉开层级；着色按钮仍按语义色着色。
        int top = tinted ? blend(FILL_TOP, baseColor, TINT_RATIO) : ACCENT_FILL_TOP;
        int bottom = tinted ? blend(FILL_BOTTOM, baseColor, TINT_RATIO) : ACCENT_FILL_BOTTOM;
        int stroke = tinted ? withAlpha(blend(baseColor, Color.WHITE, 0.5f), 0xC8) : STROKE_STRONG;
        int pressedTop = blend(top, ACCENT, 0.22f);
        int pressedBottom = blend(bottom, ACCENT, 0.22f);

        StateListDrawable states = new StateListDrawable();
        states.addState(new int[]{-android.R.attr.state_enabled},
                glass(context, radiusDp, FILL_DISABLED_TOP, FILL_DISABLED_BOTTOM, STROKE_DISABLED, true));
        // 按下时去掉底部阴影层，表面"压下去"一层，形成更实在的按压反馈（尺寸与颜色语义不变）。
        states.addState(new int[]{android.R.attr.state_pressed},
                glass(context, radiusDp, pressedTop, pressedBottom, stroke, false));
        states.addState(new int[]{},
                glass(context, radiusDp, top, bottom, stroke, true));
        return states;
    }

    /**
     * 玻璃表面本体：一层向下偏移的柔和阴影 + 一层渐变填充与发丝描边。
     * 阴影只占底部 1dp，圆角与填充一致，不改变控件尺寸。
     */
    private static Drawable glass(Context context, int radiusDp, int topColor, int bottomColor,
                                  int strokeColor, boolean shadow) {
        float radius = dp(context, radiusDp);
        GradientDrawable fill = new GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM, new int[]{topColor, bottomColor});
        fill.setShape(GradientDrawable.RECTANGLE);
        fill.setCornerRadius(radius);
        if (strokeColor != 0) {
            fill.setStroke(Math.max(1, dp(context, 1)), strokeColor);
        }
        if (!shadow) {
            return fill;
        }

        // 顶部 1dp 内高光：玻璃上边缘的反光，只改观感，不占布局尺寸。
        GradientDrawable highlight = new GradientDrawable();
        highlight.setShape(GradientDrawable.RECTANGLE);
        highlight.setColor(HIGHLIGHT);
        highlight.setCornerRadius(Math.max(1, dp(context, 1)));

        GradientDrawable shadowLayer = new GradientDrawable();
        shadowLayer.setShape(GradientDrawable.RECTANGLE);
        shadowLayer.setColor(SHADOW);
        shadowLayer.setCornerRadius(radius);

        int offset = Math.max(1, dp(context, 1));
        LayerDrawable layered = new LayerDrawable(new Drawable[]{shadowLayer, fill, highlight});
        layered.setLayerInset(0, 0, offset, 0, 0);
        layered.setLayerInset(1, 0, 0, 0, offset);
        layered.setLayerGravity(2, android.view.Gravity.TOP | android.view.Gravity.FILL_HORIZONTAL);
        layered.setLayerHeight(2, Math.max(1, dp(context, 1)));
        int insetX = Math.max(6, dp(context, 8));
        layered.setLayerInset(2, insetX, 1, insetX, 0);
        return layered;
    }

    private static int dp(Context context, int value) {
        if (value == 0) {
            return 0;
        }
        return Math.round(value * context.getResources().getDisplayMetrics().density);
    }

    /** 按比例把 base 混入 color，保留 base 的透明度。 */
    private static int blend(int base, int color, float ratio) {
        float keep = 1f - ratio;
        return Color.argb(Color.alpha(base),
                Math.round(Color.red(base) * keep + Color.red(color) * ratio),
                Math.round(Color.green(base) * keep + Color.green(color) * ratio),
                Math.round(Color.blue(base) * keep + Color.blue(color) * ratio));
    }

    private static int withAlpha(int color, int alpha) {
        return Color.argb(alpha, Color.red(color), Color.green(color), Color.blue(color));
    }
}
