"""Resolve locally installed Rust and Java compilers without fixed user paths."""
import os
import shutil
from pathlib import Path


def java_tools():
    configured = os.environ.get("JAVA_HOME")
    if configured:
        home = Path(configured)
        javac = home / "bin" / ("javac.exe" if os.name == "nt" else "javac")
        java = home / "bin" / ("java.exe" if os.name == "nt" else "java")
        if javac.is_file() and java.is_file():
            return javac, java

    javac_name = "javac.exe" if os.name == "nt" else "javac"
    java_name = "java.exe" if os.name == "nt" else "java"
    javac_path = shutil.which(javac_name)
    java_path = shutil.which(java_name)
    if javac_path and java_path:
        return Path(javac_path), Path(java_path)
    raise SystemExit(
        "Java compiler/runtime not found; set JAVA_HOME to a JDK or add javac and java to PATH"
    )


def rustc_tool():
    configured = os.environ.get("RUSTC")
    if configured:
        candidate = Path(configured)
        if candidate.is_file():
            return candidate
        resolved = shutil.which(configured)
        if resolved:
            return Path(resolved)
    resolved = shutil.which("rustc.exe" if os.name == "nt" else "rustc")
    if resolved:
        return Path(resolved)
    raise SystemExit("rustc not found; set RUSTC to the compiler path or add rustc to PATH")
