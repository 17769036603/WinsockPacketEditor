fn main() {
    let root = std::env::var("FRIDA_CORE_DEVKIT")
        .expect("FRIDA_CORE_DEVKIT must point to Android x86_64 devkit");
    println!("cargo:rustc-link-search=native={root}");
    println!("cargo:rustc-link-lib=static=frida-core");
    for lib in ["log", "dl", "m"] {
        println!("cargo:rustc-link-lib={lib}");
    }
    println!("cargo:rustc-link-arg=-pthread");
    println!("cargo:rustc-link-arg=-Wl,--export-dynamic");
    println!("cargo:rerun-if-env-changed=FRIDA_CORE_DEVKIT");
}
