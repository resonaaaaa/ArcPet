# 猫姬应用图标

- `arc-head-source.png`：内置图像生成工具根据 `reference/OIP-C.webp` 制作的透明头像原图，保留完整耳尖与头发。
- `arcpet.ico`：程序 EXE、WPF 窗口和托盘共用，包含 16、20、24、32、40、48、64、128、256 像素透明图层。
- `arc-head-preview.png`：256 像素预览。
- `tools/build_icon.py` 仅缩放和编码，不重绘；图标同时嵌入 EXE 的 Windows 图标资源和托管资源，因此运行时无需单独携带 ICO。
- 生成日期：2026-10-26。使用内置工具模式，无需 API 密钥；角色参考来自用户提供的猫姬 Arc 图片。

## 最终生成提示词

Use case: stylized-concept. Asset type: Windows desktop application and notification tray icon. Reference image: supplied Neco-Arc / 猫姬 image, use only its character identity and clean flat anime drawing style. Create ONE isolated head-only icon of this same cat character, facing front with a slight playful head tilt: blonde tousled fringe, two large pointed brown/blonde cat ears with cream insides, huge off-white oval eyes with long narrow dark red vertical pupils, tiny mischievous open smiling mouth. Warm beige face, bold clean dark-brown outlines, simple flat cel shading. Center the complete head and both ear tips in a square with small even transparent padding; occupy about 88% of canvas. No neck, no shoulders, no hands, no body, no tail, no extra props, no text, no border, no badge, no gradients or shadows outside the head. Prioritize clear recognizable facial features at 16 and 32 pixel icon sizes. Truly transparent background.


## 0.10 思考气泡

moods/ 保存三张独立云朵 PNG、15 张独立像素图案和 manifest.json；布局、分类及淡入参数由清单描述，图案不烘焙进框体。可编辑原稿与组合验证见 [原图资料库](../../design/source-library/README.md)，角色原帧不修改。
