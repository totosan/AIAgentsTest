# Project AutoGen Example

## Preparation

The following dependencies are required to set up the development environment:
### Visual Studio Code
- [Visual Studio Code](https://code.visualstudio.com/) (recommended)

### Ollama
- [Ollama](https://ollama.com/)
- download any model, that can work with text and is good at summarization
  - *this project ran with llama3.2*
- standard address & port: http://localhost:11434

### .NET SDK
- [.NET SDK](https://dotnet.microsoft.com/download) (version 8.0 or higher)

### .NET Dependencies
The .NET dependencies are defined in the file 

`multiagent.csproj`

. Here are some of the key dependencies:

- **Agentic system SDK**
  - `AutoGen`
  - `AutoGen.DotnetInteractive`

- **PDF**
  - `itext7`
  - `PdfSharp`
  - `SixLabors.ImageSharp`

- **OCR**
  - `Tesseract`

### Installing Dependencies

#### Install .NET Dependencies
Navigate to the .NET project directory and install the dependencies:

```sh
cd code/CS/multiagent
dotnet restore
```

### Running the Project

#### Run .NET Project
Navigate to the .NET project directory and run the project:

```sh
cd code/CS/multiagent
dotnet run
```
---

## Solution architecture
```mermaid
graph TD
    style A fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style B fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style C fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style D fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style E fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style F fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style G fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style H fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style I fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style J fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style K fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style L fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style M fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style N fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style O fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style P fill:#faa,stroke:#353,stroke-width:1px,rx:5,ry:5
    style FileWatcher fill:#e0e0e0,stroke:#353,stroke-width:1px,rx:10,ry:10
    style AIAgentSystem fill:#e0e0e0,stroke:#353,stroke-width:1px,rx:10,ry:10
    style FileAccess fill:#e0e0e0,stroke:#353,stroke-width:1px,rx:10,ry:10
    style TaskDistribution fill:#e0e0e0,stroke:#353,stroke-width:1px,rx:10,ry:10

    subgraph FileWatcher
        A[Watch Directory]
        B[On File Change]
    end

    subgraph AIAgentSystem
        C[Admin Agent]
        D[PDF Manager Agent]
        E[Summarizer Agent]
        F[Title Extractor Agent]
        G[Title Reviewer Agent]
        H[File Manager Agent]
        I[User Proxy Agent]
    end

    subgraph FileAccess
        J[Read File]
        K[Write File]
        L[Rename File]
        M[Delete File]
    end

    subgraph TaskDistribution
        N[Admin Assigns Tasks]
        O[Agents Perform Tasks]
        P[Admin Reviews Results]
    end

    A --> B
    B --> C
    C --> D
    D --> E
    E --> F
    F --> G
    G --> H
    H --> I
    C --> N
    N --> O
    O --> P
    P --> C
    D --> J
    H --> K
    H --> L
    H --> M
```