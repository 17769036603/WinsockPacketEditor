#[path = "../../../license_lease.rs"]
mod license_lease;

use std::ffi::{CStr, CString};
use std::fs;
use std::os::raw::{c_char, c_void};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::process::{Command, Stdio};

type JNIEnv = *const *const *const c_void;
type JObject = *mut c_void;
type JClass = JObject;
type JMethodId = *mut c_void;
type JFieldId = *mut c_void;
type JString = JObject;

const JNI_FIND_CLASS: usize = 6;
const JNI_DELETE_LOCAL_REF: usize = 23;
const JNI_GET_OBJECT_CLASS: usize = 31;
const JNI_GET_METHOD_ID: usize = 33;
const JNI_CALL_OBJECT_METHOD: usize = 34;
const JNI_GET_FIELD_ID: usize = 94;
const JNI_GET_OBJECT_FIELD: usize = 95;
const JNI_GET_STATIC_METHOD_ID: usize = 113;
const JNI_CALL_STATIC_OBJECT_METHOD: usize = 114;
const JNI_NEW_STRING_UTF: usize = 167;
const JNI_GET_STRING_UTF_CHARS: usize = 169;
const JNI_RELEASE_STRING_UTF_CHARS: usize = 170;
const JNI_EXCEPTION_CHECK: usize = 228;
const JNI_EXCEPTION_CLEAR: usize = 17;

#[repr(C)]
pub struct NativeActivity {
    callbacks: *mut c_void,
    vm: *mut c_void,
    env: JNIEnv,
    clazz: JObject,
    internal_data_path: *const c_char,
    external_data_path: *const c_char,
}

unsafe fn jni_fn<T: Copy>(env: JNIEnv, index: usize) -> T {
    let table = *env;
    std::mem::transmute_copy(&*table.add(index))
}

unsafe fn clear_exception(env: JNIEnv) {
    let check: unsafe extern "system" fn(JNIEnv) -> u8 = jni_fn(env, JNI_EXCEPTION_CHECK);
    if check(env) != 0 {
        let clear: unsafe extern "system" fn(JNIEnv) = jni_fn(env, JNI_EXCEPTION_CLEAR);
        clear(env);
    }
}

unsafe fn object_method(env: JNIEnv, object: JObject, name: &str, signature: &str) -> JObject {
    let class_fn: unsafe extern "system" fn(JNIEnv, JObject) -> JClass =
        jni_fn(env, JNI_GET_OBJECT_CLASS);
    let class = class_fn(env, object);
    if class.is_null() {
        return std::ptr::null_mut();
    }
    let method_fn: unsafe extern "system" fn(
        JNIEnv,
        JClass,
        *const c_char,
        *const c_char,
    ) -> JMethodId = jni_fn(env, JNI_GET_METHOD_ID);
    let name = CString::new(name).unwrap();
    let signature = CString::new(signature).unwrap();
    let method = method_fn(env, class, name.as_ptr(), signature.as_ptr());
    if method.is_null() {
        clear_exception(env);
        return std::ptr::null_mut();
    }
    let call: unsafe extern "system" fn(JNIEnv, JObject, JMethodId) -> JObject =
        jni_fn(env, JNI_CALL_OBJECT_METHOD);
    let result = call(env, object, method);
    let delete: unsafe extern "system" fn(JNIEnv, JObject) = jni_fn(env, JNI_DELETE_LOCAL_REF);
    delete(env, class);
    result
}

unsafe fn java_string(env: JNIEnv, value: JString) -> Option<String> {
    if value.is_null() {
        return None;
    }
    let get: unsafe extern "system" fn(JNIEnv, JString, *mut u8) -> *const c_char =
        jni_fn(env, JNI_GET_STRING_UTF_CHARS);
    let release: unsafe extern "system" fn(JNIEnv, JString, *const c_char) =
        jni_fn(env, JNI_RELEASE_STRING_UTF_CHARS);
    let chars = get(env, value, std::ptr::null_mut());
    if chars.is_null() {
        return None;
    }
    let result = CStr::from_ptr(chars).to_string_lossy().into_owned();
    release(env, value, chars);
    Some(result)
}

