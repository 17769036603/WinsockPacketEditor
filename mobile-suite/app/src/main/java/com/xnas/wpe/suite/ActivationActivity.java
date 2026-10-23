package com.xnas.wpe.suite;

import android.app.Activity;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Bundle;
import android.text.InputType;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * 授权页。普通朋友版展示激活、重新验证与返回；免激活版本隐藏授权操作并说明游戏版本限制：
 * 顶行是标题、当前授权状态与返回，第二行是安装识别码与到期口径，第三行把激活码输入与
 * 激活/重新验证放在同一行，第四行只在有操作结果或失败提示时出现，最后是构建版本。
 */
public final class ActivationActivity extends Activity {
    /** 本页未使用 CompactUi，尺寸按设备密度由 GlassSkin 的公共变量换算。 */
    private static final int AMBER = Color.rgb(255, 190, 92);

    private TextView status;
    private TextView licenseState;

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        int padX = Math.round(GlassSkin.PADDING_H_DP * getResources().getDisplayMetrics().density);
        int padY = Math.round(GlassSkin.PADDING_V_DP * getResources().getDisplayMetrics().density);
        root.setPadding(padX, padY, padX, padY);
        root.setBackground(GlassSkin.page(this));
        int rowHeight = Math.round(GlassSkin.BUTTON_HEIGHT_DP * getResources().getDisplayMetrics().density);
        int compactHeight = Math.round(GlassSkin.BUTTON_HEIGHT_COMPACT_DP * getResources().getDisplayMetrics().density);

