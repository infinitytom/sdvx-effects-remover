# SDVX Effects Remover

Windows 图形工具：通过可恢复的 IFS LayeredFS 覆盖，减少指定 SDVX 版本的背景和装饰光效，同时保留按键命中与基本读谱信息。

### v0.4.2 · 真实资源路径兼容与机台装饰精简（覆盖更新）

**0.4.0 和 0.4.1 均收到实机背景未变深黑的反馈。** 0.4.1 视频中仍可见动态舞台、绿色顶框和底部机台；日志虽然列出了覆盖目录，却还出现属性读取警告，不能据此认定资源已成功替换。

0.4.2 除原可读名称外，同时按原 IFS 内的真实哈希文件名生成几何和 AFP 覆盖，绕过对 Hook 名称索引解析的依赖。已逐项核对 22683 条真实路径，保留 0.4.1 的底色修复。此外新增 71 个独立装饰几何，隐藏顶部外框和底部机台外壳，随「隐藏动态装饰」共同控制，保留判定线、命中和读谱信息。

同一预览发布页与 ZIP 下载链接用于覆盖更新。重新下载后确认窗口标题为 **0.4.2**，退出游戏和启动器，读取旧模式，再点击「应用当前选择」。同一选择下也会补齐旧版缺少的哈希路径和机台覆盖；被手工修改的文件会阻止升级。

**这仍是需要实机复测的兼容修复。** 路径、生成和恢复验证不能代替运行时加载或最终画面的确认。

### 0.4.1 的底色修复记录

**上一版 v0.4.0 已收到用户实测反馈：背景没有变成深黑。** 原来的纹理生成测试只能证明覆盖文件有效，不能证明最终画面。检查资源发现，背景动画还存在独立的 AFP 底色，例如 dome 的普通/通关背景分别带有灰色底色；上一版没有处理它们。

0.4.1 补充 300 个已启用底色的背景 AFP 覆盖，仅将 RGB 改为黑色，保留原透明度、标志、时间线和 BSI。不会给原本透明的粒子动画添加不透明背景。**后续实机反馈确认，这项处理单独不足以解决背景问题。**

已经启用旧版「USC 深黑」的用户：退出游戏和启动器，打开修复版读取模式，再点击「应用当前选择」。修复版会识别缺少的动画底色覆盖，即使选择未变化也会补齐，不再直接提示“模式没有变化”。

参照 USC 视频的深黑背景与清晰读谱反馈，新增「USC 深黑」预设：隐藏舞台、减少旋钮尘粒、轨道泛光及动态装饰，同时保留判定文字、EARLY/LATE 和连击信息。新界面采用深黑底色、青色开关与轨道示意图，完整资源信息收进「详细信息」。预设只改变待应用选择，点击「应用当前选择」并确认后才写入游戏。

「深黑背景」将已识别 `graphics/game_bg` 中的 4302 张纹理替换为同尺寸、不透明的 RGB `0,0,0` PNG，结合原有背景绘制精简，减少残留背景色块。纯黑纹理由程序生成，不包含原游戏图片；该组可单独停用。五组既有几何规则与 625 个命中/长按/旋钮保护条目保持不变，沿用 v2 备份目录。恢复原版也会停用新增的深黑纹理组。

工具右侧为示意图，不是游戏实拍。SDVX 的 HUD 布局、镜头、Live2D 和脚本行为仍沿用原版；此更新尚未实机进歌验证，无法保证所有场景都完全纯黑或与 USC 相同。

**实验项目，修正版尚未完成实际歌曲验证。** 不保证提高帧率或消除所有演出。请先确认原版游戏稳定，再逐组试用。

## 下载与使用

