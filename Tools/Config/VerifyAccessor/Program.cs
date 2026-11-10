using AstralOath.Config;
using Luban;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("Provide the Luban binary and JSON output directories.");
var tables = new Tables(file => new ByteBuf(File.ReadAllBytes(Path.Combine(args[0], file + ".bytes"))));
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
T[] Read<T>(string file) =>
    JsonSerializer.Deserialize<T[]>(File.ReadAllBytes(Path.Combine(args[1], file + ".json")), json)
    ?? throw new InvalidDataException($"Missing JSON data for {file}.");

var ui = Read<UiData>("demo_ui");
var locale = Read<LocaleData>("demo_localizationkeys");
var assets = Read<AssetData>("demo_assets");
if (!ui.SequenceEqual(tables.Ui.DataList.Select(row =>
        new UiData(row.Id, row.TitleKey, row.MessageKey, row.DisplaySeconds, row.AssetAddress))) ||
    !locale.SequenceEqual(tables.LocalizationKeys.DataList.Select(row => new LocaleData(row.Key, row.ZhCn))) ||
    !assets.SequenceEqual(tables.Assets.DataList.Select(row => new AssetData(row.Address, row.SourcePath))))
    throw new InvalidDataException("The generated C# accessor read binary data that differs from Luban JSON.");

if (tables.Ui.GetOrDefault(1) == null || tables.Ui.DataList.Any(row =>
        row.Id is < 1 or > 9999 || row.DisplaySeconds is < 1 or > 30 ||
        row.TitleKey_Ref?.Key != row.TitleKey || row.MessageKey_Ref?.Key != row.MessageKey ||
        row.AssetAddress_Ref?.Address != row.AssetAddress))
    throw new InvalidDataException("The generated C# accessor could not resolve valid demo rows and references.");
Console.WriteLine("Generated C# accessors match the current Luban JSON and resolve all localization and asset references.");

record UiData(int Id, string TitleKey, string MessageKey, int DisplaySeconds, string AssetAddress);
record LocaleData(string Key, string ZhCn);
record AssetData(string Address, string SourcePath);
