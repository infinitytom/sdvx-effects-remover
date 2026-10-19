# SDVX Effects Remover

Windows 图形工具：通过可恢复的 IFS LayeredFS 覆盖，减少指定 SDVX 版本的背景和装饰光效，同时保留按键命中与基本读谱信息。

**实验项目，修正版尚未完成实际歌曲验证。** 不保证提高帧率或消除所有演出。请先确认原版游戏稳定，再逐组试用。

## 下载与使用

在 [Releases](https://github.com/infinitytom/sdvx-effects-remover/releases) 下载 Windows x64 预览版，需要安装 .NET 8 Windows Desktop Runtime。

1. 退出游戏和 LAZY 启动器，打开程序。
2. 点击“浏览”，选择含 `contents` 的 LAZY_KFC 根目录，再读取模式。程序会在本机用户配置目录记住所选路径。
3. 选择需要的精简项，点击“应用特效模式”，预览后确认，下次启动游戏生效。
4. 点击“恢复原版画面”可停用本版全部覆盖。首次建议只试用一组；“仅留命中”会选择全部五组，包括隐藏判定显示。

仅支持已核对的 DLL 标识 `KFC-6aa256e6_7ad708` 和匹配的原始资源。启用前核对原资源哈希；不匹配或与其他模组覆盖同一资源容器时停止操作。项目不提供游戏、曲库、原始纹理、DLL 或账号数据。

## 可选精简内容

| 分组 | 修改范围 |
| --- | --- |
| 背景 | 已识别的舞台、人物背景、背景粒子几何绘制 |
| 旋钮尘粒 | 指定旋钮尘粒动画 |
| 轨道反光 | 指定轨道反光与 HUD 泛光 |
| 额外装饰 | 旋钮周边光效、连击光束、HUD 动态装饰 |
| 判定显示 | 可识别的 CRITICAL/ERROR、EARLY/LATE、CHAIN 字样及通关、失败、PUC/UC 演出 |

保留按键命中闪光、长按反馈、按键光柱、音符、旋钮定位、血条、分数和连击数字。隐藏判定显示不改变实际判定逻辑、成绩或通关结果，可独立恢复。镜头倾斜、旋转、谱面演出指令、Live2D 和动画脚本计算不在本版处理范围。

“启动附加设置”页另外提供日志级别、副屏强制刷新、Spice 叠加界面和配套的游戏/音符 60/120 FPS 设置。它们与特效分组分别应用。

## 旧版崩溃与本版修正

早期实现把 GE2D 的绘制记录数改成零。实际游戏日志出现 `afpu-render.c:1147: afpu_render_draw_geo` 断言；对对应本地渲染器的检查确认，该分支要求记录数为 **1**。因此旧方法不能继续使用，原先的静态格式测试不足以验证引擎兼容性。

当前实现保留文件头、绘制记录、计数、索引与其他数据，仅把选定几何的二维顶点重合到原始第一个顶点。测试覆盖本地引擎的计数约束，以及 22312 个规则条目的非顶点数据保持不变。**这修正了已定位的结构问题，仍不能代替实际游玩稳定性测试。**

旧版覆盖需先用“停用旧版覆盖”移出 `data_mods`。本版使用版本 2 标记及独立备份目录，不复用旧版清零文件，也不会自动开启新方案。

## 备份与恢复

原始游戏 IFS、音频、谱面和 DLL 不会被改写。生成的五组覆盖放在 `contents/data_mods/zzzz_sdvx_smooth_*`；停用后移至游戏目录 `.sdvx-smooth/effects-v2/disabled`，历史记录位于同目录的 `history`。旧版备份保留在 `effects-v1`，请勿手动复制回游戏。

启动设置修改前备份至 `.sdvx-smooth/backups`，支持完整恢复；若同一配置后来被其他程序修改，工具拒绝直接覆盖。更新游戏或遇到异常时先恢复原版，保留本地日志供排查。日志可能包含个人信息，请先脱敏再提交 Issue。

## 构建与测试

需要 Windows 与 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o dist
$app = Get-ChildItem dist -Filter '*.exe' | Select-Object -First 1
$p = Start-Process -FilePath $app.FullName -ArgumentList '--self-test' -Wait -PassThru
Get-Content dist/self-test.txt
if ($p.ExitCode -ne 0) { throw 'Tests failed' }
```

测试使用临时合成文件覆盖生成、开关、恢复、冲突保护和命中资源保护。若通过 `SDVX_GAME_ROOT` 环境变量或本机配置提供了支持的游戏目录，还会对其进行只读资源校验，不会自动启用覆盖。

规则目录仅包含相对资源名、偏移、大小和校验值，不包含原始几何或图片。重新生成当前规则需要自行提供兼容游戏：

```powershell
py -m venv .venv
.venv/Scripts/python -m pip install -r tools/requirements.txt
.venv/Scripts/python tools/build_effect_catalog.py --game-root 'X:\Games\LAZY_KFC'
```

`LegacyEffectCatalog.json.gz` 和 `UnsafeEffectCatalog.json.gz` 仅用于识别、校验并停用历史覆盖。不要使用旧版生成算法。

## 实现参考与许可

覆盖路径依据 [IFS LayeredFS](https://github.com/mon/ifs_layeredfs)，规则生成使用 [ifstools](https://github.com/mon/ifstools)，GE2D 格式参考 [bemaniutils](https://github.com/DragonMinded/bemaniutils/blob/master/bemani/format/afp/geo.py)，启动选项参考 [Spice2x](https://github.com/spice2x/spice2x.github.io)。这些依赖不随发布包分发，适用各自许可证。

本项目源码采用 [MIT License](LICENSE)。该许可证不涵盖游戏及第三方资源。本项目与游戏厂商、Spice2x 或上述工具作者无隶属关系。