在 [Releases](https://github.com/infinitytom/sdvx-effects-remover/releases) 下载 Windows x64 预览版，需要安装 .NET 8 Windows Desktop Runtime。

完整步骤、预设区别、旧版升级、备份恢复和常见问题见 **[v0.4 使用教程](docs/使用教程.md)**。发布 ZIP 中也附带相同教程。

1. 退出游戏和 LAZY 启动器，打开程序。
2. 点击“浏览”，选择含 `contents` 的 LAZY_KFC 根目录，再读取模式。程序会在本机用户配置目录记住所选路径。
3. 点击“USC 深黑”或自行调整开关，点击“应用当前选择”，预览后确认，下次启动游戏生效。
4. 点击“恢复原版”可停用本版全部覆盖。“仅留命中”会选择全部六组，包括隐藏判定显示；“USC 深黑”会保留判定显示。

仅支持已核对的 DLL 标识 `KFC-6aa256e6_7ad708` 和匹配的原始资源。启用前核对原资源哈希；不匹配或与其他模组覆盖同一资源容器时停止操作。项目不提供游戏、曲库、原始纹理、DLL 或账号数据。

## 可选精简内容

| 分组 | 修改范围 |
| --- | --- |
| 背景 | 已识别的舞台、人物背景、背景粒子几何绘制 |
| 深黑背景 | 4302 张纯黑背景纹理，以及 300 个背景 AFP 已有底色的 RGB 修复 |
| 旋钮尘粒 | 指定旋钮尘粒动画 |
| 轨道反光 | 指定轨道反光与 HUD 泛光 |
| 额外装饰 | 旋钮周边光效、连击光束、HUD 动态装饰，以及顶部外框和底部机台外壳 |
| 判定显示 | 可识别的 CRITICAL/ERROR、EARLY/LATE、CHAIN 字样及通关、失败、PUC/UC 演出 |

保留按键命中闪光、长按反馈、按键光柱、音符、旋钮定位、血条、分数和连击数字。隐藏判定显示不改变实际判定逻辑、成绩或通关结果，可独立恢复。镜头倾斜、旋转、谱面演出指令、Live2D 和动画脚本计算不在本版处理范围。

“启动设置”页另外提供日志级别、副屏强制刷新、Spice 叠加界面和配套的游戏/音符 60/120 FPS 设置。它们与特效分组分别应用。

## 旧版崩溃与本版修正

早期实现把 GE2D 的绘制记录数改成零。实际游戏日志出现 `afpu-render.c:1147: afpu_render_draw_geo` 断言；对对应本地渲染器的检查确认，该分支要求记录数为 **1**。因此旧方法不能继续使用，原先的静态格式测试不足以验证引擎兼容性。

当前实现保留文件头、绘制记录、计数、索引与其他数据，仅把选定几何的二维顶点重合到原始第一个顶点。测试覆盖本地引擎的计数约束，以及 22312 个规则条目的非顶点数据保持不变。**这修正了已定位的结构问题，仍不能代替实际游玩稳定性测试。**

旧版覆盖需先用“停用旧版覆盖”移出 `data_mods`。本版使用版本 2 标记及独立备份目录，不复用旧版清零文件，也不会自动开启新方案。

## 备份与恢复

原始游戏 IFS、音频、谱面和 DLL 不会被改写。六个开关管理的覆盖放在 `contents/data_mods/zzzz_sdvx_smooth_*`；深黑开关共同控制 `black` 纹理组和 `backdrop` 底色组，装饰开关共同控制 `extra` 与 `frame` 机台外框组。几何和 AFP 同时保存可读名称及真实哈希路径副本。停用后移至游戏目录 `.sdvx-smooth/effects-v2/disabled`，历史记录位于同目录的 `history`。旧版备份保留在 `effects-v1`，请勿手动复制回游戏。

启动设置修改前备份至 `.sdvx-smooth/backups`，支持完整恢复；若同一配置后来被其他程序修改，工具拒绝直接覆盖。更新游戏或遇到异常时先恢复原版，保留本地日志供排查。日志可能包含个人信息，请先脱敏再提交 Issue。

## 构建与测试

需要 Windows 与 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o dist/usc-v0.4.2
$app = Get-Item 'dist/usc-v0.4.2/SDVX特效精简-USC深黑.exe'
$p = Start-Process -FilePath $app.FullName -ArgumentList '--self-test' -Wait -PassThru
Get-Content dist/usc-v0.4.2/self-test.txt
if ($p.ExitCode -ne 0) { throw 'Tests failed' }
```

测试使用临时合成文件覆盖生成、开关、恢复、冲突保护和命中资源保护。若通过 `SDVX_GAME_ROOT` 环境变量或本机配置提供了支持的游戏目录，还会对其进行只读资源校验，不会自动启用覆盖。

规则目录仅包含相对资源名、偏移、大小和校验值，不包含原始几何或图片。重新生成当前规则需要自行提供兼容游戏：

```powershell
py -m venv .venv
.venv/Scripts/python -m pip install -r tools/requirements.txt
.venv/Scripts/python tools/build_effect_catalog.py --game-root 'X:\Games\LAZY_KFC'
.venv/Scripts/python tools/validate_background_fills.py --game-root 'X:\Games\LAZY_KFC'
.venv/Scripts/python tools/validate_archive_paths.py --game-root 'X:\Games\LAZY_KFC'
```

`LegacyEffectCatalog.json.gz` 和 `UnsafeEffectCatalog.json.gz` 仅用于识别、校验并停用历史覆盖。不要使用旧版生成算法。

## 实现参考与许可

覆盖路径依据 [IFS LayeredFS](https://github.com/mon/ifs_layeredfs)，规则生成使用 [ifstools](https://github.com/mon/ifstools)，GE2D 格式参考 [bemaniutils](https://github.com/DragonMinded/bemaniutils/blob/master/bemani/format/afp/geo.py)，启动选项参考 [Spice2x](https://github.com/spice2x/spice2x.github.io)。这些依赖不随发布包分发，适用各自许可证。

本项目源码采用 [MIT License](LICENSE)。该许可证不涵盖游戏及第三方资源。本项目与游戏厂商、Spice2x 或上述工具作者无隶属关系。
