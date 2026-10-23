"""Host-compile the production mode settings logic and check its wired start gates."""
import subprocess
import sys
import tempfile
from pathlib import Path

sys.dont_write_bytecode = True
from compiler_paths import java_tools

ROOT = Path(__file__).resolve().parents[2]

STUBS = {
    "android/content/SharedPreferences.java": r'''package android.content;
import java.util.Map;
public interface SharedPreferences { Map<String, ?> getAll(); }
''',
    "android/content/Context.java": r'''package android.content;
import java.io.File;
public abstract class Context { public static final int MODE_PRIVATE=0; public abstract SharedPreferences getSharedPreferences(String name,int mode); public abstract File getFilesDir(); }
''',
    "android/system/ErrnoException.java": r'''package android.system;
public class ErrnoException extends Exception { public ErrnoException(String op,int errno){super(op);} }
''',
    "android/system/Os.java": r'''package android.system;
public final class Os { public static void chmod(String p,int m) throws ErrnoException {} public static void rename(String a,String b) throws ErrnoException {} }
''',
    "com/xnas/wpe/suite/TargetGame.java": r'''package com.xnas.wpe.suite;
import android.content.Context;
public final class TargetGame { public static final String BATTLE_LOOP="battleloop"; public static boolean supportsModule(String m,String p){return true;} public static String getSelectedPackage(Context c){return "test";} }
''',
    "com/xnas/wpe/suite/BattleConflictGate.java": r'''package com.xnas.wpe.suite;
import java.io.File;
public final class BattleConflictGate { public static boolean hasUnconfirmedLoopActivity(File f){return false;} }
''',
    "com/xnas/wpe/battleloop/BattleLoopService.java": r'''package com.xnas.wpe.battleloop;
public final class BattleLoopService { public static boolean isActive(){return false;} public static boolean isCheckingOldTask(){return false;} }
''',
    "com/xnas/wpe/battleloop/BattleVisualOptionsContract.java": r'''package com.xnas.wpe.battleloop;
import android.content.Context;
import java.io.File;
import java.util.Collections;
import java.util.Map;
public final class BattleVisualOptionsContract {
 static final class TestContext extends Context {
  private final Map<String,?> values;
  TestContext(Map<String,?> values){this.values=values;}
  public android.content.SharedPreferences getSharedPreferences(String n,int m){return () -> values;}
  public File getFilesDir(){return null;}
 }
 static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);}
 public static void main(String[] args){
  BattleVisualOptions.Snapshot defaults=BattleVisualOptions.readSettings(new TestContext(Collections.emptyMap()));
  check(defaults.skipBattle && !defaults.hideBattle && defaults.hasAnyModeEnabled(),"missing preference defaults changed");
  BattleVisualOptions.Snapshot off=BattleVisualOptions.readSettings(new TestContext(Map.of(BattleVisualOptions.SKIP_BATTLE_KEY,false,BattleVisualOptions.HIDE_BATTLE_KEY,false)));
  check(!off.skipBattle && !off.hideBattle && !off.hasAnyModeEnabled(),"both-off preferences did not close the start gate");
  BattleVisualOptions.Snapshot skip=BattleVisualOptions.readSettings(new TestContext(Map.of(BattleVisualOptions.SKIP_BATTLE_KEY,true,BattleVisualOptions.HIDE_BATTLE_KEY,false)));
  BattleVisualOptions.Snapshot hide=BattleVisualOptions.readSettings(new TestContext(Map.of(BattleVisualOptions.SKIP_BATTLE_KEY,false,BattleVisualOptions.HIDE_BATTLE_KEY,true)));
  check(skip.hasAnyModeEnabled() && hide.hasAnyModeEnabled(),"single-mode settings were rejected");
  BattleVisualOptions.Snapshot malformed=new BattleVisualOptions.Snapshot(false,false);
  check(!malformed.hasAnyModeEnabled(),"both-off snapshot unexpectedly enabled a mode");
  System.out.println("PASS Java settings: independent options and legacy skip default");
 }
}
''',
}


def main():
    JAVAC, JAVA = java_tools()
    with tempfile.TemporaryDirectory(prefix="battle-modes-java-") as directory:
        root = Path(directory)
        sources = []
        for rel, contents in STUBS.items():
            path = root / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(contents, encoding="utf-8")
            sources.append(str(path))
        production = ROOT / "app" / "src" / "main" / "java" / "com" / "xnas" / "wpe" / "battleloop" / "BattleVisualOptions.java"
        sources.append(str(production))
        classes = root / "classes"
        classes.mkdir()
        subprocess.run([str(JAVAC), "-encoding", "UTF-8", "-d", str(classes), *sources], check=True)
        subprocess.run([str(JAVA), "-cp", str(classes), "com.xnas.wpe.battleloop.BattleVisualOptionsContract"], check=True)

    service = (ROOT / "app/src/main/java/com/xnas/wpe/battleloop/BattleLoopService.java").read_text(encoding="utf-8")
    activity = (ROOT / "app/src/main/java/com/xnas/wpe/battleloop/MainActivity.java").read_text(encoding="utf-8")
    start = service.index("public synchronized int onStartCommand")
    end = service.index("@Override", start)
    start_body = service[start:end]
    gate = start_body.index("if (!requestedOptions.hasAnyModeEnabled())")
    refusal = start_body.index("return START_NOT_STICKY", gate)
    generation = start_body.index("generation++")
    worker_start = start_body.index("startWorker()")
    if not gate < refusal < generation < worker_start:
        raise SystemExit("FAIL Service gate: both-off check must return before generation or worker start")
    auto_gate = activity.index("if(!BattleVisualOptions.readSettings(MainActivity.this).hasAnyModeEnabled())")
    auto_start = activity.index("dispatchServiceAction(BattleLoopService.START)", auto_gate)
    if auto_gate >= auto_start:
        raise SystemExit("FAIL Activity gate: auto-start dispatch is not guarded")
    print("PASS Java start gates: service refuses both-off before worker launch; auto-start is gated")


if __name__ == "__main__":
    main()
