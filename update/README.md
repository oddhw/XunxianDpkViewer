# 内置更新发布说明

程序会依次读取内置地址和以前清单中学到的 `bootstrapUrls`。因此可以先从
GitHub 或 GitCode 下发新的服务器地址，再逐步迁移下载源。

版本清单和更新包都必须通过内置 RSA 公钥签名校验。即使预留的备用仓库
尚未创建，其他人也无法利用同名仓库向客户端下发可安装程序。私钥不得进入仓库，
发布时通过 `-PrivateKey` 参数或 `XUNXIAN_UPDATE_PRIVATE_KEY` 环境变量传入。

发布前：

1. 在项目文件中更新版本，然后运行 `publish-single.cmd` 生成单文件 EXE。
2. 从 `stable.template.json` 复制出 `stable.json`，填写相同版本、引导地址和下载镜像。`releaseNotes` 可以保持为空。
3. 运行 `tools\SignUpdateManifest.ps1` 生成文件大小、SHA-256 和签名。清单与 EXE 版本不一致时脚本会拒绝签名。
4. 运行 `tools\TestUpdateRelease.ps1`。脚本会实际验证改名启动、签名、覆盖安装和失败回滚。
5. 验收通过后，将同一份 `stable.json` 放到每个引导地址，并将完全相同的已签名 EXE 上传到清单中的下载地址。

```powershell
.\publish-single.cmd
.\tools\SignUpdateManifest.ps1 -Manifest .\update\stable.json -PackagePath .\XunxianDpkViewer.exe -PrivateKey 私钥文件路径
.\tools\TestUpdateRelease.ps1 -Manifest .\update\stable.json -PackagePath .\XunxianDpkViewer.exe
```

## 国内下载镜像

建议把完全相同的 EXE 同时上传到 GitCode Release 和 GitHub Release。清单中的
`packages` 按 `priority` 从小到大尝试，GitCode 国内镜像设为 `10`，GitHub 设为
`100`。不要使用无法长期控制的公共下载代理。

```json
"packages": [
  {
    "url": "这里填写 GitCode Release 返回的附件下载直链",
    "sha256": "",
    "signature": "",
    "size": 0,
    "priority": 10,
    "label": "GitCode 国内镜像"
  },
  {
    "url": "https://github.com/oddhw/XunxianDpkViewer/releases/download/v版本号/XunxianDpkViewer.exe",
    "sha256": "",
    "signature": "",
    "size": 0,
    "priority": 100,
    "label": "GitHub 备用源"
  }
]
```

也可以给签名脚本传入 `-MirrorUrl`，脚本会把该地址作为第一下载源写入清单。

不要删除或公开私钥。丢失私钥后，已经发布的旧版本将无法信任新更新包。
