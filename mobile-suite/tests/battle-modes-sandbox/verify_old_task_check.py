"""Host-compile the actual MainActivity old-task methods against small UI/service stubs."""
import subprocess
import sys
import tempfile
from pathlib import Path

sys.dont_write_bytecode = True
from compiler_paths import java_tools

ROOT = Path(__file__).resolve().parents[2]
MAIN = ROOT / "app/src/main/java/com/xnas/wpe/battleloop/MainActivity.java"


def between(source: str, start: str, end: str) -> str:
    first = source.index(start)
    last = source.index(end, first)
    return source[first:last]


def main():
    JAVAC, JAVA = java_tools()
    source = MAIN.read_text(encoding="utf-8")
    controls = between(source, "    private boolean canEditFromSnapshot(", "    private void syncModeCheckboxesFromSettings(")
    request = between(source, "    private void requestOldTaskCheck(){", "    private void dispatchServiceAction(")
    harness = r'''import java.io.File;
public final class MainActivityOldTaskHarness {
 static boolean active, checking;
 static int dispatched;
 static String lastAction;
 static final class Button { boolean enabled; void setEnabled(boolean value){enabled=value;} }
 static final class Label { String text; void setText(String value){text=value;} }
 static final class TargetGame { static final String BATTLE_LOOP="battleloop"; static boolean supportsModule(String m,String p){return true;} static String getSelectedPackage(MainActivityOldTaskHarness a){return "test";} }
 static final class BattleLoopService { static final String CHECK="check_old_task"; static boolean isActive(){return active;} static boolean isCheckingOldTask(){return checking;} }
 Button skipBattleCheckbox=new Button(), hideBattleCheckbox=new Button(), checkButton=new Button();
 Label hideHint=new Label(), status=new Label();
 String lastDisplayedStatus="";
 boolean autoStartPending;
 static String value(String raw,String key){if(raw==null)return null;for(String line:raw.split("\\n")){int i=line.indexOf('=');if(i>0&&line.substring(0,i).equals(key))return line.substring(i+1);}return null;}
 private void dispatchServiceAction(String action){dispatched++;lastAction=action;}
'''+controls+request+r'''
 static void check(boolean ok,String text){if(!ok)throw new AssertionError(text);}
 public static void main(String[] args){
  MainActivityOldTaskHarness a=new MainActivityOldTaskHarness();
  active=true;a.lastDisplayedStatus="state=running\n";a.autoStartPending=true;a.requestOldTaskCheck();
  check(a.dispatched==0,"active request dispatched CHECK");
  check(!a.autoStartPending,"active request retained an auto-start request");
  check(a.status.text.contains("正在运行"),"active request did not report the running guard");
  a.refreshBattleModeControls("state=running\n");
  check(!a.checkButton.enabled,"CHECK button stayed enabled during active service");
  active=false;checking=true;com.xnas.wpe.battleskip.BattleSkipRecoveryService.checking=true;a.dispatched=0;a.requestOldTaskCheck();
  check(a.dispatched==0,"existing checking guard dispatched CHECK again");
  check(a.status.text.contains("正在只读核查"),"checking guard status changed");
  checking=false;com.xnas.wpe.battleskip.BattleSkipRecoveryService.checking=false;a.requestOldTaskCheck();
  check(a.dispatched==1&&BattleLoopService.CHECK.equals(a.lastAction),"inactive idle request did not dispatch CHECK");
  System.out.println("PASS MainActivity CHECK: active refusal/button disable, checking guard, and idle dispatch");
 }
}
'''
    recovery_stub = r'''package com.xnas.wpe.battleskip;
public final class BattleSkipRecoveryService { public static boolean checking; public static boolean isChecking(){return checking;} }
'''
    with tempfile.TemporaryDirectory(prefix="battle-modes-check-") as directory:
        root = Path(directory)
        source_file = root / "MainActivityOldTaskHarness.java"
        source_file.write_text(harness, encoding="utf-8")
        recovery = root / "com/xnas/wpe/battleskip/BattleSkipRecoveryService.java"
        recovery.parent.mkdir(parents=True)
        recovery.write_text(recovery_stub, encoding="utf-8")
        classes = root / "classes"
        classes.mkdir()
        subprocess.run([str(JAVAC), "-encoding", "UTF-8", "-d", str(classes), str(source_file), str(recovery)], check=True)
        subprocess.run([str(JAVA), "-cp", str(classes), "MainActivityOldTaskHarness"], check=True)


if __name__ == "__main__":
    main()
