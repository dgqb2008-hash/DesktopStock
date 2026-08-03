# DesktopStock
上班摸鱼，桌面股市

<div align="center">

# 桌面股市 DesktopStock

一款轻量级 Windows 桌面 A 股行情工具,常驻桌面、随时盯盘。

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.5-512BD4)
![Language](https://img.shields.io/badge/language-C%23-239120)
![License](https://img.shields.io/badge/license-MIT-green)
![图片描述](img/1.png)
![图片描述](img/2.png)
</div>


## ✨ 项目简介

**桌面股市(DesktopStock)** 是一个使用 C# / WinForms 编写的轻量级 Windows 桌面股票行情工具。
它调用新浪财经、腾讯财经的公开行情接口获取实时数据,以紧凑的面板形式常驻桌面,
支持窗口置顶、透明度调节、自选股本地持久化、分时走势图等功能,适合在工作时把行情挂在屏幕一角,低调又不漏看。

- 🪶 **轻量**:无任何第三方 NuGet 依赖,仅使用 .NET BCL,单文件即可运行
- ⚡ **实时**:默认 5 秒刷新,批量拉取多只股票,后台异步更新不卡 UI
- 📈 **全市场**:支持沪市(sh)/深市(sz)/北交所(bj)A 股,代码自动识别
- 🎨 **红涨绿跌**:符合 A 股用户习惯的颜色方案,涨跌一眼可知
- 📌 **桌面常驻**:窗口置顶 + 透明度调节,工作盯盘两不误
- 💾 **本地持久化**:自选股、窗口位置与大小自动保存,重启即恢复
- 📊 **分时走势**:双击任一股票即可查看当日分时走势图

## 📷 程序界面

> 截图占位:可在此放入程序主界面与走势图窗口的截图。
> 推荐截图保存为 `docs/main.png`、`docs/chart.png`,然后替换下方内容:

```markdown
![主界面](docs/main.png)
![分时走势](docs/chart.png)
```

## 🚀 快速开始

### 方式一:直接运行已编译版本

进入 `DesktopStock/bin/Release/`,双击 `DesktopStock.exe` 即可运行。

### 方式二:从源码构建

任选以下一种方式:

**① 使用 Visual Studio**

用 Visual Studio 2015 及以上版本打开 `DesktopStock.sln`,按 `F5` 调试运行,或生成 Release 版本。

**② 使用 build.bat(无需 Visual Studio)**

双击运行 `DesktopStock/build.bat`,脚本会调用 .NET Framework 自带的 `csc.exe` 直接编译,
成功后自动启动程序,产物输出至 `DesktopStock/bin/DesktopStock.exe`。

**③ 使用 compile.bat(MSBuild)**

```bat
cd DesktopStock
compile.bat
```

脚本调用 MSBuild 以 Release 配置编译项目。

## 🎮 使用说明

| 操作 | 说明 |
| --- | --- |
| **添加自选股** | 在主窗口代码输入框中输入 6 位 A 股代码(如 `600000`、`000001`),点击 `+` 按钮 |
| **删除自选股** | 在对应股票面板上右键,选择删除 |
| **查看分时走势** | 双击任一股票面板,弹出分时走势窗口 |
| **窗口置顶** | 点击主窗口上的置顶按钮,切换置顶状态 |
| **调节透明度** | 拖动透明度滑块,调整窗口不透明度 |
| **拖动 / 缩放** | 自由拖动窗口位置、调整窗口大小,位置与大小会自动保存 |

界面展示信息:股票名称、代码、当前价格、涨跌额、涨跌幅,并以背景色提示涨跌方向(红涨 / 绿跌 / 灰平)。

## ⚙️ 配置说明

### 行情刷新间隔

刷新间隔由主窗体的定时器 `refreshTimer.Interval` 控制(单位:毫秒),默认 5000ms(5 秒)。
如需修改,可在 `MainForm.cs` 中调整,或在 `App.config` 的 `AppSettings` 节点配置后读取。

### 自选股与窗口状态

自选股列表、窗口位置、窗口大小均持久化保存到本地文件:

```
%LocalAppData%\DesktopStock\settings.json
```

使用自定义 JSON 序列化方式读写,无需引入第三方 JSON 库。删除该文件即可重置全部配置。

## 🏗️ 项目结构

```
桌面股市/
├── DesktopStock.sln              # Visual Studio 解决方案
├── logo.png                      # 项目图标
├── README.md                     # 本文档
└── DesktopStock/
    ├── Program.cs                # 程序入口(Main)
    ├── MainForm.cs               # 主窗体:自选股列表、刷新、置顶、透明度
    ├── StockItemPanel.cs         # 单只股票展示面板控件(红涨绿跌)
    ├── StockService.cs           # 行情服务:新浪/腾讯 API 调用与解析
    ├── StockDataStore.cs         # 本地配置读写(settings.json)
    ├── AddStockForm.cs           # 添加股票对话框
    ├── ChartForm.cs              # 分时走势图窗口
    ├── App.config                # 运行时配置
    ├── DesktopStock.csproj       # MSBuild 项目文件
    ├── build.bat / build.ps1     # csc.exe 直接编译 + 运行脚本
    ├── compile.bat               # MSBuild 编译脚本
    ├── logo.ico                  # 应用图标
    └── Properties/
        └── AssemblyInfo.cs       # 程序集信息(版本 1.0.0.0)
```

## 🔌 数据源

本程序仅使用公开的行情接口,不依赖任何付费或私有服务:

| 用途 | 接口 | 说明 |
| --- | --- | --- |
| 实时行情 | 新浪财经 `https://hq.sinajs.cn/list=xxx` | 批量拉取,返回 `var hq_str_xxx="..."` 格式,正则解析 |
| 分时走势 | 腾讯财经分时接口 | 用于 `ChartForm` 绘制当日分时图 |
| 昨收价 | 新浪财经 | 用于计算涨跌额 / 涨跌幅 |

### 股票代码格式

程序通过 `StockService.ToSinaCode` 自动为 6 位 A 股代码加上交易所前缀:

| 代码开头 | 交易所 | 前缀 | 示例 |
| --- | --- | --- | --- |
| `6` / `5` | 上海证券交易所 | `sh` | `sh600000`(浦发银行) |
| `0` / `2` / `3` / `1` | 深圳证券交易所 | `sz` | `sz000001`(平安银行) |
| `8` / `4` | 北京证券交易所 | `bj` | `bj830799` |

> ⚠️ 数据源为公开接口,仅供个人学习与参考,不保证数据的实时性与准确性,请勿用于实盘交易决策。

## 🧰 技术栈

- **语言**:C#
- **框架**:.NET Framework 4.5 / Windows Forms
- **数据解析**:`System.Web.Extensions`(JavaScriptSerializer)、正则表达式
- **网络**:`System.Net.Http` / `WebClient`
- **构建**:MSBuild / csc.exe(支持无 Visual Studio 环境)
- **第三方依赖**:无

## 📋 系统要求

- 操作系统:Windows 7 SP1 及以上
- 运行时:.NET Framework 4.5 及以上(Windows 自带)
- 屏幕:建议分辨率 1366×768 及以上

## 📈 版本

当前版本:**1.0.0.0**(见 `Properties/AssemblyInfo.cs`)

## 🤝 贡献

欢迎通过 Issue / Pull Request 反馈问题与改进建议,例如:

- 适配更多市场(港股、美股、指数、ETF)
- 增加 K 线图、技术指标
- 支持开机自启、系统托盘最小化
- 国际化(多语言)与暗色主题

## 📄 许可证

本项目基于 [MIT License](LICENSE) 开源,可自由使用、修改与分发。

数据版权归各行情接口提供方所有,本项目仅供学习交流使用,不构成任何投资建议。

## ⚖️ 免责声明

本项目仅供学习与技术研究使用,**不提供任何投资建议**。
股市有风险,投资需谨慎。使用本程序所产生的一切后果由使用者自行承担,作者不对任何因使用本程序而造成的直接或间接损失负责。