unsafe fn native_library_dir(env: JNIEnv, activity: JObject) -> Option<String> {
    let application_info = object_method(
        env,
        activity,
        "getApplicationInfo",
        "()Landroid/content/pm/ApplicationInfo;",
    );
    if application_info.is_null() {
        return None;
    }
    let class_fn: unsafe extern "system" fn(JNIEnv, JObject) -> JClass =
        jni_fn(env, JNI_GET_OBJECT_CLASS);
    let class = class_fn(env, application_info);
    let field_fn: unsafe extern "system" fn(
        JNIEnv,
        JClass,
        *const c_char,
        *const c_char,
    ) -> JFieldId = jni_fn(env, JNI_GET_FIELD_ID);
    let field_name = b"nativeLibraryDir\0";
    let field_sig = b"Ljava/lang/String;\0";
    let field = field_fn(
        env,
        class,
        field_name.as_ptr().cast(),
        field_sig.as_ptr().cast(),
    );
    if field.is_null() {
        clear_exception(env);
        return None;
    }
    let get: unsafe extern "system" fn(JNIEnv, JObject, JFieldId) -> JObject =
        jni_fn(env, JNI_GET_OBJECT_FIELD);
    let path = get(env, application_info, field);
    let result = java_string(env, path);
    let delete: unsafe extern "system" fn(JNIEnv, JObject) = jni_fn(env, JNI_DELETE_LOCAL_REF);
    if !path.is_null() {
        delete(env, path);
    }
    delete(env, class);
    delete(env, application_info);
    result
}

unsafe fn activity_alias(env: JNIEnv, activity: JObject) -> Option<String> {
    let intent = object_method(env, activity, "getIntent", "()Landroid/content/Intent;");
    if intent.is_null() {
        return None;
    }
    let component = object_method(
        env,
        intent,
        "getComponent",
        "()Landroid/content/ComponentName;",
    );
    let delete: unsafe extern "system" fn(JNIEnv, JObject) = jni_fn(env, JNI_DELETE_LOCAL_REF);
    delete(env, intent);
    if component.is_null() {
        return None;
    }
    let name = object_method(env, component, "getClassName", "()Ljava/lang/String;");
    let result = java_string(env, name);
    if !name.is_null() {
        delete(env, name);
    }
    delete(env, component);
    result
}

unsafe fn toast(env: JNIEnv, activity: JObject, message: &str) {
    let find: unsafe extern "system" fn(JNIEnv, *const c_char) -> JClass =
        jni_fn(env, JNI_FIND_CLASS);
    let toast_class = find(env, b"android/widget/Toast\0".as_ptr().cast());
    if toast_class.is_null() {
        clear_exception(env);
        return;
    }
    let method_fn: unsafe extern "system" fn(
        JNIEnv,
        JClass,
        *const c_char,
        *const c_char,
    ) -> JMethodId = jni_fn(env, JNI_GET_STATIC_METHOD_ID);
    let make = method_fn(
        env,
        toast_class,
        b"makeText\0".as_ptr().cast(),
        b"(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;\0"
            .as_ptr()
            .cast(),
    );
    if make.is_null() {
        clear_exception(env);
        return;
    }
    let new_string: unsafe extern "system" fn(JNIEnv, *const c_char) -> JString =
        jni_fn(env, JNI_NEW_STRING_UTF);
    let message = CString::new(message.replace('\0', " ")).unwrap();
    let text = new_string(env, message.as_ptr());
    let call: unsafe extern "system" fn(
        JNIEnv,
        JClass,
        JMethodId,
        JObject,
        JString,
        i32,
    ) -> JObject = jni_fn(env, JNI_CALL_STATIC_OBJECT_METHOD);
    let toast_object = call(env, toast_class, make, activity, text, 0);
    if !toast_object.is_null() {
        let get_class: unsafe extern "system" fn(JNIEnv, JObject) -> JClass =
            jni_fn(env, JNI_GET_OBJECT_CLASS);
        let toast_object_class = get_class(env, toast_object);
        let get_method: unsafe extern "system" fn(
            JNIEnv,
            JClass,
            *const c_char,
            *const c_char,
        ) -> JMethodId = jni_fn(env, JNI_GET_METHOD_ID);
        let show = get_method(
            env,
            toast_object_class,
            b"show\0".as_ptr().cast(),
            b"()V\0".as_ptr().cast(),
        );
        if !show.is_null() {
            let call_void: unsafe extern "system" fn(JNIEnv, JObject, JMethodId) = jni_fn(env, 61);
            call_void(env, toast_object, show);
        } else {
            clear_exception(env);
        }
        let delete: unsafe extern "system" fn(JNIEnv, JObject) = jni_fn(env, JNI_DELETE_LOCAL_REF);
        delete(env, toast_object_class);
        delete(env, toast_object);
    }
    let delete: unsafe extern "system" fn(JNIEnv, JObject) = jni_fn(env, JNI_DELETE_LOCAL_REF);
    if !text.is_null() {
        delete(env, text);
    }
    delete(env, toast_class);
}

fn shell_quote(value: &str) -> String {
    format!("'{}'", value.replace('\'', "'\"'\"'"))
}

