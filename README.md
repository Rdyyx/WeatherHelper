**一个天气查询助手，更多功能正在完善中**  

## 编译打包
### 环境要求
安装 [.NET SDK](https://dotnet.microsoft.com/zh-cn/download)

### 打包步骤
1. 克隆代码到本地
2. 打开终端，进入项目 src 目录
3. 执行发布命令：
```powershell
dotnet publish -c Release --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ../dist

