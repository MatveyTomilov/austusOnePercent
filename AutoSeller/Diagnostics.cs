using System;
using System.IO;
using Newtonsoft.Json;

namespace FaustusOnePercent.AutoSeller;

public sealed class Diagnostics
{
    public string DirectoryPath { get; }
    public string LastFile { get; private set; } = "";
    private readonly object logGate = new();
    public Diagnostics(string directory) { DirectoryPath = directory; Directory.CreateDirectory(directory); }
    public void Log(string message)
    {
        // A diagnostic write failure must not interrupt order acknowledgement or hide the real error.
        try { lock (logGate) File.AppendAllText(Path.Combine(DirectoryPath, "events.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch (IOException) { }
    }
    public void Dump(object value)
    {
        LastFile = Path.Combine(DirectoryPath, "exchange-ui.json");
        File.WriteAllText(LastFile, JsonConvert.SerializeObject(value, Formatting.Indented));
    }
    public void Journal(string stage, FavoritePair pair, SaleQuote quote) => File.WriteAllText(
        Path.Combine(DirectoryPath, "pending-order.json"), JsonConvert.SerializeObject(new { stage, pair, quote }));
    public bool HasPending => File.Exists(Path.Combine(DirectoryPath, "pending-order.json"));
    // Called only after positive acknowledgement or explicit user reconciliation.
    public void ClearPending() { var path = Path.Combine(DirectoryPath, "pending-order.json"); if (File.Exists(path)) File.Delete(path); }
}
