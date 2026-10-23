# 朋友版目标锁定构建（2026-10-19，免激活开放版）

## 朋友版规则

- **免激活开放版**：`PIAOMIAO_LICENSE_BYPASS=true`，不需要激活码；包内不含激活码、用户数据或授权服务配置。
- 只支持目标游戏 **com.gdoo.dhxy（DHXY）**；目标游戏在编译期锁定，系统设置不再显示目标选择。
- 不提供进程列表或手动进程选择；各功能启动时自动扫描并校验该游戏进程。
- 除目标锁定外，其他功能与作者版一致（自动找星、自动BOSS、定时任务预设摘要、停止记录自动恢复等）。
- 仅 x86_64、Android 6.0+，使用 Android Debug 签名。
- 普通/作者构建不传 `-PpiaomiaoLockTarget` 时行为不变，仍可在系统设置选择 DHXY/XXXY。

## 构建

```text
gradlew.bat --offline :app:assembleDebug
  -PpiaomiaoLicenseBypass=true
  -PpiaomiaoVersionCode=31
  -PpiaomiaoVersionName=2026.10.19.13-friends-open-v1
  -PpiaomiaoLockTarget=com.gdoo.dhxy
```

- 生成的 `BuildConfig`：`PIAOMIAO_LICENSE_BYPASS=true`、`PIAOMIAO_LOCKED_TARGET=com.gdoo.dhxy`、`VERSION_CODE=31`、`VERSION_NAME=2026.10.19.13-friends-open-v1`。
- `build-native.ps1 -LicenseBypass` 生效，9 个 Rust crate 以免激活模式重新编译。
- APK：`artifacts/friend-share-2026.10.19.13-open/飘渺助手朋友版开放版-2026.10.19.13.apk`，67,929,484 字节，SHA-256 `DF735E22E21C0537F464E7FF4DEE959964DD5211C0A44C2058BFAA93A6E56590`，v1/v2 签名通过，Debug 证书 SHA-256 `9f4085bc10118b0279dd3dcecceba16858e50c2691b525701891299534245826`。
- 分享 ZIP：同目录 `飘渺助手朋友版开放版-2026.10.19.13.zip`，67,283,481 字节，SHA-256 `3BB162978A3DD266C1A6AB75B6717A821CC3FEAB0A3E3DC23F4B37E76D2586E4`，含 APK、安装说明、APK SHA-256，不含激活码。

## 实现

- `app/build.gradle`：新增 `piaomiaoLockTarget` 参数（默认空），生成 `BuildConfig.PIAOMIAO_LOCKED_TARGET`。
- `TargetGame`：锁定构建下 `getSelectedPackage` 恒返回锁定包，`setSelectedPackage` 只接受锁定包，`isKnownPackage` 只认锁定包；模块自动扫描该包进程。
- `MainActivity` 系统设置：锁定构建显示静态行“DHXY（朋友版锁定，不可切换）”，不显示目标选择弹窗。
- 普通构建不传参数时 `isLocked()` 为 false，原 DHXY/XXXY 选择逻辑不变。
- 之前按“普通授权朋友版”构建的 code30 是中间产物，**不作为最终交付**；最终交付为本次 code31 免激活开放版。

## 验证

- `BuildConfig.java` 实测包含 `LICENSE_BYPASS=true`、`LOCKED_TARGET=com.gdoo.dhxy`、code31/name。
- APK 的 DEX 原始字符串包含 `com.gdoo.dhxy`、`PIAOMIAO_LOCKED_TARGET`、`朋友版锁定，不可切换`；`libstarhunt_worker.so` 包含当前中心落点标记。
- `apksigner` v1/v2 通过，证书与既有朋友版一致。
- 未在 emulator-5556 安装：执行时该设备已不在线（`device 'emulator-5556' not found`）；未在 emulator-5554 覆盖安装，保留用户当前 v29 本机免激活构建。因此朋友版 UI 和目标锁定的设备截图验收未完成。
- 未验证朋友设备安装、DHXY 进程绑定和真实游戏行为；未发送或发布。

## 边界

- 目标锁定是编译期实现；免激活开放版不需要授权服务。
- Debug 签名，仅适合朋友测试，不作为正式发布签名。
- 不包含任何用户数据、激活码或授权服务配置。