        LinearLayout titleRow = new LinearLayout(this);
        titleRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = new TextView(this);
        title.setText("飘渺助手授权");
        title.setTextColor(GlassSkin.TEXT);
        title.setTextSize(GlassSkin.TEXT_PAGE_TITLE_SP);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        titleRow.addView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        licenseState = new TextView(this);
        licenseState.setTextSize(GlassSkin.TEXT_BODY_SP);
        licenseState.setPadding(14, 5, 14, 5);
        titleRow.addView(licenseState);
        Button close = glassSecondaryButton("返回", compactHeight, GlassSkin.TEXT_BUTTON_COMPACT_SP);
        close.setOnClickListener(v -> finish());
        titleRow.addView(close, spacedParams(compactHeight));
        root.addView(titleRow, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView info = new TextView(this);
        info.setText("安装识别码：" + LicenseManager.shortInstallationId(this)
                + "（解绑时提供）　到期时间按设备本地时区显示；修改系统时间不影响授权校验。");
        info.setTextColor(GlassSkin.TEXT_MUTED);
        info.setTextSize(GlassSkin.TEXT_HINT_SP + 1f);
        info.setSingleLine(true);
        info.setEllipsize(TextUtils.TruncateAt.END);
        info.setPadding(0, 10, 0, 0);
        root.addView(info, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        if (LicenseManager.BYPASS) {
            TextView limited = new TextView(this);
            String gameVersion = BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_NAME
                    + "（versionCode " + BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_CODE + "）";
            limited.setText("此朋友版无需激活，仅兼容 DHXY / XXXY2026 的 " + gameVersion
                    + "；更换游戏版本后功能会停用。此限制用于版本兼容，不代表可防止同版本 APK 转发。");
            limited.setTextColor(GlassSkin.TEXT_MUTED);
            limited.setTextSize(GlassSkin.TEXT_BODY_SP);
            limited.setPadding(0, 16, 0, 0);
            root.addView(limited, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        } else {
            LinearLayout actionRow = new LinearLayout(this);
            actionRow.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout.LayoutParams actionParams = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            actionParams.topMargin = 16;
            root.addView(actionRow, actionParams);

            EditText code = new EditText(this);
            code.setSingleLine(true);
            code.setHint("输入32位激活码");
            code.setTextColor(GlassSkin.TEXT);
            code.setHintTextColor(GlassSkin.TEXT_DISABLED);
            code.setGravity(Gravity.CENTER_VERTICAL);
            code.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_CAP_CHARACTERS);
            code.setTypeface(GlassSkin.NUMERIC);
            GlassSkin.apply(code, GlassSkin.input(this, GlassSkin.RADIUS_CONTROL_DP));
            actionRow.addView(code, new LinearLayout.LayoutParams(0, rowHeight, 1f));

            Button activate = glassButton("激活", rowHeight, GlassSkin.TEXT_BUTTON_SP);
            activate.setOnClickListener(v -> {
                setStatus("正在激活…");
                LicenseManager.activate(this, code.getText().toString(), (m, ok) -> {
                    setStatus(m);
                    if (ok) setResult(RESULT_OK);
                    refreshLicenseState();
                });
            });
            actionRow.addView(activate, spacedParams(rowHeight));

            Button verify = glassButton("重新验证", rowHeight, GlassSkin.TEXT_BUTTON_SP);
            verify.setOnClickListener(v -> {
                setStatus("正在验证…");
                LicenseManager.verify(this, (m, ok) -> {
                    setStatus(m);
                    refreshLicenseState();
                });
            });
            actionRow.addView(verify, spacedParams(rowHeight));
        }

        status = new TextView(this);
        status.setTextColor(GlassSkin.TEXT_MUTED);
        status.setTextSize(GlassSkin.TEXT_BODY_SP);
        status.setPadding(0, 12, 0, 0);
        root.addView(status, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        setStatus(LicenseManager.BYPASS
                ? null
                : (LicenseManager.configured() ? null : "授权服务未配置"));

        View versionDivider = new View(this);
        versionDivider.setBackgroundColor(0x24FFFFFF);
        LinearLayout.LayoutParams dividerParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, Math.max(1, Math.round(getResources().getDisplayMetrics().density)));
        dividerParams.topMargin = 14;
        root.addView(versionDivider, dividerParams);

        TextView version = new TextView(this);
        version.setText("版本 " + shortVersionName(installedVersionName()) + " · " + BuildConfig.VERSION_CODE);
        version.setTextColor(GlassSkin.TEXT_DISABLED);
        version.setTextSize(GlassSkin.TEXT_HINT_SP);
        version.setPadding(0, 8, 0, 0);
        root.addView(version, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        setContentView(root);
        refreshLicenseState();
        if (!LicenseManager.BYPASS && LicenseManager.configured()) {
            // 后台校验只在失败时占用状态行；成功时顶行状态胶囊已反映当前授权，避免重复显示同一句话。
            LicenseManager.verify(this, (m, ok) -> {
                if (!ok) setStatus(m);
                refreshLicenseState();
            });
        }
    }

    /** 操作结果只在有内容时占位，空闲时整行收起，页面保持紧凑。 */
    private void setStatus(String message) {
        if (status == null) return;
        boolean empty = message == null || message.isEmpty();
        status.setText(empty ? "" : message);
        status.setVisibility(empty ? View.GONE : View.VISIBLE);
    }

    private Button glassButton(String label, int height, float textSize) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setTextSize(textSize);
        button.setMinHeight(0);
        button.setMinimumHeight(0);
        button.setMinWidth(0);
        button.setMinimumWidth(0);
        GlassSkin.apply(button, GlassSkin.action(this, 8));
        button.setTextColor(GlassSkin.actionText());
        return button;
    }

    /** 次级按钮：玻璃表面 + 常规文字色，不与主操作抢强调色。 */
    private Button glassSecondaryButton(String label, int height, float textSize) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setTextSize(textSize);
        button.setMinHeight(0);
        button.setMinimumHeight(0);
        button.setMinWidth(0);
        button.setMinimumWidth(0);
        GlassSkin.apply(button, GlassSkin.surface(this, 8));
        button.setTextColor(GlassSkin.TEXT);
        return button;
    }

    private LinearLayout.LayoutParams spacedParams(int height) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, height);
        params.leftMargin = 10;
        return params;
    }

    /** 顶行右侧的授权状态：已生效为常规玻璃加青绿文字，未生效为琥珀描边加琥珀文字。 */
    private void refreshLicenseState() {
        if (licenseState == null) return;
        boolean active = LicenseManager.isActive();
        licenseState.setText(LicenseManager.displayStatus(this));
        licenseState.setTextColor(active ? GlassSkin.ACCENT : AMBER);
        GlassSkin.apply(licenseState, active
                ? GlassSkin.surface(this, GlassSkin.RADIUS_PILL_DP)
                : GlassSkin.attention(this, GlassSkin.RADIUS_PILL_DP, AMBER));
    }

    /** 主界面不再占用顶行显示版本，构建识别信息放在授权页。 */
    /** 只保留版本主号，去掉渠道后缀，例如 2026.10.18.1。 */
    private static String shortVersionName(String name) {
        int cut = name == null ? -1 : name.indexOf('-');
        return cut > 0 ? name.substring(0, cut) : name;
    }

    private String installedVersionName() {
        try {
            String name=getPackageManager().getPackageInfo(getPackageName(),0).versionName;
            return name==null||name.isEmpty()?"未知版本":name;
        } catch (android.content.pm.PackageManager.NameNotFoundException error) {
            return "未知版本";
        }
    }
}
