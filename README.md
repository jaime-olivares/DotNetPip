# DotNetPip

[![NuGet](https://img.shields.io/nuget/v/DotNetPip.svg)](https://www.nuget.org/packages/DotNetPip/)
[![downloads](https://img.shields.io/nuget/dt/DotNetPip)](https://www.nuget.org/packages/DotNetPip/)
[![github](https://img.shields.io/github/stars/jaime-olivares/DotNetPip?style=flat&color=yellow)](https://github.com/jaime-olivares/DotNetPip)

**Run Python AI & Machine Learning libraries natively inside .NET 10 — zero microservices, zero HTTP overhead, zero configuration friction.**

`DotNetPip` is an ultra-lightweight, in-process interoperability engine that embeds Python directly inside your C# applications. It lets you declare Python packages directly in your `.csproj` file just like standard NuGet packages, restoring and isolating them automatically during build time.

This is still a Proof of Concept. Please read below [how to contribute](#how-to-contribute)

## Installation

```bash
dotnet add package DotNetPip
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

  <!-- Declare your Python runtime. Be sure it matches. -->
  <PropertyGroup>
    <PythonRuntimeVersion>3.11.5</PythonRuntimeVersion>  
  </PropertyGroup>

  <!-- Verify whether the nuget version is up to date -->
  <ItemGroup>
    <PackageReference Include="DotNetPip" Version="1.3.0" />
  </ItemGroup>

  <!-- Declare your Python AI packages. They must match the Python runtime. -->
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
┌────────────────────────────────────────────────────────┐
│                      BUILD TIME                        │
│  .csproj (<PythonPackageReference>)                    │
│    └─► MSBuild Task downloads wheels & provisions env  │
└─────────────────────────────┬──────────────────────────┘
                              │
┌─────────────────────────────▼──────────────────────────┐
│                       RUNTIME                          │
│  C# (.NET 10) Process                                  │
│    └─► Embedded CPython Engine (In-Memory)             │
│          └─► Zero-Copy Data Sharing (Span<T>)          │
└────────────────────────────────────────────────────────┘

```

1. **Build Phase:** `dotnet restore` and `dotnet build` run the same MSBuild task. It resolves `<PythonPackageReference>` items (including transitives) through pip, writes a hash-verified lock to `obj/python.lock.json`, and installs those exact wheels into the output directory. Later restores reuse the lock. `dotnet restore --locked-mode` (or `-p:RestoreLockedMode=true`) fails if the lock would change. Package indexes follow pip (`PIP_INDEX_URL`, `PIP_EXTRA_INDEX_URL`).
2. **Runtime Phase:** At application startup, `PythonEngine` initializes an embedded CPython instance inside the same process memory space, giving you direct, high-speed access to Python libraries.

## TODO: Potential Practical Challenges

While this architectural proposal is highly innovative, executing it comes with real-world engineering hurdles that we will need to navigate:

- The Python Global Interpreter Lock (GIL): Standard CPython is single-threaded for CPU-bound Python bytecode due to the GIL. (Though modern Python versions support free-threaded builds, managing thread safety across .NET multi-threading and CPython state requires careful synchronization).

- Native Binary Distribution: Bundling native Python dynamic libraries (i.e. python311.dll / .so) and ensuring they resolve correctly across Windows, Linux, and macOS deployment targets requires robust publishing scripts.

- ABI stability: CPython’s full C-API/ABI changes between 3.x minor versions, so native extensions (PyTorch, NumPy) must match the embedded interpreter (e.g. a `cp311` wheel with Python 3.11). The Stable ABI (`abi3`) is the opt-in subset that stays compatible across 3.x. CUDA builds add another axis: a wheel must also match the installed CUDA toolkit.

## How to contribute

Contributions are welcome! Here are a few ways you can help support and grow the project:

* :star: **Star the repository:** Giving the project a star helps increase visibility and attract more contributors.
* **Share real-world samples:** Code snippets, configuration examples, or real-life usage scenarios are highly appreciated and will be added to the sample library.
* **Submit Issues & Pull Requests:** Bug reports, feature suggestions, and code updates are always welcome.
* **Spread the word:** Sharing the project on social media or technical networks helps expand our reach.
