package com.xnas.wpe.suite;

/** Builds the trusted library search-path prefix used by Root-launched native workers. */
public final class NativeWorkerCommand {
    private NativeWorkerCommand() { }

    public static String withLibraryPath(String nativeLibraryDir, String command) {
        if (nativeLibraryDir == null || nativeLibraryDir.isEmpty()) {
            throw new IllegalArgumentException("nativeLibraryDir is required");
        }
        if (!nativeLibraryDir.startsWith("/")) {
            throw new IllegalArgumentException("nativeLibraryDir must be absolute");
        }
        if (command == null || command.isEmpty()) {
            throw new IllegalArgumentException("command is required");
        }
        return "export LD_LIBRARY_PATH=" + shellQuote(nativeLibraryDir) + "; " + command;
    }

    public static String shellQuote(String value) {
        if (value == null) throw new IllegalArgumentException("shell value is required");
        return "'" + value.replace("'", "'\\''") + "'";
    }
}
