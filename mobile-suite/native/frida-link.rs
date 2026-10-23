use std::path::PathBuf;

pub fn main() {
    let mode = std::env::var("SUITE_FRIDA_LINK_MODE").unwrap_or_else(|_| "static".to_owned());
    println!("cargo:rerun-if-env-changed=SUITE_FRIDA_LINK_MODE");
    println!("cargo:rerun-if-env-changed=FRIDA_CORE_DEVKIT");
    println!("cargo:rerun-if-env-changed=FRIDA_SHARED_LIBRARY_DIR");

    match mode.as_str() {
        "static" => {
            let root = std::env::var("FRIDA_CORE_DEVKIT")
                .expect("FRIDA_CORE_DEVKIT must point to Android x86_64 devkit");
            println!("cargo:rerun-if-changed={root}/libfrida-core.a");
            println!("cargo:rustc-link-search=native={root}");
            println!("cargo:rustc-link-lib=static=frida-core");
        }
        "shared" => {
            let root =
                PathBuf::from(std::env::var_os("FRIDA_SHARED_LIBRARY_DIR").expect(
                    "FRIDA_SHARED_LIBRARY_DIR is required when SUITE_FRIDA_LINK_MODE=shared",
                ));
            let library = root.join("libfrida-core.so");
            if !library.is_file() {
                panic!("shared Frida core is missing: {}", library.display());
            }
            println!("cargo:rerun-if-changed={}", library.display());
            println!("cargo:rustc-link-search=native={}", root.display());
            println!("cargo:rustc-link-lib=dylib=frida-core");
            println!("cargo:rustc-link-arg=-Wl,-rpath,$ORIGIN");
        }
        other => panic!("SUITE_FRIDA_LINK_MODE must be exactly shared or static, got {other:?}"),
    }

    for lib in ["log", "dl", "m"] {
        println!("cargo:rustc-link-lib={lib}");
    }
    println!("cargo:rustc-link-arg=-pthread");
    println!("cargo:rustc-link-arg=-Wl,--export-dynamic");
}
