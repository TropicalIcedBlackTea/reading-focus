# 文献专注 · Reading Focus 1.1

停留在屏幕角落的 Windows 悬浮倒计时器，用于专注阅读论文：计时、章节进度、随手笔记，以及内置学术词典的单词收集。

## 使用

下载 1.1 发行包，解压后双击 **文献专注.exe**。支持 Windows 10 / 11 和 .NET Framework 4.8，免安装。

- 25 / 45 / 60 分钟或自定义 1–180 分钟；开始后收起为迷你窗口，暂停时展开。
- 开始或继续计时时，立即最小化文件资源管理器、浏览器和 Zotero 以外的应用窗口；计时期间切到其他应用会再次被最小化。暂停即解除。
- 三个方框依次记录引言、模型、发现；「下一篇」切换论文。今日阅读分钟与章节数按实际计时统计。
- 计时中随时打开笔记，结束后随本轮一起导出。
- 在阅读应用里复制英文单词即可收入词表，释义来自内置学术词典，不联网。跨行选中的单词（`hypo-` + 换行 + `thesis`、PDF 连字、软连字符）会被还原为一个词；复数、时态和英式拼写归并到词条原形。
- 结束后导出纯单词 TXT、中英 CSV / TSV、笔记和本轮汇总。

详细操作见 [使用说明](docs/使用说明.md)，版本变化见 [1.1 发行说明](docs/RELEASE-1.1.md)。

## 内置词典

`assets/academic-words.tsv` 收录约 6000 个论文常用词条：Coxhead 学术词表（AWL）全部词族、学术词汇表（AVL）常用词、研究方法与统计 / 计量术语、经济与社会科学常用词、学术连接词与常见缩写。每行一个词条：

```text
heterogeneity	n. 异质性；多样性
estimate	v. 估计；估算 n. 估计值；估算
```

普通词查找不区分大小写；全大写缩写（如 `OLS`、`ATE`）只匹配大写。文件在编译时嵌入程序，修改后重新构建即可生效。

## 构建、检查与打包

在项目目录使用 Windows PowerShell 5.1 或更高版本：

```powershell
# 正式程序：artifacts/文献专注.exe
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 隔离运行核心检查、组件流程检查与专注保护检查
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1

# 仅含程序、使用说明和发行说明的发行包
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

构建使用 Windows 自带的 .NET Framework v4 C# 编译器（C# 5）。测试版通过 `TESTING` 条件编译包含检查代码，生成 `artifacts/tests/ReadingFocus.Tests.exe`；正式程序不含测试入口。专注保护检查会启动一个临时窗口 `FocusGuardProbe.exe` 并只对它操作，不触碰其他应用。报告与组件渲染写入 `artifacts/test-results`。

`build.ps1 -OutputPath <路径>` 可以更改输出位置；`build.ps1 -TestBuild` 只生成测试版。重建正在运行的同名 exe 前，请先退出程序。

## 目录

```text
src/                 C# 源码、程序集信息和应用清单
assets/              应用图标与内置学术词典
tests/               核心检查、组件流程检查与专注保护检查窗口
docs/                使用说明与发行说明
build.ps1            正式版 / 测试版编译
test.ps1             隔离执行检查
package.ps1          生成并核对发行包
artifacts/           本地生成内容，不提交仓库
```

## 数据与隐私

阅读时间、章节统计、笔记和词表只保存在本机：设置与历史位于 `%LOCALAPPDATA%\ReadingFocusTimer`，导出默认写入程序旁的 `词表` 目录。程序不联网，没有账号、遥测或自动同步。

## 许可证

项目许可证待作者指定，当前未附加开源许可证。
