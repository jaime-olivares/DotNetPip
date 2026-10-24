# DotNetPip

[![NuGet](https://img.shields.io/nuget/v/DotNetPip.svg)](https://www.nuget.org/packages/DotNetPip/)
[![downloads](https://img.shields.io/nuget/dt/DotNetPip)](https://www.nuget.org/packages/DotNetPip/)
[![github](https://img.shields.io/github/stars/jaime-olivares/DotNetPip?style=flat&color=yellow)](https://github.com/jaime-olivares/DotNetPip)

**Run Python AI & Machine Learning models natively inside .NET 10 — zero microservices, zero HTTP overhead, zero configuration friction.**

`DotNetPip` is an ultra-lightweight, in-process interoperability engine that embeds Python directly inside your C# applications. It lets you declare Python packages directly in your `.csproj` file just like standard NuGet packages, restoring and isolating them automatically during build time.

## Installation

```bash
dotnet add package DotNetPip --version 1.0.0
```

Published on [nuget.org](https://www.nuget.org/packages/DotNetPip/).

## Key Features

* **In-Process Performance:** Runs CPython embedded directly inside your .NET process via low-level native bindings. Eliminates gRPC/HTTP latency and extra server deployments.
* **Declarative Package Management:** Manage Python dependencies inside your C# `.csproj` using `<PythonPackageReference>`. MSBuild automatically fetches and isolates wheels during `dotnet build`.
* **Zero-Copy Memory Access:** Share large matrices, vectors, and PyTorch/NumPy tensors between C# and Python without copying bytes, using `Span<T>` and the Python Buffer Protocol.
* **Natural C# Dynamic Syntax:** Access any Python module, function, or class fluidly using C# `dynamic` types—no static wrappers or generated glue code required.

## Quick Start

### 1. Declare Python packages in your `.csproj`

Add `DotNetPip` and your required Python packages directly to your project file:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <!-- Add DotNetPip -->
  <ItemGroup>
    <PackageReference Include="DotNetPip" Version="1.0.0" />
  </ItemGroup>

  <!-- Declare your Python AI packages -->
  <ItemGroup>
    <PythonPackageReference Include="transformers" Version="4.35.0" />
    <PythonPackageReference Include="torch" Version="2.1.0" />
  </ItemGroup>

</Project>

```

### 2. Run Python models from C#

```csharp
using DotNetPip;

// 1. Initialize the embedded Python engine
using var py = PythonEngine.CreateMinimalist();

// 2. Import Python AI libraries directly
dynamic transformers = py.Import("transformers");
dynamic pipeline = transformers.pipeline("sentiment-analysis");

// 3. Execute inference in-process
dynamic result = pipeline("Evaluating embedded AI performance in .NET 10");

Console.WriteLine(result);

```

## How It Works

```
┌───────────────────────────────────────────────────────────┐
│                      BUILD TIME                           │
│  .csproj (<PythonPackageReference>)                       │
│    └─► MSBuild Task downloads wheels & provisions env     │
└─────────────────────────────┬─────────────────────────────┘
                              │
┌─────────────────────────────▼─────────────────────────────┐
│                      RUNTIME                              │
│  C# (.NET 10) Process                                     │
│    └─► Embedded CPython Engine (In-Memory)                │
│          └─► Zero-Copy Data Sharing (Span<T>)             │
└───────────────────────────────────────────────────────────┘

```

1. **Build Phase:** When you run `dotnet build`, the `DotNetPip` MSBuild task inspects your `<PythonPackageReference>` items, fetches the wheels, and prepares a isolated local environment in your output directory.
2. **Runtime Phase:** At application startup, `PythonEngine` initializes an embedded CPython instance inside the same process memory space, giving you direct, high-speed access to Python libraries.

## TODO: Potential Practical Challenges

While the architectural vision is highly innovative, executing it comes with real-world engineering hurdles that we will need to navigate:

- The Python Global Interpreter Lock (GIL): Standard CPython is single-threaded for CPU-bound Python bytecode due to the GIL. (Though modern Python versions support free-threaded builds, managing thread safety across .NET multi-threading and CPython state requires careful synchronization).

- Native Binary Distribution: Bundling native Python dynamic libraries (python311.dll / .so) and ensuring they resolve correctly across Windows, Linux, and macOS deployment targets requires robust publishing scripts.

- ABI Stability: CPython's C-API is stable across minor versions, but major version changes or third-party C-extensions (like PyTorch compiled with specific CUDA toolkits) can occasionally introduce binary compatibility edge cases.

## Building the NuGet package

The package version comes from `<Version>` in `DotNetPip/DotNetPip.csproj`.

```bash
./scripts/pack.sh          # macOS / Linux
./scripts/pack.ps1         # Windows / PowerShell
```

Packages are written to `artifacts/`. Publish to nuget.org as **jaime_olivares**:

```bash
export NUGET_API_KEY=<your-key>
./scripts/push.sh
```

Tag a release (for example `v1.0.0`) to pack and push from GitHub Actions (secret name: `NUGET_API_KEY`). The nupkg version is still the one in `DotNetPip.csproj`.