fn root_worker_command(library_dir: &str, module_dir: &str) -> Option<String> {
    if !library_dir.starts_with('/') || library_dir.is_empty() {
        return None;
    }
    let worker = format!("{library_dir}/libbattle_skip_worker.so");
    let core = format!("{library_dir}/libfrida-core.so");
    if std::path::Path::new(&worker).parent() != std::path::Path::new(&core).parent() {
        return None;
    }
    Some(format!(
        "export LD_LIBRARY_PATH={}; {} --daemon --dir {}",
        shell_quote(library_dir),
        shell_quote(&worker),
        shell_quote(module_dir)
    ))
}

fn mark_start_not_launched(module_dir: &str, reason: &str) {
    let directory = std::path::Path::new(module_dir);
    let status = directory.join("status");
    if matches!(fs::read_to_string(&status).as_deref(), Ok("state=starting\n")) {
        let _ = fs::write(
            status,
            format!(
                "state=stopped\nhooksRemoved=true\nbindingCleared=true\nreason={reason}\n"
            ),
        );
    }
    let _ = fs::write(
        directory.join("launcher-status"),
        format!("state=not_launched\nreason={reason}\n"),
    );
}

fn start_gate_reserved(module_dir: &std::path::Path) -> bool {
    let Some(modules_dir) = module_dir.parent() else {
        return false;
    };
    matches!(
        fs::read_to_string(modules_dir.join(".battle-conflict.guard")).as_deref(),
        Ok("battleskip\n")
    ) && matches!(
        fs::read_to_string(module_dir.join("status")).as_deref(),
        Ok("state=starting\n")
    )
}

#[no_mangle]
pub unsafe extern "C" fn ANativeActivity_onCreate(
    activity: *mut NativeActivity,
    _saved_state: *mut c_void,
    _saved_state_size: usize,
) {
    if activity.is_null() {
        return;
    }
    let result = catch_unwind(AssertUnwindSafe(|| on_create(activity)));
    if result.is_err() {
        ANativeActivity_finish(activity);
    }
}

unsafe fn on_create(activity: *mut NativeActivity) {
    let env = (*activity).env;
    let context = (*activity).clazz;
    let data_path = if (*activity).internal_data_path.is_null() {
        None
    } else {
        Some(
            CStr::from_ptr((*activity).internal_data_path)
                .to_string_lossy()
                .into_owned(),
        )
    };
    let alias = activity_alias(env, context);
    let mut status_path = None;
    let mut launch_failure = None;
    let result = match (alias.as_deref(), data_path) {
        (Some("com.xnas.wpe.suite.BattleSkipStart"), Some(data))
            if data == "/data/user/0/com.xnas.wpe.suite/files"
                || data == "/data/data/com.xnas.wpe.suite/files" => {
            let module_dir = format!("{data}/modules/battleskip");
            status_path = Some(format!("{module_dir}/launcher-status"));
            if fs::create_dir_all(&module_dir).is_err() {
                mark_start_not_launched(&module_dir, "module_directory_unavailable");
                toast(env, context, "功能目录不可用，启动已取消");
                ANativeActivity_finish(activity);
                return;
            }
            if license_lease::require_suite_lease().is_err() {
                mark_start_not_launched(&module_dir, "activation_lease_invalid");
                toast(env, context, "授权无效或已过期，启动已取消");
                ANativeActivity_finish(activity);
                return;
            }
            if !start_gate_reserved(std::path::Path::new(&module_dir)) {
                mark_start_not_launched(&module_dir, "gate_not_reserved");
                toast(env, context, "启动位未预留，启动已取消");
                ANativeActivity_finish(activity);
                return;
            }
            let Some(library_dir) = native_library_dir(env, context) else {
                mark_start_not_launched(&module_dir, "native_library_directory_unavailable");
                toast(env, context, "无法读取应用库目录，启动已取消");
                ANativeActivity_finish(activity);
                return;
            };
            let Some(command) = root_worker_command(&library_dir, &module_dir) else {
                mark_start_not_launched(&module_dir, "native_library_directory_invalid");
                toast(env, context, "应用库目录无效，启动已取消");
                ANativeActivity_finish(activity);
                return;
            };
            let _ = fs::write(format!("{module_dir}/root-command"), &command);
            let log = fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open(format!("{module_dir}/root-request.log"));
            let mut request = Command::new("su");
            request.arg("-c").arg(command).stdin(Stdio::null());
            if let Ok(log) = log {
                if let Ok(stderr) = log.try_clone() {
                    request.stderr(Stdio::from(stderr));
                }
                request.stdout(Stdio::from(log));
            }
            match request.spawn() {
                Ok(_child) => "已发送启动请求；请按提示允许 Root".to_owned(),
                Err(error) => {
                    launch_failure = Some("su_process_not_started");
                    mark_start_not_launched(&module_dir, "su_process_not_started");
                    format!("启动失败：{error}")
                }
            }
        }
        (Some("com.xnas.wpe.suite.BattleSkipStop"), Some(data))
            if data == "/data/user/0/com.xnas.wpe.suite/files"
                || data == "/data/data/com.xnas.wpe.suite/files" => {
            let module_dir = format!("{data}/modules/battleskip");
            status_path = Some(format!("{module_dir}/launcher-status"));
            let dir = std::path::Path::new(&module_dir);
            let request = fs::OpenOptions::new()
                .write(true)
                .create(true)
                .open(dir.join("stop"));
            match request {
                Ok(_) => "已发送停止请求；worker 将处理该请求".to_owned(),
                Err(error) => format!("停止请求失败：{error}"),
            }
        }
        _ => "无法识别启动入口或应用路径".to_owned(),
    };
    if let Some(path) = status_path {
        let status = match launch_failure {
            Some(reason) => format!("state=not_launched\nreason={reason}\nalias={alias:?}\nresult={result}\n"),
            None => format!("alias={alias:?}\nresult={result}\n"),
        };
        let _ = fs::write(path, status);
    }
    toast(env, context, &result);
    ANativeActivity_finish(activity);
}

