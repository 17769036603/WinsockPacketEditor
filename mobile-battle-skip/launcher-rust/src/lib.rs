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
    let result = match (alias.as_deref(), data_path) {
        (Some("com.xnas.wpe.battleskip.Start"), Some(data)) => {
            let Some(library_dir) = native_library_dir(env, context) else {
                toast(env, context, "无法读取应用库目录，启动已取消");
                ANativeActivity_finish(activity);
                return;
            };
            let worker = format!("{library_dir}/libbattle_skip_worker.so");
            let command = format!(
                "{} --daemon --dir {}",
                shell_quote(&worker),
                shell_quote(&data)
            );
            let _ = fs::write(format!("{data}/root-command"), &command);
            let log = fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open(format!("{data}/root-request.log"));
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
                Err(error) => format!("启动失败：{error}"),
            }
        }
        (Some("com.xnas.wpe.battleskip.Stop"), Some(data)) => {
            let dir = std::path::Path::new(&data);
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
    let _ = fs::write(
        "/data/user/0/com.xnas.wpe.battleskip/files/launcher-status",
        format!("alias={alias:?}\n{result}\n"),
    );
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
    use super::{jni_fn, shell_quote, JNIEnv};
    use std::ffi::c_void;

    unsafe extern "system" fn sentinel() {}

    #[test]
    fn shell_quote_keeps_spaces_and_single_quotes_inside_one_argument() {
        assert_eq!(
            shell_quote("/data/app/a b/worker's.so"),
            "'/data/app/a b/worker'\"'\"'s.so'"
        );
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
}
