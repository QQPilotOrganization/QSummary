<div align="center">

```text
   ___    ____                                                         
  / _ \  / ___|   _   _   _ __ ___    _ __ ___     __ _   _ __   _   _ 
 | | | | \___ \  | | | | | '_ ` _ \  | '_ ` _ \   / _` | | '__| | | | |
 | |_| |  ___) | | |_| | | | | | | | | | | | | | | (_| | | |    | |_| |
  \__\_\ |____/   \__,_| |_| |_| |_| |_| |_| |_|  \__,_| |_|     \__, |
                                                                 |___/ 
```

# QSummary

**基于窗口自动化的 QQ 聊天记录总结工具**

[![Release](https://img.shields.io/github/v/release/Na2Cr2O7/QSummary?style=flat-square)](https://github.com/Na2Cr2O7/QSummary/releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](./LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey?style=flat-square)](#)

<img alt="示例截图" src="./assets/index.png" width="400" >

> 💡 **纯视觉 + 窗口自动化 | 零 API 依赖 | 零注入 | 低封号风险**  
> 支持 Ollama 本地部署，数据不出电脑，隐私安全可控。

</div>

---

## ✨ 核心特性

- 🛡️ **安全无痕**：纯视觉识别 + 模拟操作，不 Hook 进程、不注入 DLL，极大降低封号风险。
- 🔒 **隐私至上**：支持完全本地运行（Ollama），聊天记录无需上传云端。
- 🧠 **多模态解析**：支持提取文本、时间戳、用户昵称及本地图片/表情包路径。
- 🔌 **灵活扩展**：兼容任意 OpenAI API 格式的本地或远程大模型。
- 🖥️ **虚拟机友好**：基于浏览器 UI 设计，可在虚拟机运行，实体机查看结果。

---

## 🚀 快速开始

### 1. 环境准备

| 依赖项 | 说明 | 备注 |
| :--- | :--- | :--- |
| **QSummary** | [下载最新 Release](https://github.com/Na2Cr2O7/QSummary/releases) | 解压后 < 35MB |
| **Umi-OCR** | [下载地址](https://github.com/hiroi-sora/Umi-OCR/releases) | 用于 OCR 识别群名称，需开启 HTTP 服务 |
| **.NET Runtime** | [.NET 10 SDK/Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-10.0.203-windows-x64-installer) | 程序运行基础环境 |
| **Ollama** (推荐) | [官网下载](https://ollama.com/) | 本地大模型运行时 |

### 2. 模型配置

推荐使用Ollama ，但是所有OpenAI Chat Completion API 的都支持。
安装 Ollama 后，根据设备性能拉取模型：

```bash
# ☁️ 云端模型（无需拉取，直接在设置中填写 API Key）
gpt-oss:20b-cloud

# 💻 本地推荐模型（20B，性能与效果平衡）
ollama pull qwen2.5:20b

# ⚡ 低配设备可选（0.5B，轻量快速）
ollama pull qwen2.5:0.5b

# 👁️ 视觉多模态模型
ollama pull minicpm-v4.6:1b

#词嵌入模型
ollama pull qwen3-embedding:0.6b
```

### 3. 初始化与运行

1.  启动 `QsummaryUI.exe`，访问 [http://localhost:8080](http://localhost:8080/) 进行设置。
    -   **Ollama 用户**：服务器地址直接填写 `Ollama` (默认端口)。
    -   **Umi-OCR**：确保在 Umi-OCR 设置中已勾选“允许 HTTP 服务”。
    -   **需要词嵌入模型才能使用RAG哦**
2.  打开 QQ 并将其置于前台。
![alt text](image.png)
3.  运行 `QsummaryCore.exe` 开始自动监听与总结。
> ⚠️ **重要警告**：运行期间 **严禁** 更改屏幕分辨率，鼠标位置和 DPI 缩放比例！

---

## ⚙️ QQ 推荐设置

为确保识别准确率，请将 QQ 调整为以下状态：

| 设置项 | 推荐值 | 原因 |
| :--- | :--- | :--- |
| **发送消息快捷键** | `Ctrl + Enter` | 避免回车键误触发送 |
| **联系人面板宽度** | 拖至 **最窄** | 统一坐标定位基准 |
| **系统显示缩放** | 100% 或 125% | 避免高 DPI 导致坐标偏移 |
| **聊天背景** | 默认白色 | 提升 OCR 识别对比度 |

---

## 🔧 工作原理

1.  **窗口置顶**：通过 `FocusqqWindow.dll` 强制 QQ 主窗口置顶，确保截图一致性。
2.  **DPI 自适应**：`ScaleToINI.exe` 自动检测系统缩放并写入 `config.ini` 校准坐标。
3.  **未读检测**：扫描联系人列表“小红点”，定位新消息会话。
4.  **自动交互**：模拟点击红点位置，打开对应聊天窗口。
5.  **内容提取**：框选识别聊天区域，解析为结构化 Markdown：
    ```markdown
    Username: 11-01 08:12:19
    
    这是一条包含图片的消息...

    UserB: 11-25 08:10:36
    这是一条纯文本消息
    ```
6.  **循环监听**：处理完成后自动关闭窗口，返回主界面继续下一轮检测。

---

## 💻 系统要求

> ⚠️ **注意**：本程序依赖图形界面，**不支持**无头模式（Headless）、远程桌面或纯命令行服务器。

| 配置等级 | CPU | 内存 | 存储 | 显示器 | 系统版本 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **最低配置** | 单核 1GHz+ | 1 GB | 1 GB | 1920×1080 | Win8.1 x64+ |
| **推荐配置** | 4核 2GHz+ | 4 GB | 8 GB (含模型) | 1920×1080 | Win10/11 x64 |

-   **GPU 加速**：推荐使用 NVIDIA CUDA 显卡以加速本地模型推理。
-   **Windows 7 用户**：请尝试安装 [VkKex](https://github.com/YuZhouRen86/VxKex-NEXT) 兼容层。

---

## 🛠️ 开发者指南

使用 **Visual Studio 2026** 打开解决方案进行编译：
-   `QQPilot4\QQPilot4.slnx`

---

## 🛡️ 免责声明

本项目 **仅限技术学习与研究用途**。严禁用于自动骚扰、刷屏、诈骗等恶意行为或任何违反《QQ 软件许可协议》的场景。

使用者须自行承担因使用本软件引发的一切法律责任，作者概不负责。

---

<div align="center">

**如果这个项目对你有帮助，请点亮 ⭐ Star 支持一下！**
