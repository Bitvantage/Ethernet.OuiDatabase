/*
   Bitvantage.Ethernet.OuiDatabase
   Copyright (C) 2024 Michael Crino
   
   This program is free software: you can redistribute it and/or modify
   it under the terms of the GNU Affero General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.
   
   This program is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
   GNU Affero General Public License for more details.
   
   You should have received a copy of the GNU Affero General Public License
   along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Bitvantage.Ethernet;

public class OuiDatabase : IReadOnlyDictionary<MacAddress, OuiRecord>, IDisposable
{
    private const ulong OuiMask = 0b11111110_11111111_11111111_00000000_00000000_00000000;
    private static readonly Lazy<OuiDatabase> DefaultInstance = new(() => new OuiDatabase(new OuiDatabaseOptions()), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly DateTime _internalDatabaseDateTime = new(2024, 06, 25);
    private readonly OuiDatabaseOptions _options;
    private readonly Timer _updateTimer;

    private OuiDatabaseInternal _database;
    private bool _forceUpdate;

    public static OuiDatabase Instance => DefaultInstance.Value;

    public OuiDatabase() : this(new OuiDatabaseOptions())
    {
    }

    public OuiDatabase(OuiDatabaseOptions options)
    {
        _options = options;

        // update the oui database to the most current version synchronously
        if (_options.SynchronousUpdate)
            if (_options.ThrowOnUpdateFailure)
                // if the update fails, bubble up the exception
                UpdateDatabase(false);
            else
                // if the update fails, ignore the exception
                try
                {
                    UpdateDatabase(false);
                }
                catch (Exception exception)
                {
                    _options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Error, 999999, "Initial database load failed", exception));
                }

        if (_database == null || _database.Count == 0)
        {
            var currentDatabaseCacheFile =
                DatabaseCacheFile
                    .GetDatabaseCacheFiles(_options.CacheDirectory)
                    .FirstOrDefault();

            if (currentDatabaseCacheFile != null)
                try
                {
                    LoadDatabase(currentDatabaseCacheFile.FullPath, currentDatabaseCacheFile.Date);
                }
                catch (Exception exception)
                {
                    _forceUpdate = true;

                    // TODO: Add path
                    _options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Error, 999999, "Initial database load failed", exception));
                }
        }

        if (_database == null || _database.Count == 0)
            LoadInternalDatabase();

        // schedule a periodic database update
        // if the SynchronousUpdate option is not enabled, immediately schedule a background update
        if (_options.AutomaticUpdates && _options.RefreshInterval > TimeSpan.Zero)
            _updateTimer = new Timer(state => UpdateTimer(), null, _options.SynchronousUpdate ? options.RefreshInterval : TimeSpan.Zero, options.RefreshInterval);
    }

    public void LoadInternalDatabase()
    {
        /*
         * Instructions for updating the internal database:
         *
         * Download the oui database from the ieee: wget --output-document=oui.txt https://standards-oui.ieee.org/
         * Compress the oui database using Brotli: Compress(@"c:\temp\oui.txt", @"c:\temp\oui.txt.br") -or- brotli --quality=11 oui.txt
         * Replace the oui.txt.br file with the updated version
         * Update the _internalDatabaseDateTime to the current date
         */

        // loads a copy of the oui.txt file from an embedded compressed resource
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{GetType().Namespace}.Resources.oui.txt.br"))
        using (var decompressedStream = new BrotliStream(stream, CompressionMode.Decompress))
                LoadDatabase(decompressedStream, _internalDatabaseDateTime);
    }

    public void UpdateDatabase(bool force)
    {
        var options = _options;
        var startTime = DateTime.Now;

        options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Debug, 1000, $"Database update cycle started, forced={force}"));
        var tempDatabasePath = Path.Combine(options.CacheDirectory, "oui.loading");
        var newDatabasePath = Path.Combine(options.CacheDirectory, $"oui-{startTime.Ticks}.txt");

        options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Debug, 1001, $"Source URL is {options.Location}"));
        options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Debug, 1002, $"Target path is {newDatabasePath}"));

        var databaseCacheFiles =
            DatabaseCacheFile.GetDatabaseCacheFiles(options.CacheDirectory).OrderByDescending(item => item.Sequence).ToList();

        options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Debug, 1003, $"Found {databaseCacheFiles.Count:N0} database files in target location: {string.Join(',', databaseCacheFiles)}"));

        var latestDatabaseCache = databaseCacheFiles.FirstOrDefault();

        options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Debug, 1005, $"Most recent database cache is : {latestDatabaseCache?.FullPath ?? "<null>"}"));

        // if this is not a forced update
        // and the current version of the last saved database is not old enough to trigger a database download
        // and the saved database is newer then the loaded database
        // then return
        if (!force && latestDatabaseCache != null && latestDatabaseCache.Date > (_database?.Version ?? DateTime.MinValue) && latestDatabaseCache.Date + options.RefreshInterval >= startTime)
            return;

        // if this is not a forced updated
        // and there is at least one database cache file
        // and if the current database file is within the update interval
        // do nothing and return
        if (!force && latestDatabaseCache != null && latestDatabaseCache.Date + options.CheckInterval >= startTime)
            return;

        // attempt to download the latest version of the database cache file
        Directory.CreateDirectory(options.CacheDirectory);

        // another instance, thread, or process may be attempting to download this file at the same time
        // to avoid a race condition get a system wide mutex to ensure that only one update can run at any given time
        // lock the file, and if getting the lock fails, then continue on
        var mutexName = $"OUI_Database_Update_{Regex.Replace(options.CacheDirectory[^(options.CacheDirectory.Length < 100 ? options.CacheDirectory.Length : 100)..], "[^a-zA-Z0-9]", "_")}";
        using (var updateLock = new Mutex(false, mutexName))
        {
            updateLock.WaitOne();

            using (var tempFile = File.Open(tempDatabasePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // check again to ensure that a new file has not been created between the time the lock was requested and acquired
                databaseCacheFiles =
                    DatabaseCacheFile
                        .GetDatabaseCacheFiles(options.CacheDirectory)
                        .OrderByDescending(item => item.Sequence)
                        .ToList();

                latestDatabaseCache = databaseCacheFiles.FirstOrDefault();

                if (!force && latestDatabaseCache != null && latestDatabaseCache.Date + options.CheckInterval >= startTime)
                    return;

                // download the new file
                using (var client = new HttpClient())
                using (var request = new HttpRequestMessage(HttpMethod.Get, options.Location))
                {
                    var response = client.Send(request);

                    var result = response.Content.ReadAsStream();
                    result.CopyTo(tempFile);
                }
            }

            LoadDatabase(tempDatabasePath, startTime);

            // move the temp file to the new filename that will be picked up on subsequent loads
            File.Move(tempDatabasePath, newDatabasePath);

            // delete old version of the database
            // retain the latest three versions to help avoid race conditions
            foreach (var databaseCacheFile in databaseCacheFiles.Skip(3))
                File.Delete(databaseCacheFile.FullPath);
        }
    }

    private void LoadDatabase(Stream stream, DateTime version)
    {
        var ouiRecords =
            Parse(stream)
                .DistinctBy(item => item.Prefix.MacAddressBits);

        var database = new OuiDatabaseInternal { Version = version };
        foreach (var ouiRecord in ouiRecords) 
            database.Add(ouiRecord.Prefix.MacAddressBits, ouiRecord);

        if (database.Count == 0)
            throw new FormatException("Downloaded database contains no records");

        _database = database;
    }

    private void LoadDatabase(string filename, DateTime version)
    {
        using (var inputStream = File.Open(filename, FileMode.Open, FileAccess.Read)) 
            LoadDatabase(inputStream, version);
    }

    private static IEnumerable<OuiRecord> Parse(Stream content)
    {
        using var sr = new StreamReader(content);
        var address = new List<string>();

        // first four lines are the header, skip past them
        sr.ReadLine();
        sr.ReadLine();
        sr.ReadLine();
        sr.ReadLine();

        while (!sr.EndOfStream)
        {
            // next two lines are the MAC OUI
            // the only difference between the lines is that the first one has dashes in the MAC OUI
            // since it is slightly easier to parse, use the second dash-less entry
            sr.ReadLine();

            var data = sr.ReadLine();
            if (data == null)
                throw new FormatException();

            var prefix = BitConverter.ToUInt64(
                new byte[]
                {
                    0x00,
                    0x00,
                    0x00,
                    byte.Parse(data[4..6], NumberStyles.HexNumber),
                    byte.Parse(data[2..4], NumberStyles.HexNumber),
                    byte.Parse(data[..2], NumberStyles.HexNumber),
                    0x00,
                    0x00
                });

            var organization = data[22..];

            address.Clear();
            string? addressLine;
            while ((addressLine = sr.ReadLine()) is not null and not "")
                address.Add(addressLine[4..]);

            yield return new OuiRecord(prefix, organization, string.Join('\n', address));
        }
    }

    private void UpdateTimer()
    {
        try
        {
            UpdateDatabase(_forceUpdate);

            if (_forceUpdate)
                _forceUpdate = false;
        }
        catch (Exception exception)
        {
            // TODO: Add path
            _options.OnDatabaseEvent(new UpdateEventArgs(UpdateEventArgs.LogLevel.Error, 999999, "Initial database load failed", exception));
        }
    }

    public void Dispose()
    {
        _updateTimer.Dispose();
    }

    public IEnumerator<KeyValuePair<MacAddress, OuiRecord>> GetEnumerator()
    {
        return
            _database
                .Select(item => new KeyValuePair<MacAddress, OuiRecord>(new MacAddress(item.Key), item.Value))
                .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _database.Count;

    public bool ContainsKey(MacAddress key)
    {
        var oui = key.ToUInt64() & OuiMask;
        return _database.ContainsKey(oui);
    }

    public bool TryGetValue(MacAddress key, [NotNullWhen(true)] out OuiRecord? value)
    {
        var oui = key.ToUInt64() & OuiMask;

        return _database.TryGetValue(oui, out value);
    }

    public OuiRecord this[MacAddress key]
    {
        get
        {
            if (!TryGetValue(key, out var value))
                throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

            return value;
        }
    }

    public IEnumerable<MacAddress> Keys => _database.Keys.Select(item => new MacAddress(item));
    public IEnumerable<OuiRecord> Values => _database.Values;

    private class OuiDatabaseInternal : Dictionary<ulong, OuiRecord>
    {
        public DateTime Version { get; set; }
    }

    private record DatabaseCacheFile
    {
        private static readonly Regex FilePatternRegex = new(@"^oui-(?<ticks>\d{1,18}).txt$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        internal DateTime Date { get; }
        internal string Filename { get; }
        internal string FullPath { get; }
        internal long Sequence { get; }

        private DatabaseCacheFile(string fullPath, string filename, long sequence)
        {
            FullPath = fullPath;
            Filename = filename;
            Sequence = sequence;
            Date = new DateTime(sequence);
        }

        internal static IEnumerable<DatabaseCacheFile> GetDatabaseCacheFiles(string? path)
        {
            if (!Directory.Exists(path))
                return Enumerable.Empty<DatabaseCacheFile>();

            var databaseCacheFiles =
                Directory
                    .EnumerateFiles(path, "oui-*.txt")
                    .Select(item => new { FullPath = item, Match = FilePatternRegex.Match(Path.GetFileName(item)) })
                    .Where(item => item.Match.Success)
                    .Select(item => new DatabaseCacheFile(item.FullPath, item.Match.Value, long.Parse(item.Match.Groups["ticks"].Value)));

            return databaseCacheFiles;
        }
    }
}