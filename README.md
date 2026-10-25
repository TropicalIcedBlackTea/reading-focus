# 文献专注 · Reading Focus 1.2

停留在屏幕角落的 Windows 悬浮倒计时器，用于专注阅读论文：计时、章节进度、随手笔记、离线查词与永久生词本。

## 使用

下载 1.2 发行包，解压后双击 **文献专注.exe**。支持 Windows 10 / 11 和 .NET Framework 4.8，免安装。

- 25 / 45 / 60 分钟或自定义 1–180 分钟；开始后收起为迷你窗口，暂停时展开。
- 开始或继续计时时，立即最小化文件资源管理器、浏览器和 Zotero 以外的应用窗口；计时期间切到其他应用会再次被最小化。暂停即解除。
- 三个方框依次记录引言、模型、发现；「下一篇」切换论文。今日阅读分钟与章节数按实际计时统计。
- 计时中随时打开笔记，结束后随本轮一起导出。
- 在阅读应用里复制英文单词即可查词，释义来自内置离线词典，不联网。连续查询的多个单词同时显示，每张卡片各自停留 10 秒。跨行选中的单词（`hypo-` + 换行 + `thesis`、PDF 连字、软连字符）会被还原为一个词；复数、时态和英式拼写归并到词条原形。
- 查过的单词自动收入永久生词本，跨轮次与重启保留，记录查询次数和时间，可搜索、可移除，并同步导出为 `词表/生词本.csv`。
- 每轮结束导出纯单词 TXT、中英 CSV / TSV、笔记和本轮汇总。

详细操作见 [使用说明](docs/使用说明.md)，版本变化见 [1.2 发行说明](docs/RELEASE-1.2.md)。

## 内置词典

两份词表在编译时嵌入程序，每行一个词条（`词条<TAB>词性与中文释义`），查词时学术词表优先：

- `assets/academic-words.tsv`：约 6800 个论文常用词条，覆盖 Coxhead 学术词表（AWL）全部词族、学术词汇表（AVL）常用词、研究方法与统计 / 计量术语、经济学各领域（理论、产业组织、贸易、宏观、金融、劳动与发展）术语、学术连接词与常见缩写。
- `assets/general-words.tsv`：约 1.5 万个通用词条。按真实语料词频（wordfreq）排序、经 WordNet 过滤人名与非词，选取学术词表未收录、又不属于最基础词汇的常用词，逐条编写中文释义。

```text
heterogeneity	n. 异质性；多样性
formidable	adj. 强大的；令人敬畏的；艰巨的
```

普通词查找不区分大小写；全大写缩写（如 `OLS`、`ATE`）只匹配大写。两份词表都没有的词，如果由已知词加常见前缀（un-、non-、over-、multi- 等）、-ness / -less 或连字符组合而成，会给出标注「〔构词〕」的拆解提示，例如 `unobservable` →「〔构词〕un-（不/未）+ observable（可观测的）」。

## 构建、检查与打包

在项目目录使用 Windows PowerShell 5.1 或更高版本：

```powershell
# 正式程序：artifacts/文献专注.exe
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 隔离运行核心检查、组件流程检查与专注保护检查
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1

# 含程序、使用说明、发行说明和许可证的发行包
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

构建使用 Windows 自带的 .NET Framework v4 C# 编译器（C# 5）。测试版通过 `TESTING` 条件编译包含检查代码，生成 `artifacts/tests/ReadingFocus.Tests.exe`；正式程序不含测试入口。专注保护检查会启动一个临时窗口 `FocusGuardProbe.exe` 并只对它操作，不触碰其他应用。报告与组件渲染写入 `artifacts/test-results`。

`build.ps1 -OutputPath <路径>` 可以更改输出位置；`build.ps1 -TestBuild` 只生成测试版。重建正在运行的同名 exe 前，请先退出程序。

## 目录

```text
src/                 C# 源码、程序集信息和应用清单
assets/              应用图标与内置词典
tests/               核心检查、组件流程检查与专注保护检查窗口
docs/                使用说明与发行说明
build.ps1            正式版 / 测试版编译
test.ps1             隔离执行检查
package.ps1          生成并核对发行包
artifacts/           本地生成内容，不提交仓库
```

## 数据与隐私

阅读时间、章节统计、笔记、词表和生词本只保存在本机：设置与历史位于 `%LOCALAPPDATA%\ReadingFocusTimer`（生词本随历史保存在 `reading-data.xml`），导出默认写入程序旁的 `词表` 目录。程序不联网，没有账号、遥测或自动同步。

## 许可证

本项目以 [GNU 通用公共许可证第 3 版（GPL-3.0）](LICENSE) 授权发布：可以自由使用、修改和再分发，分发修改版或程序时须以相同协议提供源代码。