#[cfg(not(test))]
#[cfg_attr(target_os = "android", link(name = "android"))]
unsafe extern "C" {
    fn ANativeActivity_finish(activity: *mut NativeActivity);
}

#[cfg(test)]
#[allow(non_snake_case)]
unsafe fn ANativeActivity_finish(_activity: *mut NativeActivity) {}

#[cfg(test)]
mod tests {
    use super::{jni_fn, root_worker_command, shell_quote, start_gate_reserved, JNIEnv};
    use std::ffi::c_void;
    use std::fs;
    use std::path::PathBuf;
    use std::time::{SystemTime, UNIX_EPOCH};

    struct TempTree(PathBuf);

    impl Drop for TempTree {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    unsafe extern "system" fn sentinel() {}

    #[test]
    fn shell_quote_keeps_spaces_and_single_quotes_inside_one_argument() {
        assert_eq!(
            shell_quote("/data/app/a b/worker's.so"),
            "'/data/app/a b/worker'\"'\"'s.so'"
        );
    }

    #[test]
    fn root_worker_command_uses_only_the_shared_library_directory() {
        let command = root_worker_command("/data/app/suite lib's/lib/x86_64", "/data/user/0/app/files/modules/battleskip")
            .expect("absolute app native library directory");
        assert!(command.starts_with("export LD_LIBRARY_PATH='/data/app/suite lib'\"'\"'s/lib/x86_64'; "));
        assert!(command.contains("'/data/app/suite lib'\"'\"'s/lib/x86_64/libbattle_skip_worker.so'"));
        assert!(command.contains("'/data/user/0/app/files/modules/battleskip'"));
        assert!(!command.contains("$LD_LIBRARY_PATH"));
        assert!(root_worker_command("relative/lib", "/data/user/0/app/files").is_none());
    }

    #[test]
    fn jni_fn_reads_function_pointer_from_the_requested_table_slot() {
        let mut slots = [std::ptr::null::<c_void>(); 3];
        slots[2] = sentinel as *const () as *const c_void;
        let table = slots.as_ptr();
        let env: JNIEnv = &table;
        let function: unsafe extern "system" fn() = unsafe { jni_fn(env, 2) };

        assert_eq!(function as *const (), sentinel as *const ());
    }

    #[test]
    fn start_requires_the_battleskip_reservation_and_exact_starting_status() {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let tree = TempTree(std::env::temp_dir().join(format!(
            "battle-skip-launcher-test-{}-{unique}",
            std::process::id()
        )));
        let module = tree.0.join("modules/battleskip");
        fs::create_dir_all(&module).unwrap();
        let marker = tree.0.join("modules/.battle-conflict.guard");
        let status = module.join("status");
        fs::write(&marker, "battleskip\n").unwrap();
        fs::write(&status, "state=starting\n").unwrap();
        assert!(start_gate_reserved(&module));

        fs::write(&marker, "battleloop\n").unwrap();
        assert!(!start_gate_reserved(&module));

        fs::write(&marker, "battleskip\n").unwrap();
        fs::write(&status, "state=starting\nreason=stale\n").unwrap();
        assert!(!start_gate_reserved(&module));
    }
}
