package com.xnas.wpe.bosshunt;

import android.system.ErrnoException;
import android.system.Os;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

/** Atomic, monotonically sequenced command-file access for the module service. */
final class BossHuntCommandFile {
    private BossHuntCommandFile() {
    }

    static BossHuntCommand read(File directory) throws IOException {
        File command = new File(directory, "command");
        if (!command.isFile()) return null;
        try (FileInputStream input = new FileInputStream(command);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            try {
                return BossHuntCommand.parse(new String(output.toByteArray(), StandardCharsets.UTF_8));
            } catch (IllegalArgumentException error) {
                throw new IOException("Existing bosshunt command is malformed", error);
            }
        }
    }

    static BossHuntCommand writeNext(File directory, BossHuntCommand.Mode mode,
                                    BossHuntSelection maps, int map) throws IOException {
        if (!directory.isDirectory() && !directory.mkdirs()) {
            throw new IOException("Cannot create bosshunt module directory");
        }
        BossHuntCommand previous = read(directory);
        long sequence;
        try {
            sequence = BossHuntCommand.nextSequence(previous == null ? 0L : previous.sequence);
        } catch (IllegalStateException error) {
            throw new IOException("Starhunt command sequence exhausted", error);
        }
        final BossHuntCommand next;
        try {
            next = new BossHuntCommand(sequence, mode, maps, map);
        } catch (IllegalArgumentException error) {
            throw new IOException("Invalid bosshunt command", error);
        }

        File temporary = new File(directory, "command.tmp");
        File destination = new File(directory, "command");
        byte[] contents = next.toFileContents().getBytes(StandardCharsets.UTF_8);
        try (FileOutputStream output = new FileOutputStream(temporary, false)) {
            output.write(contents);
            output.getFD().sync();
        }
        try {
            // POSIX rename is atomic because both files live in the module directory.
            Os.rename(temporary.getAbsolutePath(), destination.getAbsolutePath());
        } catch (ErrnoException error) {
            if (temporary.exists() && !temporary.delete()) {
                error.addSuppressed(new IOException("Could not remove temporary command file"));
            }
            throw new IOException("Could not atomically publish bosshunt command", error);
        }
        return next;
    }
}
