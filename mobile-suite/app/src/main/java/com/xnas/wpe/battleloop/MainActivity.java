package com.xnas.wpe.battleloop;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.io.*;
import java.nio.charset.StandardCharsets;
import com.xnas.wpe.suite.GlassSkin;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;
import com.xnas.wpe.suite.UiStatusFiles;

public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final long OLD_TASK_CHECK_THROTTLE_MS=10_000L;
    private final Handler handler=new Handler();
    private TextView status;
    private TextView visualStatus;
    private TextView hideHint;
    private String actualStatusForVisuals;
    private String lastDisplayedStatus;
    private CheckBox skipBattleCheckbox;
    private CheckBox hideBattleCheckbox;
    private Button pauseButton;
    private Button checkButton;
    private Button copyVisualLogButton;
    private long lastOldTaskCheckRequestAt;
    private boolean autoStartPending;
    private boolean updatingModeCheckboxes;
    private final Runnable refresh=new Runnable(){@Override public void run(){if(status!=null){File dir=getFilesDir();String targetKey=TargetGame.getSelectedPackage(MainActivity.this);String raw=currentStatus(dir,targetKey);lastDisplayedStatus=raw;status.setText(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,targetKey)?"此功能仅支持 DHXY":raw==null?"状态：状态读取中，运行情况未确认":display(raw,dir));if(visualStatus!=null)visualStatus.setText(visualDisplay(raw));refreshBattleModeControls(raw);if(autoStartPending&&!BattleLoopService.isCheckingOldTask()&&!com.xnas.wpe.battleskip.BattleSkipRecoveryService.isChecking()){autoStartPending=false;try{if(!BattleVisualOptions.readSettings(MainActivity.this).hasAnyModeEnabled()){status.setText("至少开启一项：跳过战斗或隐藏战斗画面。已完成只读核查，未启动。");}else if(com.xnas.wpe.suite.BattleConflictGate.settled(getFilesDir().getParentFile(),"battleloop"))dispatchServiceAction(BattleLoopService.START);}catch(RuntimeException ignored){}}handler.postDelayed(this,autoStartPending||BattleLoopService.isActive()?1000L:2000L);}}};
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);
        ScrollView scroll=new ScrollView(this);
        scroll.setFillViewport(true);
        LinearLayout root=new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        int densityPadX=Math.round(GlassSkin.PADDING_H_DP*getResources().getDisplayMetrics().density);
        int densityPadY=Math.round(GlassSkin.PADDING_V_DP*getResources().getDisplayMetrics().density);
        root.setPadding(densityPadX,densityPadY,densityPadX,densityPadY);
        root.setBackground(GlassSkin.page(this));

        status=new TextView(this);
        status.setTextColor(GlassSkin.TEXT);
        status.setTextSize(GlassSkin.TEXT_BODY_SP);
        visualStatus=new TextView(this);
        visualStatus.setTextColor(GlassSkin.TEXT);
        visualStatus.setTextSize(GlassSkin.TEXT_BODY_SP);
        visualStatus.setPadding(0,Math.round(6*getResources().getDisplayMetrics().density),0,0);

        skipBattleCheckbox=new CheckBox(this);
        skipBattleCheckbox.setText("跳过战斗");
        skipBattleCheckbox.setTextColor(GlassSkin.TEXT);
        skipBattleCheckbox.setTextSize(GlassSkin.TEXT_BODY_SP);
        hideBattleCheckbox=new CheckBox(this);
        hideBattleCheckbox.setText("隐藏战斗画面");
        hideBattleCheckbox.setTextColor(GlassSkin.TEXT);
        hideBattleCheckbox.setTextSize(GlassSkin.TEXT_BODY_SP);
        updatingModeCheckboxes=true;
        BattleVisualOptions.Snapshot settings=BattleVisualOptions.readSettings(this);
        skipBattleCheckbox.setChecked(settings.skipBattle);
        hideBattleCheckbox.setChecked(settings.hideBattle);
        updatingModeCheckboxes=false;
        skipBattleCheckbox.setOnCheckedChangeListener((button,isChecked)->onModeCheckboxChanged(button,isChecked,BattleVisualOptions.SKIP_BATTLE_KEY));
        hideBattleCheckbox.setOnCheckedChangeListener((button,isChecked)->onModeCheckboxChanged(button,isChecked,BattleVisualOptions.HIDE_BATTLE_KEY));
        hideHint=new TextView(this);
        hideHint.setTextColor(GlassSkin.TEXT_MUTED);
        hideHint.setTextSize(GlassSkin.TEXT_HINT_SP);

        int rowH=Math.round(GlassSkin.BUTTON_HEIGHT_DP*getResources().getDisplayMetrics().density);
        int compactH=Math.round(GlassSkin.BUTTON_HEIGHT_COMPACT_DP*getResources().getDisplayMetrics().density);
        Button start=new Button(this);
        start.setText("启动");
        start.setTextSize(GlassSkin.TEXT_BUTTON_SP);
        start.setOnClickListener(v->requestStart());
        pauseButton=new Button(this);
        pauseButton.setText("暂停");
        pauseButton.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP);
        pauseButton.setOnClickListener(v->{boolean resuming="继续".contentEquals(pauseButton.getText());dispatchServiceAction(resuming?BattleLoopService.RESUME:BattleLoopService.PAUSE);pauseButton.setText(resuming?"暂停":"继续");});
        GlassSkin.apply(pauseButton,GlassSkin.surface(this,8));
        pauseButton.setTextColor(GlassSkin.TEXT);
        checkButton=new Button(this);
        checkButton.setText("只读核查旧任务");
        checkButton.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP);
        checkButton.setOnClickListener(v->requestOldTaskCheck());
        GlassSkin.apply(checkButton,GlassSkin.surface(this,8));
        checkButton.setTextColor(GlassSkin.TEXT);
        Button stop=new Button(this);
        stop.setText("停止");
        stop.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP);
        stop.setOnClickListener(v->dispatchServiceAction(BattleLoopService.STOP));
        copyVisualLogButton=new Button(this);
        copyVisualLogButton.setText("复制隐藏战斗日志");
        copyVisualLogButton.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP);
        copyVisualLogButton.setOnClickListener(v->copyVisualLogAsync());
        GlassSkin.apply(start,GlassSkin.actionTinted(this,8,GlassSkin.ACTION_START));
        start.setTextColor(GlassSkin.actionText());
        GlassSkin.apply(stop,GlassSkin.actionTinted(this,8,GlassSkin.ACTION_STOP));
        stop.setTextColor(GlassSkin.actionText());
        GlassSkin.apply(copyVisualLogButton,GlassSkin.surface(this,8));
        copyVisualLogButton.setTextColor(GlassSkin.TEXT);

        int gapPx=Math.round(8*getResources().getDisplayMetrics().density);
        root.addView(status,new LinearLayout.LayoutParams(-1,-2));
        root.addView(visualStatus,new LinearLayout.LayoutParams(-1,-2));
        LinearLayout.LayoutParams checkboxParams=new LinearLayout.LayoutParams(-1,-2);
        checkboxParams.topMargin=gapPx;
        root.addView(skipBattleCheckbox,checkboxParams);
        LinearLayout.LayoutParams hideCheckboxParams=new LinearLayout.LayoutParams(-1,-2);
        root.addView(hideBattleCheckbox,hideCheckboxParams);
        root.addView(hideHint,new LinearLayout.LayoutParams(-1,-2));
        android.view.View topSpace=new android.view.View(this);
        root.addView(topSpace,new LinearLayout.LayoutParams(-1,0,1f));
        LinearLayout.LayoutParams lps=new LinearLayout.LayoutParams(-1,rowH);lps.setMargins(0,0,0,gapPx);root.addView(start,lps);
        LinearLayout.LayoutParams lpp=new LinearLayout.LayoutParams(-1,compactH);lpp.setMargins(0,0,0,gapPx);root.addView(pauseButton,lpp);
        LinearLayout.LayoutParams stopParams=new LinearLayout.LayoutParams(-1,compactH);stopParams.setMargins(0,0,0,gapPx);root.addView(stop,stopParams);
        LinearLayout.LayoutParams checkParams=new LinearLayout.LayoutParams(-1,compactH);checkParams.setMargins(0,0,0,gapPx);root.addView(checkButton,checkParams);
        root.addView(copyVisualLogButton,new LinearLayout.LayoutParams(-1,compactH));
        android.view.View bottomSpace=new android.view.View(this);
        root.addView(bottomSpace,new LinearLayout.LayoutParams(-1,0,1f));
        scroll.addView(root,new ScrollView.LayoutParams(-1,-1));
        setContentView(scroll);
        refreshBattleModeControls(null);
    }
    private void onModeCheckboxChanged(android.widget.CompoundButton button,boolean isChecked,String preferenceKey){
        if(updatingModeCheckboxes)return;
        if(!BattleVisualOptions.canEdit(this)){
            syncModeCheckboxesFromSettings();
            refreshBattleModeControls(lastDisplayedStatus);
            android.widget.Toast.makeText(this,"运行或清理未确认期间不能修改战斗模式设置。",android.widget.Toast.LENGTH_SHORT).show();
            return;
        }
        getSharedPreferences(BattleVisualOptions.PREFERENCES_NAME,MODE_PRIVATE).edit()
                .putBoolean(preferenceKey,isChecked).apply();
        refreshBattleModeControls(lastDisplayedStatus);
    }
    private boolean canEditFromSnapshot(String raw){
        if(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,TargetGame.getSelectedPackage(this))
                ||BattleLoopService.isActive()||BattleLoopService.isCheckingOldTask()||raw==null)return false;
        if(raw.isEmpty())return true;
        String state=value(raw,"state");
        if("startable".equals(state))return true;
        if(!"stopped".equals(state))return false;
        if("true".equals(value(raw,"processEnded")))return true;
        if(!"true".equals(value(raw,"hooksRemoved"))
                ||!"true".equals(value(raw,"bindingCleared"))
                ||!"true".equals(value(raw,"restoreConfirmed")))return false;
        return !"true".equals(value(raw,"hideBattleRequested"))
                ||"true".equals(value(raw,"visibilityRestoreConfirmed"));
    }
    private void refreshBattleModeControls(String raw){
        if(skipBattleCheckbox==null||hideBattleCheckbox==null)return;
        boolean editable=canEditFromSnapshot(raw);
        skipBattleCheckbox.setEnabled(editable);
        hideBattleCheckbox.setEnabled(editable);
        if(checkButton!=null)checkButton.setEnabled(!BattleLoopService.isActive());
        if(hideHint!=null)hideHint.setText(editable
                ?"至少开启一个模式；更改只在下一次启动生效。运行或清理未确认期间不可修改。"
                :"本次运行模式已固定；等待停止和清理确认后可修改，下一次启动生效。");
    }
    private void syncModeCheckboxesFromSettings(){
        if(skipBattleCheckbox==null||hideBattleCheckbox==null)return;
        BattleVisualOptions.Snapshot settings=BattleVisualOptions.readSettings(this);
        updatingModeCheckboxes=true;
        if(skipBattleCheckbox.isChecked()!=settings.skipBattle)skipBattleCheckbox.setChecked(settings.skipBattle);
        if(hideBattleCheckbox.isChecked()!=settings.hideBattle)hideBattleCheckbox.setChecked(settings.hideBattle);
        updatingModeCheckboxes=false;
    }
    private String visualDisplay(String raw){
        raw=visualSource(raw);
        if(raw==null||raw.isEmpty())return "本次跳过：未确认\n本次隐藏：未确认\n隐藏画面：当前没有运行状态";
        boolean processEnded="true".equals(value(raw,"processEnded"));
        boolean hasRunSnapshot=hasModeFields(raw)||processEnded||legacyRunSnapshot(raw);
        String skipRequested=value(raw,"skipBattleRequested");
        if(skipRequested==null&&hasRunSnapshot)skipRequested="true";
        String requested=value(raw,"hideBattleRequested");
        if(requested==null&&hasRunSnapshot)requested="false";
        String visual=value(raw,"visualState");
        String battles=value(raw,"hiddenBattles");
        String rounds=value(raw,"visualRounds");
        if(!hasRunSnapshot&&requested==null&&visual==null&&battles==null&&rounds==null)
            return "本次跳过：未确认\n本次隐藏：未确认\n隐藏画面：当前状态没有视觉记录";
        return (processEnded?"旧游戏进程已结束；视觉恢复未确认\n":"")
                +"本次跳过："+requestLabel(skipRequested)
                +"\n本次隐藏："+requestLabel(requested)
                +"\n画面状态："+(processEnded?"未确认":visualStateLabel(visual))
                +"\n已隐藏战斗："+countLabel(battles)
                +"；累计回合："+countLabel(rounds);
    }
    private static String requestLabel(String value){
        if("true".equals(value))return "开启";
        if("false".equals(value))return "关闭";
        return "未确认";
    }
    private static String countLabel(String value){
        if(value==null||value.isEmpty())return "未确认";
        for(int i=0;i<value.length();i++)if(value.charAt(i)<'0'||value.charAt(i)>'9')return "未确认";
        return value;
    }
    private static boolean legacyRunSnapshot(String raw){
        String state=value(raw,"state");
        if(state==null)return false;
        switch(state){
            case "starting":case "checking":case "startable":case "verified":case "unconfirmed":case "conflict_wait":
                return false;
            default:return true;
        }
    }
    private static String visualStateLabel(String value){
        if(value==null)return "未确认";
        switch(value){
            case "disabled":return "关闭";
            case "waiting_battle":return "等待战斗";
            case "waiting_next_battle":return "等待下一场战斗";
            case "hiding":return "正在隐藏";
            case "hidden":return "隐藏中";
            case "hide_unconfirmed":return "隐藏状态未确认";
            case "battle_ended":return "战斗已结束";
            case "restored_paused":return "暂停后已恢复";
            case "restored":return "已恢复";
            case "restoring":return "正在恢复";
            case "restore_unconfirmed":return "恢复未确认";
            case "restore_unconfirmed_identity_changed":return "身份变化，恢复未确认";
            case "restore_unconfirmed_scene_changed":return "场景变化，恢复未确认";
            case "stopped":return "已停止";
            case "not_started":return "未开始";
            case "not_needed":return "无需恢复";
            default:return "未确认";
        }
    }
    private void copyVisualLogAsync(){
        if(copyVisualLogButton==null)return;
        copyVisualLogButton.setEnabled(false);
        copyVisualLogButton.setText("正在读取…");
        File log=new File(getFilesDir(),BattleVisualOptions.VISUAL_LOG_FILE_NAME);
        new Thread(()->{
            String contents;
            try(RandomAccessFile input=new RandomAccessFile(log,"r")){
                long length=input.length();
                if(length<=0){contents="";}
                else{
                    int count=(int)Math.min((long)BattleVisualOptions.MAX_VISUAL_LOG_BYTES,length);
                    input.seek(length-count);
                    byte[] tail=new byte[count];
                    int offset=0;
                    while(offset<count){
                        int read=input.read(tail,offset,count-offset);
                        if(read<0)break;
                        offset+=read;
                    }
                    contents=new String(tail,0,offset,StandardCharsets.UTF_8);
                }
            }catch(FileNotFoundException missing){
                contents="";
            }catch(IOException error){
                contents=null;
            }
            final String result=contents;
            handler.post(()->{
                if(isFinishing()||isDestroyed())return;
                copyVisualLogButton.setEnabled(true);
                copyVisualLogButton.setText("复制隐藏战斗日志");
                if(result==null){
                    android.widget.Toast.makeText(this,"隐藏战斗日志读取失败。",android.widget.Toast.LENGTH_SHORT).show();
                    return;
                }
                if(result.trim().isEmpty()){
                    android.widget.Toast.makeText(this,"暂无隐藏战斗日志。",android.widget.Toast.LENGTH_SHORT).show();
                    return;
                }
                ClipboardManager clipboard=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);
                if(clipboard!=null)clipboard.setPrimaryClip(ClipData.newPlainText("visual.log",result));
                android.widget.Toast.makeText(this,"已复制最近最多 64 KiB 隐藏战斗日志。",android.widget.Toast.LENGTH_SHORT).show();
            });
        },"battleloop-visual-log-copy").start();
    }
    private void requestStart(){if(pauseButton!=null)pauseButton.setText("暂停");
        if(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,TargetGame.getSelectedPackage(this))){status.setText("此功能仅支持 DHXY");return;}
        if(!BattleVisualOptions.readSettings(this).hasAnyModeEnabled()){
            status.setText("至少开启一项：跳过战斗或隐藏战斗画面。未启动。请勾选一项后重试。");
            android.widget.Toast.makeText(this,"至少开启一项：跳过战斗或隐藏战斗画面。",android.widget.Toast.LENGTH_SHORT).show();
            return;
        }
        File dir=getFilesDir();
        if(BattleLoopService.isCheckingOldTask()||com.xnas.wpe.battleskip.BattleSkipRecoveryService.isChecking()){
            autoStartPending=true;status.setText("正在准备，请稍候；核对完成后会自动启动。");return;
        }
        if(!BattleLoopService.isActive()&&com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(dir.getParentFile())){
            long now=System.currentTimeMillis();File recovery=new File(dir,"recovery-status");long recoveryAge=now-recovery.lastModified();
            boolean recentlyChecked=recovery.isFile()&&recoveryAge>=0&&recoveryAge<OLD_TASK_CHECK_THROTTLE_MS;
            long requestAge=now-lastOldTaskCheckRequestAt;
            if(recentlyChecked||(lastOldTaskCheckRequestAt!=0&&requestAge>=0&&requestAge<OLD_TASK_CHECK_THROTTLE_MS)){
                status.setText("战斗跳过旧任务刚核查过，保护状态仍有效；请查看当前结果，确认后再次点击启动。");return;
            }
            lastOldTaskCheckRequestAt=now;
            try{
                dispatchServiceAction(BattleLoopService.CHECK);
                autoStartPending=true;status.setText("发现未确认的战斗跳过旧任务，正在只读核查；核对完成后会自动启动。");
            }catch(RuntimeException error){
                status.setText("旧任务核查请求未发送；战斗启动保护仍保留，暂未启动。");
            }
            return;
        }
        if(!BattleLoopService.isActive()&&com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedSkipActivity(dir.getParentFile())){
            long now=System.currentTimeMillis();
            if(lastOldTaskCheckRequestAt!=0&&now-lastOldTaskCheckRequestAt<OLD_TASK_CHECK_THROTTLE_MS)return;
            lastOldTaskCheckRequestAt=now;
            Intent check=new Intent(this,com.xnas.wpe.battleskip.BattleSkipRecoveryService.class);
            try{if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(check);else startService(check);
                autoStartPending=true;status.setText("正在确认上次快进是否退出；核对完成后会自动启动。");}
            catch(RuntimeException error){status.setText("无法确认上次快进是否退出，暂未启动。");}
            return;
        }
        try{dispatchServiceAction(BattleLoopService.START);}
        catch(RuntimeException error){status.setText("启动请求未发送；现有战斗保护状态保留。");}
    }
    private void requestOldTaskCheck(){
        autoStartPending=false;
        if(BattleLoopService.isActive()){
            String state=value(lastDisplayedStatus,"state");
            status.setText("stopping".equals(state)
                    ?"战斗循环正在清理，请等待清理确认后再核查旧任务。"
                    :"战斗循环正在运行，当前不能核查旧任务。请停止并等待清理确认后重试。");
            return;
        }
        if(BattleLoopService.isCheckingOldTask()||com.xnas.wpe.battleskip.BattleSkipRecoveryService.isChecking()){
            status.setText("旧任务正在只读核查，请稍候。");
            return;
        }
        try{
            dispatchServiceAction(BattleLoopService.CHECK);
            status.setText("已请求只读核查旧任务；核查不会启动战斗模式。");
        }catch(RuntimeException error){
            status.setText("旧任务只读核查请求未发送；现有保护状态保留。");
        }
    }
    private void dispatchServiceAction(String action){Intent intent=new Intent(this,BattleLoopService.class).setAction(action);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(intent);else startService(intent);}
    @Override protected void onResume(){super.onResume();syncModeCheckboxesFromSettings();handler.post(refresh);}
    @Override protected void onPause(){handler.removeCallbacks(refresh);super.onPause();}
    private String currentStatus(File dir,String targetKey){
        UiStatusFiles.Snapshot actualSnapshot=UiStatusFiles.readForDisplay("battleloop",new File(dir,"status"),targetKey);
        if(!actualSnapshot.known){actualStatusForVisuals=null;return null;}
        String actual=actualSnapshot.exists?actualSnapshot.text:"";File modulesRoot=dir.getParentFile();
        actualStatusForVisuals=actual;
        if(!BattleLoopService.isActive()&&com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedSkipActivity(modulesRoot)){
            File skipStatus=new File(new File(modulesRoot,"battleskip"),"status");UiStatusFiles.Snapshot skipSnapshot=UiStatusFiles.readForDisplay("battleskip",skipStatus,targetKey);String skip=skipSnapshot.known&&skipSnapshot.exists?skipSnapshot.text:"";
            String reason=com.xnas.wpe.suite.BattleWorkerStatus.isRunning(skip)
                ?"战斗快进正在运行，已阻止启动战斗跳过；请先到“战斗快进”页面停止快进，清理确认后再启动。"
                :"战斗快进上次任务未确认退出，已阻止启动战斗跳过；请先到“战斗快进”页面停止快进并等待清理确认。";
            return "state=conflict_wait\nmessage="+reason+"\n";
        }
        if(com.xnas.wpe.suite.BattleConflictGate.settled(modulesRoot,"battleloop"))return actual;
        File recovery=new File(dir,"recovery-status");if(!BattleLoopService.isActive()){UiStatusFiles.Snapshot recoverySnapshot=UiStatusFiles.readForDisplay("battleloop",recovery,targetKey);if(!recoverySnapshot.known)return null;if(recoverySnapshot.exists)return recoverySnapshot.text;}
        File gate=new File(dir,"gate-status");UiStatusFiles.Snapshot gateSnapshot=UiStatusFiles.readForDisplay("battleloop",gate,targetKey);if(!gateSnapshot.known)return null;return gateSnapshot.exists?gateSnapshot.text:actual;
    }
    private String visualSource(String current){
        if(current!=null&&"true".equals(value(current,"processEnded")))return current;
        if(hasModeFields(current))return current;
        if(hasModeFields(actualStatusForVisuals))return actualStatusForVisuals;
        return current;
    }
    private static boolean hasModeFields(String raw){
        return raw!=null&&(value(raw,"skipBattleRequested")!=null
                ||value(raw,"hideBattleRequested")!=null
                ||value(raw,"visualState")!=null||value(raw,"hiddenBattles")!=null
                ||value(raw,"visualRounds")!=null);
    }
    private static String value(String s,String key){for(String line:s.split("\\r?\\n"))if(line.startsWith(key+"="))return line.substring(key.length()+1).trim();return null;}
    private static String display(String s,File dir){if("stopped".equals(value(s,"state"))&&com.xnas.wpe.suite.BattleConflictGate.settled(dir.getParentFile(),"battleloop"))return "状态：已停止";if(s.isEmpty())return com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(dir.getParentFile())?"状态：战斗跳过旧任务未确认；请使用‘只读核查旧任务’查看保护状态":"状态：未启动";String gateMessage=value(s,"message");if(gateMessage!=null&&!gateMessage.isEmpty())return "状态："+gateMessage;String state=value(s,"state");String result="状态：";switch(state==null?"unknown":state){case "stopped":if(!com.xnas.wpe.suite.BattleConflictGate.settled(dir.getParentFile(),"battleloop"))result+="停止状态未确认";else if("true".equals(value(s,"processEnded")))result+="旧进程已确认结束；未将此结果作为 Hook 或自动状态恢复证明";else if("false".equals(value(s,"skipBattleRequested")))result+="已停止，Hook、绑定与视觉清理已确认；本次未开启跳过，无需恢复自动状态";else result+="已停止，Hook、绑定与自动状态恢复均已确认";break;case "startable":result+="可以启动（上次核对未发现运行中的 worker）";break;case "checking":result+="正在只读核查旧任务";break;case "waiting_restart":result+="等待重启游戏后继续核查旧任务";break;case "verified":result+="旧进程核查已通过，但启动保护仍保留；清理状态未确认";break;case "unconfirmed":result+="旧任务未确认；战斗启动保护仍保留";break;case "starting":result+="已保留启动位，等待 worker";break;case "waiting_game":result+="等待游戏启动";break;case "waiting_role":result+="等待角色登录或重新绑定";break;case "waiting_battle":result+="等待战斗";break;case "waiting_config":result+="等待人物/宝宝当前自动技能设置";break;case "waiting_conflict":result+="与其他战斗辅助冲突，已暂停";break;case "conflict_wait":result+="战斗功能互斥，等待另一功能停止或清理确认";break;case "running":result+="运行中";break;case "stopping":result+="false".equals(value(s,"skipBattleRequested"))?"正在恢复画面并清理 Hooks；清理未确认前启动位保持锁定":"正在恢复自动状态并清理 Hooks；清理未确认前启动位保持锁定";break;case "error":result+="运行失败，请查看 worker 日志";break;default:result+="状态未确认";}String player=value(s,"characterSkillId"),pet=value(s,"petSkillId"),petId=value(s,"petId");if(player!=null)result+="\n人物技能 ID："+player;if(pet!=null&&!pet.equals("none"))result+="\n宝宝 ID："+(petId==null?"未确认":petId)+"，技能 ID："+pet;return result;}

    @Override public ModuleStatus getModuleStatus(){
        String raw;
        File dir=getFilesDir();
        String selected=TargetGame.getSelectedPackage(this);
        if(!TargetGame.supportsModule(TargetGame.BATTLE_LOOP,selected))return new ModuleStatus(BattleLoopService.isActive()?ModuleStatus.State.UNKNOWN:ModuleStatus.State.STOPPED,BattleLoopService.isActive()?"状态未确认":"不支持",selected,BattleLoopService.isActive()?"战斗跳过状态未确认。":"此功能仅支持 DHXY。","");
        raw=currentStatus(dir,selected);
        if(raw==null)return ModuleStatus.unknown(selected,"本地战斗循环状态读取中或暂不可用；运行状态未确认。");
        if(raw.isEmpty()){
            if(com.xnas.wpe.suite.BattleConflictGate.hasUnconfirmedLoopActivity(dir.getParentFile()))return new ModuleStatus(ModuleStatus.State.UNKNOWN,"旧任务未确认",selected,"存在未确认的战斗跳过旧记录；打开页面不会触发 Root 核查。","点击启动会进行一次只读核查；核查后仍需再次点击启动。");
            return new ModuleStatus(ModuleStatus.State.STOPPED,"未启动",selected,"没有战斗跳过运行记录；打开页面不会触发 Root 核查。","点击启动以运行；本功能仅支持 DHXY。");
        }
        String state=value(raw,"state"), message=value(raw,"message"); if(BattleLoopService.isCheckingOldTask())return new ModuleStatus(ModuleStatus.State.WAITING,"正在准备",selected,"正在只读核对旧任务进程身份。核查不会自动启动功能。","");
        if(message!=null&&!message.isEmpty()&&("conflict_wait".equals(state)||"stopping".equals(state)))return new ModuleStatus("stopping".equals(state)?ModuleStatus.State.STOPPING:ModuleStatus.State.WAITING,
                "stopping".equals(state)?"清理待确认":"启动受阻", "com.gdoo.dhxy", message+"；状态文件来自战斗互斥保护器，worker/游戏绑定未确认。", "等待上方指出的旧任务核查或清理结果；保护状态不代表游戏绑定状态。");
        ModuleStatus.State mapped;
        String label;
        if(state==null){mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";}
        else switch(state){
            case "stopped": if(com.xnas.wpe.suite.BattleConflictGate.settled(dir.getParentFile(),"battleloop")){mapped=ModuleStatus.State.STOPPED;label="已停止";}else{mapped=ModuleStatus.State.UNKNOWN;label="停止状态未确认";}break;
            case "starting": mapped=ModuleStatus.State.WAITING;label="等待 worker";break;
            case "waiting_game": mapped=ModuleStatus.State.WAITING;label="等待游戏";break;
            case "waiting_role": mapped=ModuleStatus.State.WAITING;label="等待角色";break;
            case "waiting_battle": mapped=ModuleStatus.State.WAITING;label="等待战斗";break;
            case "waiting_config": mapped=ModuleStatus.State.WAITING;label="等待技能设置";break;
            case "waiting_conflict": case "conflict_wait": mapped=ModuleStatus.State.WAITING;label="辅助冲突";break;
            case "paused": mapped=ModuleStatus.State.PAUSED;label="已暂停";break;
            case "running": mapped=ModuleStatus.State.RUNNING;label="循环运行中";break;
            case "stopping": mapped=ModuleStatus.State.STOPPING;label="待确认";break;
            case "startable": mapped=ModuleStatus.State.STOPPED;label="可以启动";break;case "checking": mapped=ModuleStatus.State.WAITING;label="正在准备";break;case "waiting_restart": mapped=ModuleStatus.State.WAITING;label="等待重启游戏";break;case "unconfirmed": mapped=ModuleStatus.State.UNKNOWN;label="旧任务未确认";break;case "error": mapped=ModuleStatus.State.ERROR;label="运行失败";break;
            default: mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";
        }
        if(BattleLoopService.isActive()&&mapped==ModuleStatus.State.STOPPED){mapped=ModuleStatus.State.UNKNOWN;label="状态未确认";}
        return new ModuleStatus(mapped,label,"com.gdoo.dhxy",display(raw,dir)+"；目标角色/战斗绑定状态以 worker 报告为准。",
                mapped==ModuleStatus.State.STOPPED?"点击开始以运行；本功能仅支持 DHXY。":"本功能仅支持 DHXY；循环状态不代表每个游戏帧均已确认。");
    }
}
