# Local_API_AI



LocalAIAgentAPI 是一套本地部署的 AI 推理平台，支援多種智慧任務，包括圖像分類、OCR 文字辨識、圖像描述生成、文字生成與多格式輸出。可脫離雲端，直接在本地執行，提供高隱私、高效能且可擴展的 AI API 解決方案，適合企業內部工具整合、嵌入式裝置、無網路環境等場景。



----------------------------



## 功能

| 功能 | 說明 |
| ------ | -----------------|
| 圖像分類     | 上傳圖片，自動辨識主題並回傳分類標籤（使用 ONNX 模型 `resnet50-v2-7.onnx`） |
| 圖像描述生成    | 自動為圖片生成自然語言描述（支援英文）                                 |
| OCR 文字辨識  | 支援中英文圖片中文字辨識，整合 Tesseract OCR 引擎                    |
| 文字生成（LLM） | 整合本地 LLM（如 Ollama），進行文字問答、創作等任務                     |
| 多格式輸出     | 回應格式可選擇純文字或 JSON，便於串接前後端或命令列工具                      |



----------------------------



###  🛠️ 技術架構

* .NET 8 為基礎 Web API 框架，使用 Minimal API 寫法

* ONNX Runtime 加速推論，支援跨平台部署（已內建所有 native lib）

* Tesseract OCR 提供準確的圖片文字辨識

* 依賴注入 DI 架構：所有服務統一註冊與解耦

* Swagger 自動化 API 文件與測試介面

* 中介軟體 Middleware：提供全域錯誤攔截與記錄

* 內建前端 index.html：可快速測試各項 AI 功能



----------------------------



### 🔒 安全性與隱私

* 所有 AI 處理皆在本地進行，無資料上傳雲端

* 適合需要保密性與資料自主權的企業內部使用場景

* 可離線部署於封閉網路環境



----------------------------



### 🚀 效能與體驗

* ONNX 模型 + 本地 OCR，不需等待雲端延遲

* 快速響應，低記憶體需求

* 可擴充其他 AI 模型，只需實作 `IAIService` 介面即可



----------------------------



### 💰 成本與部署

| 項目      | 狀態                       |
| ----- | -----------------|
| ✅ 零雲端費用 | 全程本地推論、無需訂閱雲端 API        |
| ✅ 輕量化部署 | 單機執行 `.exe` 即可啟動         |
| ✅ 跨平台支援 | 支援 Windows / Linux / Mac |



----------------------------



🌱 環境影響

本專案主打輕量部署，不需 GPU、無需連網、不需第三方平台授權，能大幅降低碳足跡，提升 AI 使用的永續性與成本效益。



----------------------------



### 📂 專案結構

```bash

LocalAIAgentAPI/

├─ index.html                 # 測試介面（支援模型選擇）

├─ Controllers/AIController   # API 路由與邏輯

├─ Services/                 # 模型服務實作（Image, OCR, LLM）

├─ Middlewares/ErrorHandlingMiddleware.cs

├─ Models/resnet50-v2-7.onnx

├─ tessdata/                 # OCR 語言包

├─ .github/workflows/dotnet-ci.yml

└─ README.md

```



----------------------------



### 🧪 快速測試



1.啟動後端：

```bash

dotnet run

```

2.開啟 `http://localhost:5136/index.html`，選擇模型功能，並上傳圖片或輸入文字



3.若使用 API 端點，可參考 Swagger 文件：

```bash

http://localhost:5136/swagger

```



----------------------------



### 🧠 API 端點



| 功能       | 路徑                       | 方法   | 備註                    |
| -------- | ------------------------ | ---- | --------------------- |
| Image 分類 | `/api/ai`                | POST | model=imageclassifier |
| OCR      | `/api/ai/ocr`            | POST | 支援中英文圖片               |
| 圖像描述生成   | `/api/ai/describe-image` | POST | 英文自然語言描述              |
| LLM 問答   | `/api/ai/ask`            | POST | prompt 與 output 格式    |



----------------------------



### 🧰 CI/CD

內建 GitHub Actions：

* dotnet build

* dotnet test（如需擴充）

* 自動化部署可加上 publish 參數



----------------------------



### 🔧 延伸與自定義

新增 AI 模型？→ 實作一個 `IAIService`，加到 `AIServiceFactory`



想支援其他格式？→ 修改前端 `index.html` 與 Controller 回傳格式即可



支援多語言 OCR？→ 加入 `.traineddata` 到 `/tessdata/`

