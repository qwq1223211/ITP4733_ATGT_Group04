# Unity Agent Bridge（com.local.unity-agent）

Unity 2022.3 编辑器侧最小 Agent 桥：本机 TCP + NDJSON，供 DeepSeek Harness（DSH）驱动编辑器。

## 装进 Unity 2022.3 工程

1. 把整个文件夹 `com.local.unity-agent` 复制到目标工程的 `Packages/` 下，例如：
   `YourProject/Packages/com.local.unity-agent/`
2. 用 Unity 2022.3 打开工程；包会作为嵌入式 UPM 包自动加载（无需改 `manifest.json`）。
3. 确认 `Packages/com.local.unity-agent/package.json` 的 `"unity": "2022.3"`。

不要装到批处理自检以外的无关目录；也不要改用户真实业务工程以外的工程结构。

## server.json 在哪

非批处理模式下，编辑器启动后会监听 `127.0.0.1` 随机端口，并把连接信息写到：

```
<project>/Library/UnityAgent/server.json
```

字段示例：`version`、`port`、`token`、`pid`、`projectPath`、`unityVersion`、`startedAt`。

批处理（`Application.isBatchMode`）下**不会**监听端口，也**不会**写 `server.json`，避免污染 CI/自检。

## DSH 如何连接

1. 按 Unity 工程根路径读取 `Library/UnityAgent/server.json`。
2. 用文件里的 `pid` 确认编辑器进程仍在。
3. TCP 连接 `127.0.0.1:<port>`，每行一条 NDJSON 请求，例如：
   `{"id":"r-1","cmd":"ping","args":{},"token":"<server.json 的 token>"}`
4. 读回一行响应：`{"id":"r-1","ok":true,"data":{...}}` 或带 `error` 的失败信封。

域重载后端口会变，客户端需重新读 `server.json`。

## 自检

```text
Unity.exe -batchmode -quit -projectPath <scratch> -executeMethod Local.UnityAgent.SelfTest.Run -logFile <path>
```

日志中应出现 `SELFTEST {"ok":true,...}`，进程退出码 0。
