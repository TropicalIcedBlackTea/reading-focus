# 文献专注 · Reading Focus 1.0

一个停留在屏幕角落的 Windows 悬浮倒计时器，用来专注阅读论文，并保存阅读进度、随手笔记和单词。

## 使用

下载 1.0 发行包，解压后双击 **文献专注.exe**。支持 Windows 10 / 11 和 .NET Framework 4.8；不需要安装 Python、.NET SDK 或其他开发工具。

- 可选 25 / 45 / 60 分钟，或自定义 1–180 分钟；开始与继续时自动进入迷你模式，暂停后展开。
- 计时中保留暂停和笔记入口；需要提前结束时先暂停，再使用唯一的「结束本轮」按钮。
- 三个无文字方框记录引言、模型、发现，勾选播放彩带；随时切换下一篇，保留已经完成的部分。
- 查看今天累计阅读分钟数与三类章节完成数，暂停时间不计入。
- 计时中点击「笔记」直接展开文本框，约 450ms 自动保存；结束后展示并导出，重启仍能查看上一轮笔记。
- 专注期间允许 Zotero、文件管理器和常见浏览器；切到其他普通应用时尝试最小化，并跳动、闪红边提醒。暂停即解除保护。
- 复制单个英文词后后台翻译，成功释义展示 10 秒；结束后汇总，失败项目可以重试，按钮显示当前处理状态。
- 导出每行一个词的 TXT、中英 CSV / TSV、独立笔记和本轮汇总。

扇贝单词目前提供的是**纯单词 TXT 和复制入口**。其当前 App 文件导入入口尚未验证，不承诺特定文件可直接导入，也不自动同步账号。

详细操作见 [使用说明](docs/使用说明.md)，版本变化见 [1.0 发行说明](docs/RELEASE-1.0.md)。

## 构建、检查与打包

在项目目录使用 Windows PowerShell 5.1 或更高版本：

```powershell
# 正式程序：artifacts/文献专注.exe
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 隔离运行核心检查与组件流程检查
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1

# 仅含程序、使用说明和发行说明的发行包
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

构建使用 Windows 的 .NET Framework v4 C# 编译器，语言版本为 C# 5。测试版通过 `TESTING` 条件编译包含检查代码，生成 `artifacts/tests/ReadingFocus.Tests.exe`；正式程序不编入测试类或测试命令入口。检查使用合成数据，报告与组件图片写入 `artifacts/test-results`。

`build.ps1 -OutputPath <路径>` 可以更改输出位置；相对路径从项目目录解析。`build.ps1 -TestBuild` 只生成测试版。重建正在使用的同名 exe 前，请先暂停并退出该程序。

打包生成 `artifacts/ReadingFocus-1.0-win.zip`，并校验包内只有三个明确列出的发行文件。实际词表、笔记、设置、历史记录和测试数据都不进入发行包。

## 目录

```text
src/                 生产 C# 源码、程序集信息和应用清单
assets/              应用图标
tests/               核心检查与 WPF 组件流程检查
docs/                使用说明与 1.0 发行说明
build.ps1            正式版 / 测试版编译
test.ps1             隔离执行检查
package.ps1          生成并核对发行包
artifacts/           本地生成内容，不提交仓库
```

## 数据与隐私

阅读时间、章节统计、笔记和词表保存在本机，设置与历史位于 `%LOCALAPPDATA%\ReadingFocusTimer`。结束后的导出默认写入程序旁的 `词表` 目录，无法写入时使用本地应用数据目录。

在线翻译由 MyMemory 提供。联网请求只包含正在计时期间识别出的**单个英文词**，不发送笔记、论文、窗口标题或其他剪贴板内容。翻译需要网络，离线失败的单词仍会保留供稍后重试。没有账号登录、遥测或自动同步功能。

## 许可证

项目许可证待作者指定，当前未附加开源许可证。
