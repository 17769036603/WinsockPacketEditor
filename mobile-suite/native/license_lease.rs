//! Short-lived suite activation lease shared by native workers.
use std::{fs, path::Path};

const MAX_LEASE_MS: u64 = 120_000;

/// 本机免激活构建的编译期开关。只有 `-PpiaomiaoLicenseBypass=true` 的 Gradle
/// 构建会通过 build-native.ps1 设置 `PIAOMIAO_LICENSE_BYPASS=1`；常规构建与
/// 朋友版构建不得设置该变量，否则原生 worker 不再校验授权租约。
const fn bypass_flag(value: Option<&str>) -> bool {
    match value {
        Some(value) => value.len() == 1 && value.as_bytes()[0] == b'1',
        None => false,
    }
}

const LICENSE_BYPASS: bool = bypass_flag(option_env!("PIAOMIAO_LICENSE_BYPASS"));
/// 免激活构建合成的租约期限。battle_skip 的 Lua 侧要求 deadline 落在
/// `(now, now+120s]`，这里留 10 秒给写入与读取之间的正常延迟。
const BYPASS_LEASE_MS: u64 = 110_000;

#[derive(Debug, PartialEq, Eq)]
pub struct Lease {
    pub version: u64,
    pub deadline_elapsed_ms: u64,
    pub boot_id: String,
}

fn json_string(input: &str, key: &str) -> Option<String> {
    let needle = format!("\"{key}\"");
    let mut values = input.match_indices(&needle);
    let (at, _) = values.next()?;
    if values.next().is_some() {
        return None;
    }
    let tail = input[at + needle.len()..].trim_start();
    let tail = tail.strip_prefix(':')?.trim_start();
    let tail = tail.strip_prefix('"')?;
    let end = tail.find('"')?;
    let value = &tail[..end];
    if value.is_empty() || value.contains('\\') {
        return None;
    }
    if !matches!(
        tail[end + 1..].trim_start().chars().next(),
        Some(',') | Some('}')
    ) {
        return None;
    }
    Some(value.to_owned())
}

fn json_u64(input: &str, key: &str) -> Option<u64> {
    let needle = format!("\"{key}\"");
    let mut values = input.match_indices(&needle);
    let (at, _) = values.next()?;
    if values.next().is_some() {
        return None;
    }
    let tail = input[at + needle.len()..]
        .trim_start()
        .strip_prefix(':')?
        .trim_start();
    let digits: String = tail.chars().take_while(|c| c.is_ascii_digit()).collect();
    if digits.is_empty() || (digits.len() > 1 && digits.starts_with('0')) {
        return None;
    }
    if !matches!(
        tail[digits.len()..].trim_start().chars().next(),
        Some(',') | Some('}')
    ) {
        return None;
    }
    digits.parse().ok()
}

impl Lease {
    pub fn parse(input: &str) -> Option<Self> {
        Some(Self {
            version: json_u64(input, "v")?,
            deadline_elapsed_ms: json_u64(input, "deadlineElapsedMs")?,
            boot_id: json_string(input, "bootId")?,
        })
    }

    pub fn permits(&self, now_ms: u64, boot_id: &str) -> bool {
        self.version == 1
            && self.boot_id == boot_id
            && self.deadline_elapsed_ms > now_ms
            && self.deadline_elapsed_ms <= now_ms.saturating_add(MAX_LEASE_MS)
    }

    pub fn game_mirror(&self) -> String {
        format!(
            "deadlineElapsedMs={}\nbootId={}\n",
            self.deadline_elapsed_ms, self.boot_id
        )
    }
}

pub fn boottime_ms() -> Option<u64> {
    let uptime = fs::read_to_string("/proc/uptime").ok()?;
    let seconds: f64 = uptime.split_whitespace().next()?.parse().ok()?;
    if !seconds.is_finite() || seconds < 0.0 || seconds * 1000.0 > u64::MAX as f64 {
        return None;
    }
    Some((seconds * 1000.0).floor() as u64)
}

