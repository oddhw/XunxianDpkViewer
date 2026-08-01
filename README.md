# 寻仙 DPK 资源浏览器

一个基于 WinUI 3 的《新寻仙》客户端资源浏览与导出工具。

- 当前版本：2.1
- 作者：黑风岭-梵心似火
- 系统：Windows x64

## 直接使用

从 GitHub Releases 下载单文件版 `XunxianDpkViewer.exe`，无需安装 .NET，也无需把其他 DLL 放在程序旁边。

首次启动时选择《新寻仙》安装目录或其中的 `res` 目录。之后可以通过软件顶部的“设置”查看和更换资源路径。

## 主要功能

- 自动读取客户端 `res` 目录中的全部 DPK。
- 按 DPK 内的真实文件夹结构浏览资源。
- 预览 PNG、JPG、DDS 等图像与贴图。
- 试听 OGG、WAV 音乐和音效。
- 解析 PMF 模型并关联 CCT、CMF 与 DDS 材质。
- 支持贴图、实体、线框三种模型预览方式。
- 自动组合一个对象目录中的多个 PMF 部件，同时保留单独部件查看。
- 将单个 PMF 部件或完整组合模型导出为 Wavefront OBJ，并在旁边生成 MTL 与 DDS 贴图。
- 单选、多选或按当前查询结果批量导出资源，并保留原始目录结构。
- XML、CCT、CMF 等文本配置可展开查看格式化原文。
- 文件属性窗口显示格式、大小、DPK、内部路径、索引块和 SHA-256。

## 构建

需要 .NET 10 SDK，目标平台为 Windows x64。

```powershell
dotnet restore .\XunxianDpkViewer.csproj
dotnet build .\XunxianDpkViewer.csproj -c Release -p:Platform=x64
```

生成自包含单文件版本：

```powershell
.\publish-single.cmd
```

发布后的程序位于仓库根目录：

```text
XunxianDpkViewer.exe
```

## 自检

安装了《新寻仙》客户端的电脑可执行：

```powershell
.\XunxianDpkViewer.exe --self-test
```

结果写入：

```text
%LOCALAPPDATA%\XunxianDpkViewer\self-test.log
```

## 格式说明

DPK/WHSC 解码使用客户端实际密钥与块链格式。模型预览目前覆盖常见 PMF 静态网格、多 UV、颜色、骨骼权重顶点声明、CCT/CMF 材质链和 DDS 贴图；客户端专用着色器、完整骨骼动画及部分引擎内部配置仍在继续解析。
