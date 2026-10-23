package com.xnas.wpe.battleloop;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.net.Uri;
import android.provider.Settings;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.io.*;
import java.nio.charset.StandardCharsets;

public final class MainActivity extends Activity {
    private final Handler handler=new Handler();
    private TextView status;
    private final Runnable refresh=new Runnable(){@Override public void run(){if(status!=null){try{File f=new File(getFilesDir(),"status");String raw=f.isFile()?read(f):"state=stopped";status.setText(display(raw));}catch(Exception e){status.setText("状态：读取失败");}handler.postDelayed(this,1000);}}};
    @Override public void onCreate(Bundle saved){super.onCreate(saved);LinearLayout root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);int pad=Math.round(24*getResources().getDisplayMetrics().density);root.setPadding(pad,pad,pad,pad);TextView title=new TextView(this);title.setText("战斗循环助手");title.setTextSize(20);root.addView(title);TextView rootInfo=new TextView(this);rootInfo.setText("需要已 Root 的 x86_64 Android 环境，并开启悬浮窗权限。启动后等待游戏与角色绑定。\n");root.addView(rootInfo);status=new TextView(this);root.addView(status);Button overlayPermission=new Button(this);overlayPermission.setText("开启悬浮窗权限");overlayPermission.setOnClickListener(v->startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,Uri.parse("package:"+getPackageName()))));root.addView(overlayPermission);Button start=new Button(this);start.setText("开始");start.setOnClickListener(v->{Intent i=new Intent(this,BattleLoopService.class).setAction(BattleLoopService.START);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(i);else startService(i);});root.addView(start);Button stop=new Button(this);stop.setText("停止");stop.setOnClickListener(v->startService(new Intent(this,BattleLoopService.class).setAction(BattleLoopService.STOP)));root.addView(stop);setContentView(root);}
    @Override protected void onResume(){super.onResume();if(android.os.Build.VERSION.SDK_INT<23||Settings.canDrawOverlays(this))startService(new Intent(this,BattleLoopService.class).setAction(BattleLoopService.OVERLAY));handler.post(refresh);}
    @Override protected void onPause(){handler.removeCallbacks(refresh);super.onPause();}
    private static String read(File f)throws Exception{try(FileInputStream in=new FileInputStream(f);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return out.toString("UTF-8");}}
    private static String value(String s,String key){for(String line:s.split("\\n"))if(line.startsWith(key+"="))return line.substring(key.length()+1);return "未确认";}
    private static String display(String s){String state=value(s,"state");String result="状态：";switch(state){case "stopped":result+="已停止";break;case "waiting_game":result+="等待游戏启动";break;case "waiting_role":result+="等待角色登录或重新绑定";break;case "waiting_battle":result+="等待战斗";break;case "waiting_config":result+="等待人物/宝宝当前自动技能设置";break;case "waiting_conflict":result+="检测到桌面辅助冲突，已暂停";break;case "running":result+="运行中";break;case "stopping":result+="正在恢复自动状态并清理 Hooks";break;case "error":result+="运行失败，请查看 worker 日志";break;default:result+="正在监控游戏";}String player=value(s,"characterSkillId"),pet=value(s,"petSkillId"),petId=value(s,"petId");if(!player.equals("未确认"))result+="\n人物技能 ID："+player;if(!pet.equals("未确认")&&!pet.equals("none"))result+="\n宝宝 ID："+petId+"，技能 ID："+pet;return result;}
}
