# ArcPet 0.14 · 双击跳舞与独立交互

C# / WPF / .NET Framework 4.8，无新增NuGet依赖。运行位置继续使用 `dist/ArcPet-v0.9/`，目录名不代表文件版本；保留既有自启动登记和根目录data偏好。

## 当前输入契约

| 输入 | 结果 |
| --- | --- |
| 锁定或未锁定双击 | 两次短点击，间隔不超过350ms、位置差不超过8 DIP；第二次短点击松开后直接播放12轮跳舞 |
| 普通随机动作或跳舞时双击 | 立即替换为新一组12轮跳舞，不排队、不恢复旧动作 |
| 锁定下按住500ms | 保留完整蹲下、按住小循环、松开弹跳；可打断普通随机动作和跳舞 |
| 锁定下按住1500ms | 沿原分段火箭路径蓄力、渐红，松手发射并准确归位 |
| 未锁定长按/拖动 | 移动位置，保持普通随机动作/跳舞播放 |
| Ctrl + 左键拖动 | 保留0.4x—2x缩放，不登记为双击 |
| 单击、右键 | 更新活动时间，保持普通随机动作/跳舞播放 |
| 滑行中的任意鼠标输入 | 停在当前位置并待机，停止按下单独消费 |
| 睡眠中首次左右键 | 只唤醒，清除双击历史 |

超过8 DIP的真实拖动、Ctrl缩放、右键、模式变化和捕获取消均清除短点击候选。锁定状态下第二次按住满500ms以长按优先，不同时触发跳舞。

## 模块边界

- `ArcPet.Animation`：不可变帧/时长、目录加载、单调时钟和通用姿势衔接标记；不依赖窗口或鼠标。LoadAll加载正式19段动画。
- `PetBehaviorController`：识别短点击/双击/锁定长按，选择播放请求与完成策略。每次双击通过注入的随机源等概率选择dance或dance_mirrored，再使用所选片段的12轮有限动画，阶段仍为automatic；不把dance放入待机随机池。原随机池、配置轮数和睡眠排队保持不变。
- `PetWindow`：WPF事件转换为行为输入，区分锁定点击与未锁定拖动/缩放，真实移动清除候选。以16ms轮询单调播放时钟，延迟时选择当前正确帧，不堆积补播。图片缓存按内容去重，不变形。
- `HorizontalTravel`：纯水平行程控制，管理长程到边界、短程5w往返与中立姿势换向。skate拥有独立的任意输入停止规则。
- `VerticalFlight` / `RocketArtworkWindow`：保持900ms上升、120ms顶部停留、800ms回原处；火焰使用独立320×440被动显示层，不缩小角色或扩大点击范围。保存偏好时采用飞行原点。
- `MoodBubbleController` / `MoodBubbleWindow`：独立观察最终播放阶段；待机20—30秒/持续4秒，思考每6秒换内容。dance和其他不允许阶段同步收起气泡，不反向操作角色播放。
- `ArcPet.Settings`：后台串行、原子保存与备份；启动恢复大小、位置、锁定、显示、睡眠、置顶、移动模式。退出Flush，临时按键和动作状态不保存。
- `StartupRegistration`：只读写当前用户ArcPet登录启动项；测试注入内存存储，不修改真实注册。头像图标嵌入程序并与托盘共用。

## 跳舞素材与时长

当前重绘在 `design/actions-v12/`：暖金发色、较高头型、尾巴摆动；16姿势按相邻往返生成30帧。全批使用同一个等比缩放与脚底锚点，不逐帧拉伸。`dance_mirrored` 是320×320画布的精确水平翻转。每次有效双击独立等概率选择朝向；同方向连续出现属于正常随机结果。

`tools/import_dance_v0_13.py`负责统一导出、镜像、清单与预览。时序延续原1200ms按5/6累计缩短：精确1000ms，两端各250ms，过渡为16/17/25ms；12轮360帧/12000ms。透明WebP保持精确时长，GIF按20ms采样。重建入口最终调用新版导入，避免恢复旧跳舞。

最终19段/740张角色PNG；其余680张帧、气泡素材和v10历史设计保持不变。跳舞使用普通automatic完成出口，播完12轮后才评估睡眠，结束后重新安排随机待机时间。被鼠标有效打断的批次不会恢复。

## 构建与验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File app/build.ps1 -Test -OutputDirectory dist/ArcPet-v0.13
powershell -NoProfile -ExecutionPolicy Bypass -File app/test-windows.ps1
```

`test-windows.ps1`默认检查独立0.13目录；支持 `-DanceOnly`、`-MoodOnly`、`-RocketOnly`，也可指定Runtime/Report。所有诊断均使用内存或隔离设置，不写真实自启动项。WPF锁定点击采用路由事件，未锁定点击/拖动/缩放调用生产手势适配方法，避免合成事件不能改变原生鼠标按键状态导致误取消；没有注入物理鼠标。

本次552项核心检查包含两个朝向在两种锁定状态的可达性、每次双击重新随机、逐帧时序一致性；窗口回归覆盖既有输入138项、气泡19项、火箭16项、跳舞24项。总计749项。分钟级体验、混合DPI、多屏变化及重绘观感继续实机确认。

`tools/verify_animation.py`核对19段/740帧透明边界与共同姿势。`tools/package_desktop_v0_13.py`核对旧资产、正式资产与运行目录散列、检查报告及ZIP内容，输出运行包和validation.json。`tools/update_runtime_v0_13.ps1`先核验报告与文件，正常退出旧窗口再更新原位置，失败可从临时备份恢复，保留偏好和自启动；有运行实例时正常重启。

报告在 `docs/desktop-v0.13-validation/`，当前运行包为 `exports/ArcPet-v0.13.zip`，旧0.11源码在 `history/app-v0.11-before-dance.zip`。构建、测试和更新备份仅放app/.build，正式发布后可清理；清理后可按上述命令再生成。


## 2026-10-28 文件管理更新（以此为准）

正式PNG唯一来源为根anime；design仅保留当前原图与必要源图，旧版本代码/文字已整理入history轻量档案。build_animation.py现在验证现有素材并重建预览，不再运行历史导入链。dance导出依赖tools/dance_asset_helpers.py，直接输出根anime和previews；无design逐帧副本。当前分发ZIP按需生成，package_desktop_v0_13.py默认从原运行目录打包，--verify-only仅校验。当前目录说明见根README。


## 0.14 当前跳舞排布

PetBehaviorController.DanceLoopsPerFacing=3，双击只随机起始朝向一次，再将dance/dance_mirrored按三轮一组组合为360帧有限片段；3/6/9秒切换，无新播放请求。Id保持起始朝向用于现有观测，具体画面按各帧AssetPath读取，不能用批次Id推断整个期间朝向。锁定与未锁定均适用，原打断/睡眠策略不变。
