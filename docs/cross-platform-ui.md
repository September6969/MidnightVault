# MidnightVault 跨平台界面规范

`design-system/design-tokens.json` 是跨平台视觉语义的基准。Windows 与 Android 分别通过 XAML 资源和 Compose Theme 适配，不共享页面结构，也不做整体缩放。

## 布局模式

- Compact：Android 小于 600dp，Windows 页面宽度小于 640px。Android 使用底部导航与单列内容；Windows 使用覆盖式导航和单列内容。
- Medium：Android 为 600–839dp，Windows 为 640–899px。Android 使用 NavigationRail；Windows 使用紧凑侧栏并对页面内容重排。
- Expanded：Android 从 840dp 开始，Windows 从 900px 开始。Android 使用带文字的固定侧栏并允许双列内容；Windows 使用完整侧栏、宽屏指标卡和最大内容宽度。

## 平台差异

- Windows 保持 Fluent、Mica、鼠标悬停、工具提示、键盘快捷键与可见滚动条。
- Android 保持 Material 3、48dp 触控目标、edge-to-edge、安全区域、系统返回、IME 避让、底部导航和 Snackbar。
- 两端共享强调色、语义状态色、字号层级、间距节奏、圆角意图和业务状态命名。

## 资源

- 导航、返回、刷新、设置等功能图标使用平台图标库。
- 备份空状态与品牌插画可复用生成式透明素材；文字、按钮、卡片和状态均由原生控件绘制。
- Android 位图导出为 WebP 密度资源，Windows 保留透明 PNG 源和高分辨率应用图标。

## Token 生成链

- 修改 `design-system/design-tokens.json` 后运行 `python tools/generate_design_tokens.py`。
- 生成结果包括 WinUI XAML/C# 和 Android Compose 的颜色、字号、行高、字重、间距、圆角、尺寸、动效与断点常量。
- 运行 `python tools/export_android_assets.py` 可从高分辨率透明源重新导出等视觉尺寸的多密度 WebP；启动图颜色同样读取共享 token。
- 设备名、接收地址和控制面地址可通过 `SavedStateHandle` 恢复；上传令牌和控制令牌不会写入 Bundle，只保存在 Android Keystore 支持的凭据存储中。

## 视觉回归

- `scripts/capture-winui-visuals.ps1` 执行 5 项快速门禁；添加 `-Full` 执行 15 项验收矩阵，覆盖 640×480 最小窗口、需求中的 7 个 PC 尺寸、四个主要页面、深浅主题以及 100%/125%/150% 输出栅格比例。
- WinUI 截图由应用内部离屏渲染，包含自定义标题栏；当前显示器放不下 2560×1440 时会按目标尺寸离屏布局。125%/150% 用例验证同一 DIP 布局的高密度栅格输出，不替代真实多显示器 DPI 切换测试。
- `scripts/capture-android-ui-matrix.ps1` 在唯一授权设备或模拟器上执行 7 项矩阵，覆盖 Compact/Medium/Expanded、竖横屏、深浅主题、100%/130%/160% 字体、手势/三键导航和软键盘打开。
- Compose 官方 Golden 使用 JDK 17：`gradlew :app:updateDebugScreenshotTest` 更新基线，人工复核后用 `gradlew :app:validateDebugScreenshotTest` 校验。常规 `scripts/verify.ps1` 和 Windows CI 都会执行校验任务，但不会自动更新基线。
