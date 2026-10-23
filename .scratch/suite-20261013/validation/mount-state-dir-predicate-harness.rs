use std::path::Path;
fn valid_state_dir(path: &Path) -> bool {
    matches!(
        path.to_str(),
        Some("/data/user/0/com.xnas.wpe.suite/files/modules/mounthold")
            | Some("/data/data/com.xnas.wpe.suite/files/modules/mounthold")
    )
}

fn main() {
 let accepted = [
  "/data/user/0/com.xnas.wpe.suite/files/modules/mounthold",
  "/data/data/com.xnas.wpe.suite/files/modules/mounthold",
 ];
 let rejected = [
  "/data/user/0/com.xnas.wpe.suite/files/modules/mounthold/child",
  "/data/user/0/com.xnas.wpe.suite/files/modules/mounthold/../battleloop",
  "/data/user/0/com.xnas.wpe.suite/files",
  "/data/user/0/com.xnas.wpe.battleskip/files/modules/mounthold",
  "relative/modules/mounthold",
 ];
 for path in accepted { assert!(valid_state_dir(Path::new(path)), "should accept {path}"); }
 for path in rejected { assert!(!valid_state_dir(Path::new(path)), "should reject {path}"); }
 println!("MountStateDirPredicateHarness: PASS (2 accepted, 5 rejected; extracted exact production function)");
}