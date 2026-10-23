import com.xnas.wpe.suite.NativeWorkerCommand;

public final class NativeWorkerCommandSelfTest {
    public static void main(String[] args) {
        String libraryDir = "/data/app/com.xnas.wpe suite/lib's/x86_64";
        String quotedDir = "'/data/app/com.xnas.wpe suite/lib'\\''s/x86_64'";
        String start = NativeWorkerCommand.withLibraryPath(libraryDir,
                NativeWorkerCommand.shellQuote(libraryDir + "/libanlei_worker.so") + " --dir '/data/user/0/app/files'");
        require(start.equals("export LD_LIBRARY_PATH=" + quotedDir
                + "; " + NativeWorkerCommand.shellQuote(libraryDir + "/libanlei_worker.so")
                + " --dir '/data/user/0/app/files'"),
                "start command must quote the exact trusted directory");

        String reconcile = NativeWorkerCommand.withLibraryPath(libraryDir,
                "'/data/app/worker.so' --reconcile --dir '/data/user/0/app/files'");
        require(reconcile.startsWith("export LD_LIBRARY_PATH=" + quotedDir + "; "),
                "recovery command must set the trusted library directory");
        require(!reconcile.contains("$LD_LIBRARY_PATH") && !reconcile.contains(":")
                && !reconcile.contains("/system/lib"), "must not inherit or append a polluted search path");

        String special = NativeWorkerCommand.shellQuote("/a path/it's here");
        require(special.equals("'/a path/it'\\''s here'"), "single quote shell escaping");
        expectInvalid(() -> NativeWorkerCommand.withLibraryPath("", "worker"));
        expectInvalid(() -> NativeWorkerCommand.withLibraryPath("relative/lib", "worker"));
        System.out.println("NativeWorkerCommandSelfTest passed");
    }

    private static void expectInvalid(Runnable action) {
        try { action.run(); throw new AssertionError("expected IllegalArgumentException"); }
        catch (IllegalArgumentException expected) { }
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
