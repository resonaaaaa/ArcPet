# ArcPet 0.18 · 桌面程序

C# / WPF / .NET Framework 4.8，无新增NuGet依赖。安装继续使用dist/ArcPet-v0.9固定路径，以保留开机启动登记和根目录data偏好；文件版本为0.18。25段、891张正式PNG以根anime为唯一程序资产来源。

## 当前交互

| 输入或条件 | 行为 |
| --- | --- |
| 锁定或未锁定时有效双击 | 12轮跳舞，随机起始朝向，每3轮镜像；可抢断普通随机动作 |
| 锁定下长按500ms | 普通蹲跳；完整蹲下后才起跳 |
| 锁定下继续按住1500ms | 整脸渐红、脚底起火，松开发射并回原处 |
| 解锁长按后拖动 / Ctrl+左键拖动 | 按住左键满0.5秒拎起，松手落地 / 直接缩放0.4x—2x |
| 单击或右键 | 更新活动时间，不打断普通随机动作 |
| 滑板/散步途中任意鼠标输入 | 独立停止滑行并消费停止点击 |
| 睡眠中首次左右键 | 唤醒并清除点击历史，不立即跳舞或蹲跳 |

双击按两次点击间隔350ms、位置距离8DIP识别；实际拖动、缩放、右键或捕获取消清除双击候选。主程序使用16ms计时更新，按单调时钟选择当前正确帧，不补播过期帧。

## 睡眠与趴姿

无左右键交互满5分钟后具备睡眠条件。普通随机动作、跳舞及滑板/散步行程先完成，再评估最新活动时间和睡眠开关。每次真正进入睡眠时统一抽选：Next(0,3)==0采用lying，其余采用sleep，即1/3与2/3。两种都使用Phase=sleep、IsSleeping=true，左右键唤醒、关闭睡眠唤醒、120000ms自然醒来的逻辑完全共用。只在入睡时选择一次，不逐轮重新抽选。

lying不进入60—90秒普通随机池；该池仍为进食、思考、音乐、做操、电话及符合条件的移动动作。做操两轮、思考三轮、其余普通动作五轮；跳舞保持12轮。趴姿36帧、1600ms循环，睡眠最多播放两分钟，不适用五轮规则。气泡仅识别待机和思考，趴姿睡眠不显示气泡。

## 大小与资产

用户确认的design/lying-v1整尾浅弧原画通过tools/import_lying_v0_16.py导入anime/lying。按记录的暖色头部像素边界校准，整幅等比1.0875，平移(-46,-25)，所有36帧使用同一个变换。待机头宽160px、趴姿159px，头中心137/137.5px；可见落地基线均292px。尺寸测量是辅助，最终以对照图与实机观感验收。

没有逐帧拉伸、旋转或造姿势，原已确认设计帧保持不变。窗口本身的大小和位置不因睡眠改变，原缩放设置继续统一作用。校准记录及对照图见docs/desktop-v0.16-validation/lying-size.json和size-comparison.png。

## 模块边界

- ArcPet.Animation：帧、逐帧时长、通用播放时钟与JSON目录加载；不依赖WPF或输入。
- PetBehaviorController：输入识别、随机动作、统一睡眠入口与退出；只发出播放请求。
- PetWindow / FrameCache：窗口、手势适配、缓存及显示，不按具体姿态另建睡眠计时器。
- HorizontalTravel：共享水平行程与边界计算，注入选定动作的左右片段；行为层以独立travel阶段处理任意输入停止，普通动作打断仍走原手势识别。
- VerticalFlight / RocketArtworkWindow：独立火箭位移和火焰层，保存飞行原点。
- MoodBubbleController / MoodBubbleWindow：观察最终阶段，不反向控制角色播放。
- ArcPet.Settings / StartupRegistration：后台原子保存、恢复设置和当前用户开机启动开关。

## 构建与验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File app/build.ps1 -Test -OutputDirectory dist/ArcPet-v0.18
powershell -NoProfile -ExecutionPolicy Bypass -File app/test-windows.ps1
python -B tools/verify_animation.py
python -B tools/package_desktop_v0_18.py --source dist/ArcPet-v0.18
```

test-windows.ps1默认检查0.18独立构建，报告放docs/desktop-v0.18-validation；可用Runtime/Report参数或DanceOnly、MoodOnly、RocketOnly限制既有专项；TravelOnly单独检查两种移动动作。完整运行包含TravelRegression与SleepVariantRegression，覆盖三种抽选结果、两种锁定状态、真实帧显示、窗口位置/缩放不变、左右键唤醒、关闭睡眠与两分钟自然结束。

测试采用内存或隔离设置和自启动存储，不写用户真实偏好或开机启动项。锁定点击用WPF路由事件；未锁定拖动/缩放用生产适配器，因为合成事件不能改变物理按钮状态。边界时序由测试时钟推进，不伪称等待了两分钟或完成实物鼠标验收。

package_desktop_v0_18.py验证旧资产未变、当前运行资产散列与测试报告，再生成按需ZIP；verify-only不生成ZIP。update_runtime_v0_18.ps1核验文件后正常退出旧程序、更新原路径，有运行实例时重启，并核对偏好和开机启动登记。分发包不带用户data。临时构建和更新备份确认后清理，当前包按需重建。


## 0.17 · 散步与滑板共享移动

待机60—90秒的随机池继续只保留一个移动名额，启用移动、未锁定、可见且有安全空间时才加入。抽中移动后单独Next(0,2)，滑板与散步各1/2；同一行程固定动作，短程返程只切换左右片段，不再次抽选。TravelRequested携带动作名，桌面据此注入对应片段给HorizontalTravel，动画播放接口保持通用。

两种动作均按较远端选择初始方向，保持窗口Top不变；长程到安全边界结束，短程预期5w后返回，双向均检测边界。边距按所选动作可见范围计算。散步左右各30帧/1200ms，换向标记为0、15双支撑附近姿势；滑板保留原有标记。移动不受普通动作固定轮数限制，睡眠等待完整行程结束。

单击、双击、长按、右键、中键、滚轮均立即停止移动并消费停止输入；锁定、隐藏或更改移动模式也会停止。普通随机动作可被有效双击或有效长按抢断，单击、右键、阈值前移动及 Ctrl 缩放期间继续播放普通动作。

正式资产anime/walk_left、anime/walk_right来自已批准的design/walk-v4首尾修订稿。按待机站立高度对整幅统一缩放1.01875，平移(-22,-6)，左右围绕固定身体中心x135镜像；所有帧同一变换，保留45°侧身的头宽透视和原头身比例。可见落地线291—292px，对照见docs/desktop-v0.17-validation/walk-size-comparison.png。tools/import_walk_v0_17.py可重建。

## 0.18 · 长按拎起

PetWindow 在左键捕获期间检测500毫秒阈值，并以达阈值时的指针位置为拖动起点。PetBehaviorController 播放拎起、悬挂与落地，快速松手时缩短剩余拎起帧。悬挂松手按已显示帧选择最短收势。复用扩展画布窗口，拖动帧384×384相对于原窗口偏移(-32,-64)，最终共用待机姿势。
