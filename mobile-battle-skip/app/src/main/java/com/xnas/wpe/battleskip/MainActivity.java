package com.xnas.wpe.battleskip;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

public final class MainActivity extends Activity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setFitsSystemWindows(true);
        int padding = Math.round(24 * getResources().getDisplayMetrics().density);
        layout.setPadding(padding, padding, padding, padding);

        TextView hint = new TextView(this);
        hint.setText("选择操作后返回游戏。首次启动请允许 Root；停止后请保持游戏在前台，等待清理完成。");
        layout.addView(hint);

        Button start = new Button(this);
        start.setText("启动快进");
        start.setOnClickListener(view -> {
            startActivity(new Intent().setClassName(this, getPackageName() + ".Start"));
            finish();
        });
        layout.addView(start);

        Button stop = new Button(this);
        stop.setText("停止快进");
        stop.setOnClickListener(view -> {
            startActivity(new Intent().setClassName(this, getPackageName() + ".Stop"));
            finish();
        });
        layout.addView(stop);

        setContentView(layout);
    }
}
