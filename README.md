
## 🌟 Overview

**Dynamic Mock Server Studio** is an ASP.NET Core 10 Web API application designed for frontend developers, QA engineers, and backend teams. Instead of waiting for backend services to be built or writing boilerplate mock handlers, you can upload an **Excel spreadsheet (`.xlsx`)**, define route paths and input filter conditions, and have a fully functional C# API live in **Swagger UI** in milliseconds.

The server uses **Microsoft Roslyn** to compile C# controllers in-memory and dynamically injects them into ASP.NET Core's routing pipeline and Swagger schema at runtime.

---

## ✨ Key Features

- ⚡ **Zero-Restart Runtime Compilation**: Compiles dynamic C# controllers in-memory with Roslyn (`Microsoft.CodeAnalysis.CSharp`) and injects them into ASP.NET Core's `ApplicationPartManager`.
- 🔄 **Real-Time Swagger Hot-Reload**: Automatically triggers `IActionDescriptorChangeProvider` so new routes and schemas immediately appear in Swagger UI without restarting the application.
- 📊 **Excel Dataset Extraction**: Parses `.xlsx` workbooks (1,000+ rows) via ClosedXML, automatically converting types (`int`, `decimal`, `double`, `bool`, `DateTime`, `string`).
- 🔍 **Dynamic Request Inputs & Filtering**:
  - Define query parameters (`GET`/`DELETE`) or JSON request body (`POST`/`PUT`).
  - Supports condition operators: **`Equals (==)`**, **`Contains (Text)`**, **`Starts With`**, **`Greater Than (>)`**, **`Less Than (<)`**, etc.
  - Case-insensitive & space-resilient property mapping (e.g., Excel header `"Task Name"` seamlessly maps to DTO property `TaskName`).
- 🎯 **Configurable Response Formats**:
  - **Single Object** (e.g. `GetById`/`Info` endpoints): Returns `{ ... }` directly (or `404 Not Found`).
  - **Paginated List** (Default): Returns `{ totalRecords, currentPage, pageSize, data: [...] }`.
  - **Direct Array**: Returns raw array `[ { ... } ]`.
- 🖥️ **Modern Web Dashboard**: Single-page studio interface located at `/` built with Tailwind CSS, offering a visual column builder, input filter generator, Excel drag-and-drop, and generated code viewer.
- 💾 **Startup Persistence**: Scans `GeneratedApis/` on application startup to restore and compile previously generated APIs automatically.
- 🐳 **Docker Ready**: Pre-configured with a multi-stage `Dockerfile` for containerized environments.

---

## 🏗️ Architecture & Data Flow

```mermaid
flowchart TD
    User([User / Browser Dashboard]) -->|1. Upload .xlsx & define schema/filters| AddApi["POST /api/generator/addApi"]
    AddApi --> Parser["ExcelParserUtility\n(ClosedXML)"]
    Parser --> DataStore[("JSON Dataset\n(GeneratedApis/data_*.json)")]
    
    AddApi --> CodeGen["RuntimeApiCompilerService\n(Generates C# DTO & Controller)"]
    CodeGen --> Roslyn["Roslyn CSharpCompilation\n(In-Memory Assembly)"]
    
    Roslyn --> PartMgr["ASP.NET Core\nApplicationPartManager"]
    PartMgr --> Notifier["DynamicActionDescriptorChangeProvider\n(NotifyChanges)"]
    
    Notifier --> Swagger["Swagger UI & Route Table\n(Hot-Reloaded)"]
    
    Client([Client / Frontend App]) -->|2. Call Generated Route\ne.g., GET /api/products?productId=101| DynamicController["Dynamic In-Memory Controller"]
    DynamicController -->|3. Filter with LINQ| DataStore
    DynamicController -->|4. Return Filtered Result| Client






## 📖 How to Use

### Step 1: Open the Studio Dashboard
Navigate to `http://localhost:5292` in your browser.

### Step 2: Configure the Endpoint
1. **API Name**: Enter a descriptive name (e.g. `GetProductInfo` or `CustomerSearch`).
2. **Route Path**: Set your REST route (e.g. `api/products/GetProductInfo`).
3. **HTTP Method**: Choose `GET`, `POST`, `PUT`, or `DELETE`.
4. **Response Format**: Select **Single Object** (first match / 404), **Paginated List**, or **Direct Array**.

### Step 3: Define Response Columns (Excel Headers)
Add the columns that match row 1 of your Excel workbook:
- `productID` (int)
- `name` (string)
- `price` (decimal)
- `category` (string)

### Step 4: Configure Input Parameters & Conditions
Add parameters that callers can pass to filter data:
- **Parameter Name**: `productId`
- **Data Type**: `int`
- **Target Excel Column**: `productID`
- **Condition**: `Equals (==)`
- **Required?**: Unchecked (optional filter)

*(Tip: Click **⚡ Auto-Create Filter from Columns** to generate filters automatically for all columns!)*

### Step 5: Upload Excel Workbook & Deploy
1. Upload your `.xlsx` spreadsheet.
2. Click **🚀 Generate & Deploy Dynamic Mock API**.
3. The API compiles and registers immediately — click **Inspect in Swagger UI** to test it right away!

---

## 💡 Example Endpoints

### Single Object Filtering (`GetProductInfo`)
**Request:**
```http
GET /api/products/GetProductInfo?productId=101 HTTP/1.1
Host: localhost:5292
```

**Response (200 OK):**
```json
{
  "productID": 101,
  "name": "Ultra Gaming Laptop",
  "price": 1899.99,
  "category": "Computers"
}
```

**Non-existent item (`productId=999`):**
```json
HTTP/1.1 404 Not Found
{
  "message": "No matching record found."
}
```

---

### Paginated List Filtering (`SearchProducts`)
**Request:**
```http
GET /api/products/search?category=Electronics&page=1&pageSize=10 HTTP/1.1
Host: localhost:5292
```

**Response (200 OK):**
```json
{
  "totalRecords": 24,
  "currentPage": 1,
  "pageSize": 10,
  "data": [
    {
      "productID": 101,
      "name": "Wireless Headphones",
      "price": 89.99,
      "category": "Electronics"
    }
  ]
}
```

---

## 🛠️ API Generator Endpoint Reference

You can also automate mock API creation via `curl` or automated CI scripts:

### `POST /api/generator/addApi`
**Content-Type**: `multipart/form-data`

| Form Field | Type | Description |
| :--- | :--- | :--- |
| `ApiName` | `string` | Safe C# identifier prefix (e.g. `GetProducts`) |
| `Route` | `string` | Target route path (e.g. `api/store/products`) |
| `HttpMethod` | `string` | `GET`, `POST`, `PUT`, or `DELETE` |
| `ResponseFormat` | `string` | `SingleObject`, `PaginatedList`, or `DirectArray` |
| `ResponsePropertiesJson`| `string (JSON)` | Object mapping columns to types: `{"id":"int","name":"string"}` |
| `RequestParametersJson` | `string (JSON)` | Array of filter definitions: `[{"Name":"id","Type":"int","TargetProperty":"id","MatchMode":"Equals"}]` |
| `ExcelFile` | `file (.xlsx)` | Excel binary spreadsheet with data rows |

---
