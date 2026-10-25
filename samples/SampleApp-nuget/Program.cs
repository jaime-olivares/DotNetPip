using DotNetPip;

// 1. Initialize embedded minimalist CPython runtime
using var py = PythonEngine.CreateMinimalist();

// 2. Import Python AI library natively from C#
dynamic transformers = py.Import("transformers");
dynamic pipeline = transformers.pipeline("sentiment-analysis");

// 3. Execute model inference in-process
dynamic result = pipeline("Evaluating embedded AI performance in .NET 10");
Console.WriteLine(result);
