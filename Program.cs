using FilamentSharp;

Console.WriteLine($"Filament version: {NativeInfo.FilamentVersion}");
Console.WriteLine($"ABI version:      {NativeInfo.AbiVersion}");

using var engine = Engine.Create(Backend.Noop);
using var renderer = engine.CreateRenderer();

Console.WriteLine($"Active backend:   {engine.ActiveBackend}");
Console.WriteLine("FilamentSharp loaded successfully.");