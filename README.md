# VisionWorkbench License Issuer

VisionWorkbench License Issuer 是一个独立的 Windows WPF 离线许可证签发工具。它不依赖 VisionWorkbench 主程序项目，通过约定的 JSON + ECDSA P-256 协议生成 `.vwlicense` 文件。

## 重要的密钥说明

许可证签发依赖一对 ECDSA P-256 密钥：

- 客户端只内置公钥，用于验证许可证签名。
- 签发工具使用私钥生成许可证签名。
- 私钥绝不能提交到 GitHub、发送给客户或放入安装包。
- 本仓库只包含工具源码，不包含任何生产签发私钥。

官方发布版本使用官方签发密钥。其他开发者如果要运行自己的签发流程，需要创建自己的密钥对，并在自己编译的 VisionWorkbench 客户端中配置对应公钥。否则，使用新生成私钥签发的许可证无法被官方客户端验证，这是预期的安全行为。

## 使用流程

1. 编译并启动许可证工具。
2. 首次签发时，工具会在当前 Windows 用户的本地应用数据目录生成签发私钥，并保存对应公钥。
3. 从以下文件读取自建签发公钥：

   `%LOCALAPPDATA%\VisionWorkbenchLicenseIssuer\license-public-key.txt`

4. 在自己维护的 VisionWorkbench 源码中，将 `LicenseService.OfficialPublicKey` 替换为该公钥并重新编译客户端。
5. 在客户端“许可证管理”中复制完整申请码。
6. 将申请码粘贴到许可证工具，选择有效期和授权模块。
7. 点击生成许可证，并将生成的 `.vwlicense` 文件导入对应客户端。

## 授权模块

工具支持按数据与模型模块签发许可证，包括：

- AI 目标检测
- AI 语义分割
- AI 实例分割
- AI 关键点检测
- AI 行为识别
- AI 文本识别
- AI 条码识别
- AI 二维码识别

选择“全部模块”会写入 `module.all`。取消全部模块后，可以单独勾选需要开放的模块。

## 环境要求

- Windows 10 或 Windows 11
- .NET 10 SDK
- Visual Studio 2022、Rider 或 VS Code

## 构建和运行

```powershell
dotnet restore VisionWorkbenchLicenseIssuer.csproj
dotnet build VisionWorkbenchLicenseIssuer.csproj -c Release
dotnet run --project VisionWorkbenchLicenseIssuer.csproj
```

运行数据目录：

`%LOCALAPPDATA%\VisionWorkbenchLicenseIssuer`

许可证默认输出到 `D:\`；如果没有 D 盘，则输出到运行数据目录下的 `issued-licenses`。

## 安全注意事项

- 不要提交 `license-signing-private.pem`。
- 不要提交生成的 `.vwlicense` 文件、客户申请码或设备指纹。
- 生产签发私钥应使用访问控制、离线备份和权限审计保护。
- 如果私钥泄露，应立即更换签发密钥，并发布使用新公钥构建的客户端版本。
- 第三方模型、相机 SDK 和插件的许可证不因本项目开源而自动改变。

## 项目链接

- GitHub：<https://github.com/guanhaiye/VisionWorkbenchLicenseIssuer>
- 主程序：<https://github.com/guanhaiye/VisionWorkBench>
- Gitee：<https://gitee.com/yzbnb/vision-workbench-license-issuer>
