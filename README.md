# ArcPet 猫姬桌宠

当前程序 **0.14**，正式动画 **v12**：19段、740张透明PNG。C# / WPF / .NET Framework4.8，功能实现与桌面适配、动画、设置分别管理。

[下载0.14运行版](https://github.com/resonaaaaa/ArcPet/releases/tag/v0.14) · [全部动画](preview.html) · [跳舞双向预览](design/actions-v12/review.html) · [使用说明](app/USAGE.txt)

## 已完成

- 拖动、Ctrl+左键上下缩放（0.4x—2x），锁定位置和大小；右键/托盘显示隐藏、睡眠、移动模式、自启动开关；设置自动记忆。
- 待机60—90秒随机触发进食、思考、音乐、做操、电话；做操2轮、思考3轮、其余5轮。满足睡眠条件后等待当前动作结束。
- 滑板仅在未锁定且允许移动时触发；长程滑至边缘，短程往返，任意鼠标输入立即停位。
- 锁定/未锁定双击均跳舞，每次随机起始朝向，每3轮镜像一次，1秒/轮、12轮；锁定0.5秒长按蹲跳、1.5秒长按进入火箭蓄力，松手发射归位。
- 普通动作和跳舞保持原打断规则：单击、右键、未锁定拖动不中断；有效双击或锁定长按可抢断。滑行打断独立处理。
- 待机/思考气泡、15个像素图案、三云依次淡入。程序只显示托盘入口，不占任务栏。
- 跳舞保留身体前后换重心，修订发色、头型及尾巴，增加镜像。观感仍以用户实机验收为准。

## 当前目录

| 目录 | 用途 |
| --- | --- |
| anime/ | 唯一的正式播放帧与逐帧时长；保留原始PNG画质 |
| app/ | 当前代码、测试、图标与独立气泡资产 |
| dist/ArcPet-v0.9/ | 当前0.14运行目录，固定路径以保留自启动登记 |
| data/ | 个人设置；不加入分发包 |
| design/actions-v12/ | 当前跳舞原图、提示词、检查记录、共享预览入口 |
| design/source-library/ | 整理保留的动作原图与可编辑气泡SVG，无重复播放帧 |
| sources/、reference/ | 基础动作源帧、点击原图与角色/动作参考 |
| previews/ | 质量90的WebP展示副本及一个跳舞GIF，尺寸和时序不变 |
| tools/ | 当前素材验证、预览、跳舞导出及发布工具 |
| docs/ | 验证、清理与历史文字记录 |
| history/ | 轻量旧设计文字/工具记录和代码快照 |
| exports/ | 一个0.13回退包；当前分发包按需生成 |

## 文件管理与重建

2026-10-28已删除过期设计的重复帧、预览、废弃原稿及旧图片归档。保留必要原图、原始播放PNG与一个回退版本。旧设计文字/提示词/工具可在本机历史文字档案（history/design-records-and-legacy-tools.zip，不上传GitHub）查阅，旧图片路径是历史位置，废弃图片不再保留。本机空间优化报告（docs/storage-optimization-2026-10-28.json）。

实际文件大小由约661.6MiB降至225.0MiB，释放436.6MiB（66%）；design由312.4MiB降至21.3MiB。删除2045个过期/重复文件，并清理验证产生的临时构建。程序及正式PNG校验值未变；桌宠保持运行，保留其间正常保存的最新设置。

当前以anime/为正式播放资产的唯一来源。`tools/build_animation.py`验证现有PNG并重新生成小体积预览，不再串联各旧版本目录或自动生成整个工程ZIP。`tools/import_dance_v0_13.py`可从当前原图重新导出双向跳舞，直接写入正式目录。其余动作的PNG就是保留的可编辑原始资产；必要早期原图集中于source-library，旧版本裁剪脚本仅保留作历史参考。

构建与检查：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File app/build.ps1 -Test -OutputDirectory dist/ArcPet-check
python -B tools/build_animation.py
python -B tools/package_desktop_v0_14.py --verify-only
```

按需生成当前分发包：`python -B tools/package_desktop_v0_14.py`，默认从已安装目录打包到exports/ArcPet-v0.14.zip；可用--source指定已验证的构建目录。生成后按需要分发，不长期保存多个副本。构建验证后清理临时目录和app/.build。

当前检查：624项核心、204项窗口回归，共828项；本次整理后另验证了19段/740帧完整性、60张跳舞的逐像素复现、19份预览的尺寸与总时长、打包检查和独立构建。[核心检查记录](docs/desktop-v0.14-validation/core-tests.txt)。

每次更新维护本README、[RAEDME记录](RAEDME.md)及[TODO](TODO.md)。

## 2026-10-30 · 跳舞每三轮镜像

双击随机选择起始朝向，按A3轮→B3轮→A3轮→B3轮完整播放。镜像仅改变下一轮使用的资产，不重新登记播放请求；再次双击开始新批次并重新选择起始朝向。打断、睡眠排队和所有原帧保持不变。

0.14已在原路径正常更新重启，设置和自启动登记保留；新程序临时运行包和构建副本发布后清理，回退版本更新为0.13。

## GitHub与Release

[私有仓库](https://github.com/resonaaaaa/ArcPet) · [v0.14 Release](https://github.com/resonaaaaa/ArcPet/releases/tag/v0.14)。下载运行包后完整解压并运行ArcPet.Preview.exe；首次运行使用默认设置。

仓库保存当前代码、正式PNG、必要原稿及轻量预览。data、dist、exports、history、临时构建和本机维护清单不提交；运行ZIP仅作为Release附件发布。版本标签v0.14对应当前源码。当前未指定开源许可证，角色与美术资产不因上传而获得额外授权。
