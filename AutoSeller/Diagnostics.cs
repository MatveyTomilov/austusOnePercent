using System;
using System.IO;
using Newtonsoft.Json;

namespace FaustusOnePercent.AutoSeller;

public sealed class Diagnostics
{
    public string DirectoryPath { get; }
    public string LastFile { get; private set; } = "";
    private readonly object logGate = new();
    private readonly string journalFile;
    public Diagnostics(string directory, string journalName = "pending-order.json")
    { DirectoryPath = directory; Directory.CreateDirectory(directory); journalFile = Path.Combine(directory, journalName); }
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
        journalFile, JsonConvert.SerializeObject(new { stage, pair, quote }));
    public bool HasPending => File.Exists(journalFile);
    // Called only after positive acknowledgement or explicit user reconciliation.
    public void ClearPending() { if (File.Exists(journalFile)) File.Delete(journalFile); }
}
