# 当前跳舞设计v12

保留原先通过的身体前后换重心；内置绘图修订发色、头型与尾巴细节，正式双向动画各30帧，每轮1秒，双击随机起始朝向，每3轮切换镜像，共12轮。

唯一原图为sources/preserved-body-atlas.png，同批16姿势往返排布。正式帧在根anime/dance与dance_mirrored，预览共用根previews，不在此重复保存。

[双向预览](review.html) · [GIF](../../previews/dance.gif) · [提示词](docs/prompts.json) · [绘制检查](docs/verification.json)。身体定位变化约35度、眼线倾角约3度以内；这是图像定位估计，不能代替观感验收。

导出：python -B tools/import_dance_v0_13.py。活动算法已移至tools/dance_asset_helpers.py，不依赖旧版本设计目录。输出会直接覆盖当前跳舞正式帧；修改原图后应先审核再执行。GIF为时序示意，原始PNG完整保留画质，WebP只是压缩展示副本。

旧候选图片已清理；提示词中的旧候选路径是历史记录，部分候选不再保留。
