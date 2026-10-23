# 网站授权接入验收（2026-10-16）

## 当前交付状态

网站及code9 APK已发布，公开页面显示2026.10.16.3-online，公网下载66,149,289字节及SHA-256均与本机一致。所有者登录、正式D1发码、APK有效激活、重启自动验证、重复/跨安装、续期、解绑及停用已实测；空闲服务残留修复通过JVM、CodeRabbit及设备激活/停用复验。用户手动打开Wi-Fi后，经雷电官方ld.exe独立命令通道确认Android联网VALIDATED，移动数据恢复为1。断网120秒及真实游戏安全停止仍未完成，不能把发布成功说成所有运行场景验收完成。未获取或保存登录密码，未绕过登录或增加机器管理后门。

## 网站源码与发布

- 源码独立checkout：`F:/项目目录/封包监听工具/sites-piaomiao-library`，原站点提交 `e5528553faa4f45a9e84c850989b385e6c17fd4e`；本次已推送提交 `c01897434c55415e0e4b796e7ade3dddac2d7618`。
- 站点ID `appgprj_6ab6a43244dc8191ba00ac8e6dff02f0`，所有者与public访问策略保留。原公开项目清单为0条，部署后仍0条。
- 版本4：`appgprj_6ab6a43244dc8191ba00ac8e6dff02f0~appgver_7c4ec360ebb08191b9e0cd2f8fb01785`。
- 部署 `appgdep_6ad111bdcf14819187c1766962231d92` 原生工具返回succeeded，环境revision6。
- 公开页 `https://my-project-library.yinzhiping.chatgpt.site/piaomiao`；管理页 `/piaomiao/manage`；客户端 `/v1/activate` 与 `/v1/verify`。
- 新增四个独立D1表与索引，不修改projects表；schema-only迁移 `drizzle/0001_dashing_excalibur.sql`。发码计数按业务请求初始化。
- 授权管理依赖Sites可信登录身份与运行时所有者配置，写请求同时要求Origin同源。密钥通过Sites运行时配置，没有写入源码/部署包/APK，既有ADMIN_PASSWORD保持。
- APK发布使用R2独立对象、校验SHA-256成功后更新版本清单，保留历史文件。当前线上尚未上传APK，元数据接口404“APK正在准备中”，下载流程不可称已完成。

## 本地与静态验证

- Node24真实SQLite D1适配测试20/20通过：并发同安装幂等、跨安装唯一绑定、8会话、期限、续期、解绑、停用、哈希存储、容量和持久限流、body上限、owner和Origin、上传发布失败边界。
- 20,000限流键满表时旧键能继续到120请求，新键被拒；500码以3条D1批次SQL一次生成，100,000容量不半写入。
- TypeScript检查及最终网站构建通过。首次开发预览ECONNRESET，改用构建后本地Workerd预览完成页面观察。
- 本地Workerd实际D1绑定：生成201，同安装两次并发激活均200，验证200/120秒，停用200后验证401。
- 本地Workerd R2：真实66,149,067字节APK上传201，下载后SHA256与输入相同。这是本地模拟平台绑定，不等于Cloudflare线上D1/R2实测。
- CodeRabbit基线首次未识别main，显式指定后连接失败WebSocket closed；恢复后首审25文件完成，2 issues(1major限流满表、1minor续期提示)，均修复并增加测试。一次复审26文件完成，0 issues。
- 网站计划判断API实际调用HTTP429，not_run，不计通过；本阶段结果判断实际调用HTTP429，not_run，不计通过，主代理依现有证据保持未全部完成。
- Sites源工作流临时凭证到期后刷新。Windows GNU tar把F:当远程路径的打包失败，通过Git Bash与TAR_OPTIONS=--force-local重试后成功；未改插件。部署包核对没有.dev.vars、数据库、预览状态或凭证。

## 线上及设备已确认

本节下方code8及登录等待记录是较早阶段快照；以文末code9及当前交付状态为准。

