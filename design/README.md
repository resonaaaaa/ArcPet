# 当前设计资料

正式程序使用根目录 `anime/`，当前确认稿和必要原画保留在下列目录。此页只列当前状态，早期修订过程见Git历史和根README工作记录。

| 目录 | 内容 |
| --- | --- |
| actions-v12、source-library | 当前跳舞源稿、基础动作原图与气泡源图 |
| lying-v1、walk-v4、drag-lift-v1 | 已接入的趴姿、散步与拎起源稿 |
| pomodoro-v3 | 已确认开始动画；[逐帧预览](pomodoro-v3/review.html) |
| pomodoro-reading-v3 | 已确认阅读循环；[逐帧预览](pomodoro-reading-v3/review.html) |
| pomodoro-pause-v3 | 已确认放书、托腮循环和固定道具层；[预览](pomodoro-pause-v3/review.html) |
| pomodoro-resume-v1 | 已接入的继续拿书动作；[预览](pomodoro-resume-v1/review.html) |
| pomodoro-end-stretch-v1 | 已接入的放书伸懒腰；[预览](pomodoro-end-stretch-v1/review.html) |
| pomodoro-end-tomato-v1 | 已接入的扔番茄与糊屏淡出；[预览](pomodoro-end-tomato-v1/review.html) |

番茄钟在0.20接入，0.20.1修复原生鼠标命中。源稿、当前PNG、固定道具及提示词用于后续美术修订和重建，不能当作普通缓存清理。未通过的番茄钟v1/v2草稿已删除；walk-v1/v2/v3仅保留少量历史参考文字。

阅读共享导出逻辑现位于 `tools/pomodoro_reading_export.py`，由 `tools/build_pomodoro_reading_v3.py`调用；其他阶段工具名与当前设计目录相对应。默认道具输出使用暂停v3，避免重新生成已弃用的v2目录。
