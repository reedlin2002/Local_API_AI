# Local_API_AI

[![CI](https://github.com/reedlin2002/Local_API_AI/actions/workflows/ci.yml/badge.svg)](https://github.com/reedlin2002/Local_API_AI/actions/workflows/ci.yml)
[![Container image](https://github.com/reedlin2002/Local_API_AI/actions/workflows/container.yml/badge.svg)](https://github.com/reedlin2002/Local_API_AI/actions/workflows/container.yml)
[![GitHub Pages](https://github.com/reedlin2002/Local_API_AI/actions/workflows/pages.yml/badge.svg)](https://reedlin2002.github.io/Local_API_AI/)

LocalAIAgentAPI 是一套在**自己電腦上執行**的 AI API：圖片分類、OCR、文字生成、圖像描述，以及會自己呼叫工具的 Agent。資料不會離開你的電腦，可以離線、在封閉網路中使用。

**線上介面：** https://reedlin2002.github.io/Local_API_AI/
（介面放在 GitHub Pages，AI 運算仍在你本機的 API 上執行，請先依下方步驟啟動 API。）

![前端](docs/frontend.png)

## 功能

| 功能 | 端點 | 說明 |
| --- | --- | --- |
| 圖片分類 | `POST /api/ai`（`model=imageclassifier`） | ONNX ResNet50，回傳前 5 名 ImageNet 類別與機率 |
| 批次分類 | `POST /api/ai/batch-classify` | 一次上傳多張（`files`），壞檔會單獨標示錯誤 |
| OCR | `POST /api/ai/ocr` | Tesseract，英文 + 繁體中文 |
| 文字生成 | `POST /api/ai`（`model=textgeneration`） | 本機 Ollama 文字模型 |
| 圖像描述 | `POST /api/ai/describe-image` | 有 Ollama 視覺模型（如 `llava`）時直接看圖描述；沒有時改用分類結果請文字模型描述，並在 `method` 標明 |
| Agent | `POST /api/ai/ask` | 可附一張圖片；模型會以 tool calling 呼叫 OCR / 分類再回答。模型不支援或沒有呼叫工具時，伺服器會先跑工具再把結果交給模型，回答一定根據圖片內容 |
| 健康檢查 | `GET /health` | |

所有 `POST` 端點都接受 `?outputFormat=json`（預設）或 `?outputFormat=text`。錯誤一律回傳 [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `application/problem+json`，不會洩漏堆疊資訊。Swagger 文件在 `/swagger`。

## 快速開始

### 方式一：Docker（推薦）

```bash
docker run --rm -p 127.0.0.1:5136:8080 ghcr.io/reedlin2002/local_api_ai:latest
```

打開 http://localhost:5136 （或 [GitHub Pages 介面](https://reedlin2002.github.io/Local_API_AI/)）。
`127.0.0.1:` 讓 API 只對本機開放，不會暴露到區域網路。Linux 上若要連到主機的 Ollama，再加 `--add-host=host.docker.internal:host-gateway`。

### 方式二：Windows 免安裝版

到 [Releases](https://github.com/reedlin2002/Local_API_AI/releases) 下載 `LocalAIAgentAPI-win-x64.zip`，解壓後執行 `LocalAIAgentAPI.exe`，打開 http://localhost:5136 。

### 方式三：從原始碼執行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（Windows：`winget install Microsoft.DotNet.SDK.10`）。

```bash
dotnet run
dotnet test   # 執行測試
```

### 文字生成 / Agent / 圖像描述需要 Ollama

```bash
ollama pull llama2    # 文字生成
ollama pull mistral   # Agent（支援 tool calling）
ollama pull llava     # 圖像描述（選用；沒有的話會自動改用分類結果描述）
```

## 設定

在 `appsettings.json` 調整，或用環境變數覆寫（例如 `Ollama__AgentModel=qwen2.5`）：

| 設定 | 預設 | 說明 |
| --- | --- | --- |
| `Ollama:BaseUrl` | `http://localhost:11434` | Docker 映像內預設為 `http://host.docker.internal:11434` |
| `Ollama:TextModel` / `AgentModel` / `VisionModel` | `llama2` / `mistral` / `llava` | |
| `Ollama:TimeoutSeconds` | `180` | 超過回傳 504 |
| `Cors:AllowedOrigins` | GitHub Pages 網址 | **只有列在這裡的網站能從瀏覽器呼叫 API**，避免你瀏覽的任意網站偷用本機模型 |
| `Upload:MaxBytes` | 10 MB | 上傳大小上限 |

## 架構

```
Controllers/AIController.cs        路由、輸入驗證、輸出格式
Services/ImageClassifier.cs        ONNX ResNet50（Singleton，模型只載入一次）+ SkiaSharp 前處理
Services/OcrService.cs             Tesseract（引擎物件池，避免每次重新載入語言檔）
Services/OllamaClient.cs           Ollama REST API（typed HttpClient，錯誤轉成清楚訊息）
Services/ImageDescriber.cs         視覺模型描述，缺模型時退回分類 + 文字模型
Services/AgentService.cs           tool-calling 迴圈
Infrastructure/                    設定、例外 → ProblemDetails
wwwroot/index.html                 前端（同時部署到 GitHub Pages）
tests/LocalAIAgentAPI.Tests/       xUnit 整合 + 單元測試（Ollama 以假物件取代）
```

## CI/CD

| Workflow | 觸發 | 內容 |
| --- | --- | --- |
| `ci.yml` | push / PR | 在 Linux 建置並執行全部測試（含真實 ONNX 推論與 OCR） |
| `container.yml` | push main / tag | 建置 Docker 映像 → 啟動容器跑 `scripts/smoke-test.sh` → 推到 GHCR（`latest`、`sha-…`、版本號） |
| `pages.yml` | `wwwroot/` 變更 | 部署前端到 GitHub Pages |
| `release.yml` | tag `v*` | 打包 Windows 免安裝版並建立 GitHub Release |

全部只用 GitHub 內建的 `GITHUB_TOKEN`，**不需要設定任何 secret**。發版：`git tag v1.2.0 && git push origin v1.2.0`。

## 授權與模型來源

- 圖片分類模型：[ONNX Model Zoo – ResNet50 v2](https://github.com/onnx/models)
- OCR 語言檔：[tesseract-ocr/tessdata](https://github.com/tesseract-ocr/tessdata)