- 线上匿名管理GET403，伪造身份头GET403，匿名APK上传PUT403，无效码激活POST401；项目列表200且保留0条。
- 所有者管理页正确进入ChatGPT登录，但现有会话需要密码，等待用户登录。尚未用所有者真实身份完成线上发码或上传。
- APK：`artifacts/PiaomiaoHelper-2026.10.16.2-online-candidate.apk`，66,149,067 bytes，SHA256 `F4A3C6A869788ABB0CFE7311FE865B7CFBE4F39F8D3B402FE8D2D3DA24AD9018`。
- code8 / `2026.10.16.2-online`，x86_64，min23/target35，9个native，Debug v1/v2签名通过。新旧签名证书相同；classes5.dex和生成BuildConfig确认实际HTTPS授权基址。
- emulator-5554原code5安装包已备份到 `build/online-acceptance/installed-code5-before-online.apk`，SHA256 `31139DE52A4E2C0AEB64F2A74A2348B9624261FB0D453D359399DD8165470FFA`；使用install -r成功更新到code8，保留应用数据。
- 冷启动MainActivity Status ok，屏幕确认版本、未激活、六入口保留；服务列表nothing，进程表仅suite宿主，没有列出的功能worker。没有Root、游戏附加或游戏操作。
- APK授权页安装识别码可见；输入无效测试码后屏幕显示“激活码无效或已使用（401），现有授权保持不变”，证明实际APK HTTPS请求与线上错误响应已走通。
- 证据截图：`build/online-acceptance/device-cold.png`、`device-license.png`、`device-invalid.png`。uiautomator因未达idle失败，未把XML缺失当成通过，使用实际截图验收。

## 剩余验收

1. 设备断网租约耗尽仍未验收；不重复关闭模拟器Wi-Fi，后续须选不影响调试通道的测试方式。网络恢复、code9有效激活和空闲停用后服务收尾已确认，真实游戏清理另须指定Root/游戏操作范围。
2. 阶段判断API已返回not_run；发布、授权及空闲收尾证据分别保留，未将其算成真实游戏完整验收。

## 2026-10-16 所有者登录后的实际进展

- 所有者成功登录，管理页列表加载0条；通过正式管理UI生成两枚1天专用测试码，不保存明文到证据或源码。
- 正式HTTP：激活200、重复激活200且expiresAt不变、跨安装409、验证200/120秒。续期1天后expiresAt精确增加86,400,000毫秒；解绑旧token401，新安装重绑200且期限仍保持；停用后verify/activate均401。
- 当前设备识别码与后台绑定一致，有效码激活后屏幕显示2026-10-17 08:36到期；force-stop后冷启动Status ok并自动恢复授权，未启动功能服务。六个子页均实屏打开，分别保留截图，打开页面时服务0、功能worker0。
- 撤销设备测试码后，APK自动回到尚未激活，已删除lease。实测两轮STOP(来自LicenseManager及SuiteApplication)唤起MountHold/Connection服务，45秒仍残留，虽然无功能worker。MountHold目录仅有过往expired journal，没有当前mount_hold.journal；为避免继续保留冷停止服务，已force-stop套件宿主，未操作游戏。此问题将修复后重新打包，不把code8直接当最终版本。
- `build/online-acceptance/production-api-results.json` 记录无凭证状态码及期限对照。截图device-valid.png含专用测试码，测试码已停用，不作为可发放码；公开证据优先device-restart.png与device-*-page.png。
- 文件选择器setFiles被Chrome扩展缺少Allow access to file URLs拒绝；已请求用户按官方说明开启，没有通过其他控制技术绕过。尚未向生产R2上传APK。
- 主机Node fetch曾出现TypeError，改用curl经隐藏stdin发送测试凭证后HTTP验证通过，未关闭TLS校验，没有把此传输失败说成服务端错误。

## code9空闲停止修复及受阻的设备复验

- LicenseManager在锁内判断此前租约，SuiteApplication移除重复STOP波次；MountHoldService仅在确认无worker、无当前journal且无未解决恢复时结束空服务，journal存在或目录状态未知仍走原安全恢复。ConnectionService按startId及当前连接身份结束空闲或已确认退出的service，迟到回调不能停止新连接，停止后不重建前台通知。
- 两个真实源码JVM harness及service gate通过，覆盖首次冷启动、临时网络失败、401/403、并发租约清理、过往journal与当前journal、目录未知、活动worker、连接退出未确认与新连接保护。CodeRabbit首审8文件的两项测试脚本问题已修复，正确同步快照后复审8文件0issues；一次准备失败的旧快照审查中断，不计有效审查。
- 计划判断实际API返回HTTP200但LOW_CONFIDENCE，状态not_run，不计通过。阶段结果判断实际API返回HTTP429，not_run，不计通过。
- 当前候选及拟发布文件：`artifacts/PiaomiaoHelper-2026.10.16.3-stop-fix-candidate.apk`、`artifacts/PiaomiaoHelper-2026.10.16.3.apk`，两者66,149,289 bytes，SHA256 `1A081015A590E34162968FCB0FAB8CA779919ED7DA483AE641C92CC62A4F4583`。code9 / `2026.10.16.3-online`，Debug v1/v2签名、x86_64/min23/target35通过，9个native内容与code8逐项相同。未覆盖旧交付文件。
- emulator-5554 install-r成功，冷启动成功，第三枚专用测试码激活成功并显示次日到期。代码与编译验证不能代替空闲停止修复的设备验收。
- 获得用户短暂断网授权后执行测试，已发出关闭移动数据及Wi-Fi命令；首张screencap卡住，此后的shell echo、settings与恢复svc命令均未成功返回。ADB reconnect及仅重启主机ADB服务后设备仍offline；没有重启模拟器、没有操作游戏或批准Root。测试脚本未生成有效结果JSON或断网截图，断网验收失败，网络恢复尚未确认。此故障不能归因为APK，也不能宣称恢复成功；已请求用户手动恢复网络，或授权必要的模拟器重启。
- Chrome扩展文件URL权限未开启，代理访问chrome://extensions被平台明确拒绝，没有使用CDP、系统文件修改或其它控制技术绕过。发布表单版本已准备为2026.10.16.3-online，等待用户手动选择本机APK。
- 三枚专用测试码在正式管理页均显示已停用，刷新页面后没有本次明文码。失败的临时断网脚本已移除，避免再次运行无单命令超时保护的测试；未将其失败结果算成通过。

