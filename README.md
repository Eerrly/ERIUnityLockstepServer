# ERIUnityLockstepServer
***帧同步C#控制台服务器DEMO***

### 介绍
+ 登录、房间等业务逻辑，使用TCP通信
+ 战斗、校验等战斗逻辑，使用基于kcp2k的KCP通信[^kcp2k]
+ 通讯数据使用Google的ProtoBuf[^google.protobuf]

### 设置
+ 端口号设置在 `NetCore/NetSetting.cs`
+ 最大帧缓存数量、一帧的毫秒数在 `Battle/BattleSetting.cs`
+ ProtoBuf生成工具在 `ProtoGen/protogen.bat` 双击运行
+ 日志打印在 `Runtime/LogManager.cs` 最上面，把宏开启就可以得到全量打印
+ 主入口 `Program.cs`

### 运行
`dotnet run`

### 引用
[^kcp2k]:kcp2k - <https://github.com/MirrorNetworking/kcp2k>
[^google.protobuf]:google.protobuf - <https://github.com/google/protobuf>