pub fn current_boot_id() -> Option<String> {
    let id = fs::read_to_string("/proc/sys/kernel/random/boot_id").ok()?;
    let id = id.trim();
    if id.is_empty() || id.chars().any(char::is_whitespace) {
        return None;
    }
    Some(id.to_owned())
}

/// 免激活构建不读磁盘租约，直接返回一份滚动期限的合成租约。
/// 每次调用按当前时刻重新计算，battle_skip 的每秒镜像刷新因此始终落在允许窗口内。
fn bypass_lease() -> Option<Lease> {
    Some(Lease {
        version: 1,
        deadline_elapsed_ms: boottime_ms()?.checked_add(BYPASS_LEASE_MS)?,
        boot_id: current_boot_id()?,
    })
}

pub fn read_valid_file(path: &Path) -> Option<Lease> {
    if LICENSE_BYPASS {
        return bypass_lease();
    }
    let Some(now) = boottime_ms() else {
        return None;
    };
    let Some(boot_id) = current_boot_id() else {
        return None;
    };
    let text = fs::read_to_string(path).ok()?;
    let lease = Lease::parse(&text)?;
    lease.permits(now, &boot_id).then_some(lease)
}

pub fn valid_file(path: &Path) -> bool {
    read_valid_file(path).is_some()
}

pub fn valid_suite_lease() -> bool {
    [
        Path::new("/data/user/0/com.xnas.wpe.suite/files/license-lease.json"),
        Path::new("/data/data/com.xnas.wpe.suite/files/license-lease.json"),
    ]
    .iter()
    .any(|path| valid_file(path))
}

pub fn require_suite_lease() -> Result<(), String> {
    if valid_suite_lease() {
        Ok(())
    } else {
        Err("activation lease missing, expired, invalid, or from another boot".into())
    }
}

pub fn suite_lease_from_module_dir(module_dir: &Path) -> Option<std::path::PathBuf> {
    let files = module_dir.parent()?.parent()?;
    Some(files.join("license-lease.json"))
}

pub fn require_module_lease(module_dir: &Path) -> Result<(), String> {
    let path = suite_lease_from_module_dir(module_dir).ok_or("invalid suite module directory")?;
    if valid_file(&path) {
        Ok(())
    } else {
        Err("activation lease missing, expired, invalid, or from another boot".into())
    }
}

pub fn read_valid_module_lease(module_dir: &Path) -> Option<Lease> {
    read_valid_file(&suite_lease_from_module_dir(module_dir)?)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn validates_same_boot_expiry_and_upper_bound() {
        let lease =
            Lease::parse(r#"{"v":1,"deadlineElapsedMs":112000,"bootId":"boot-a"}"#).unwrap();
        assert!(lease.permits(100_000, "boot-a"));
        assert!(!lease.permits(112_000, "boot-a"));
        assert!(!lease.permits(100_000, "boot-b"));
        assert!(!Lease {
            deadline_elapsed_ms: 220_001,
            ..lease
        }
        .permits(100_000, "boot-a"));
    }

    #[test]
    fn rejects_bad_fields_and_versions() {
        assert!(Lease::parse(r#"{"v":1,"deadlineElapsedMs":1.2,"bootId":"x"}"#).is_none());
        assert!(
            !Lease::parse(r#"{"v":1,"deadlineElapsedMs":0,"bootId":"x"}"#)
                .unwrap()
                .permits(1, "x")
        );
        assert!(!Lease {
            version: 2,
            deadline_elapsed_ms: 10,
            boot_id: "x".into()
        }
        .permits(1, "x"));
    }

    #[test]
    fn mirror_preserves_the_authorized_deadline_and_boot_identity() {
        let lease = Lease::parse(r#"{"v":1,"deadlineElapsedMs":1080,"bootId":"boot-a"}"#).unwrap();
        assert_eq!(
            lease.game_mirror(),
            "deadlineElapsedMs=1080\nbootId=boot-a\n"
        );
    }
}