## 获准后的模拟器恢复（2026-10-16 09:07起）

- 用户明确回复允许必要时重启模拟器。实时list2确认仅第0台运行，原进程dnplayer=10472、VM=16500；调用已安装官方CLI的reboot --index 0返回0，随后新进程dnplayer=76316、VM=2228，第1台仍未启动。没有修改或重建实例、没有清除数据。
- 重启后list2运行标志为1，但ADB仍offline，getprop sys.boot_completed也返回device offline，因此不能说Android启动、网络或应用恢复已验收。官方文档的action --index 0 --key call.network --value connect也已尝试，仍未取得恢复连接的证据。主机配置networkEnable=true不等于Android内Wi-Fi已开启。
- 已请求用户在模拟器快捷设置手动开启Wi-Fi，并反馈界面是否能操作。网络恢复、授权状态和设备复验仍待确认；没有继续断网、启动游戏或执行Root功能。

## 用户打开Wi-Fi后的恢复及code9停用复验（09:20—09:25）

- 用户确认Wi-Fi已打开且界面能操作。ADB重连及主机服务重启仍offline，不能把此状态等同Android断网。依据雷电官方文档 https://vnm.ldplayer.net/blog/1183.html 使用已安装ld.exe -s 0的独立命令通道，getprop sys.boot_completed返回1。
- wifi_on读回1，mobile_data最初0，使用svc data enable恢复后读回1；dumpsys connectivity确认默认网络100为WIFI CONNECTED且INTERNET/VALIDATED。随后APK真实HTTPS激活成功，进一步确认授权网站可达；没有再次关闭网络。
- ld.exe通道默认id返回uid=0、context=ldinit，未调用su或提升Root权限；只执行系统状态、网络恢复和本应用UI/私有文件检查，没有附加、读取或改写游戏。重启后观察到游戏PID2061，与重启前PID不同，不能复用旧PID身份。
- code9安装版本、六页和无授权状态实屏确认；生成第四枚1天专用测试码，通过应用授权页激活，显示2026-10-17 09:23到期。后台停用后应用自动显示尚未激活，license-lease.json消失，dumpsys activity services为nothing，仅套件宿主1898，无功能worker。MountHold目录只有既有expired journal，没有当前mount_hold.journal，修复后的空闲服务不再残留。此项覆盖空闲收尾，不代表活动游戏worker的恢复验收。
- 截图device-network-recovered.png、device-v3-recovered-valid.png、device-v3-revoked.png保存在build/online-acceptance。valid截图含已停用的专用码，不作发放码；最终可见证据使用无明文的revoked截图。临时设备截图文件已删除。

## 正式APK发布与公网下载核对

- 用户手动启用ChatGPT扩展文件URL权限后，原控制标签连接超时；重新打开同一Chrome中的管理页成功，无需再次登录。通过正式文件选择器选入PiaomiaoHelper-2026.10.16.3.apk，版本2026.10.16.3-online，点击发布后管理页明确显示已发布及SHA-256。
- 公开页面/piaomiao显示最新版本2026.10.16.3-online、63.1 MB和下载APK入口。使用不携带浏览器会话的curl从/piaomiao/download实际下载，长度66,149,289字节，SHA256为1A081015A590E34162968FCB0FAB8CA779919ED7DA483AE641C92CC62A4F4583，与本机原件一致。
- 无凭证验证记录保存在build/online-acceptance/production-release-results.json；核对后删除临时下载副本，保留原始交付APK。未更改网站源版本、既有项目记录或授权表数据。
