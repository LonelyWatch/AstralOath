using AstralOath.Config;
using Luban;

if (args.Length != 1) throw new ArgumentException("Provide the Luban binary output directory.");
var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(Path.Combine(args[0], file + ".bytes"))));
var demo = tables.Ui[1];
if (demo.DisplaySeconds != 5 || demo.TitleKey != "foundation.title" || demo.MessageKey != "foundation.message" ||
    demo.TitleKey_Ref == null || demo.MessageKey_Ref == null || demo.AssetAddress_Ref == null)
    throw new InvalidDataException("The generated C# accessor could not resolve the demo references.");
Console.WriteLine("Generated C# accessors loaded Luban binary and resolved localization and asset references.");
