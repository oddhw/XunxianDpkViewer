# 内置更新发布说明

程序会依次读取内置地址和以前清单中学到的 `bootstrapUrls`。因此可以先从
GitHub 或 GitCode 下发新的服务器地址，再逐步迁移下载源。

版本清单和更新包都必须通过内置 RSA 公钥签名校验。即使预留的备用仓库
尚未创建，其他人也无法利用同名仓库向客户端下发可安装程序。私钥不得进入仓库，
发布时通过 `-PrivateKey` 参数或 `XUNXIAN_UPDATE_PRIVATE_KEY` 环境变量传入。

发布前：

1. 从 `stable.template.json` 复制出 `stable.json`。
2. 填写版本、更新说明、引导地址和下载镜像。
3. 运行 `tools\SignUpdateManifest.ps1` 生成文件大小、SHA-256 和签名。
4. 将同一份 `stable.json` 放到每个引导地址。
5. 将完全相同的已签名 EXE 放到清单中的下载地址。

不要删除或公开私钥。丢失私钥后，已经发布的旧版本将无法信任新更新包。
