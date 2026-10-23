package com.xnas.wpe.suite;

/** Implemented by embedded module pages that can report a current status snapshot. */
public interface ModuleStatusProvider {
    ModuleStatus getModuleStatus();
}
