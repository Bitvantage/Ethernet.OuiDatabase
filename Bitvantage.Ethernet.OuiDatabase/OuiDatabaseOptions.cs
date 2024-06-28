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

namespace Bitvantage.Ethernet;

public record OuiDatabaseOptions
{
    public bool AutomaticUpdates { get; init; } = true;
    public string? CacheDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkAddressing");
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromHours(1);
    public Uri Location { get; init; } = new("https://standards-oui.ieee.org/");
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromDays(30);
    public bool SynchronousUpdate { get; init; } = false;
    public bool ThrowOnUpdateFailure { get; init; } = false;

    public event EventHandler<UpdateEventArgs> DatabaseEvent;

    internal virtual void OnDatabaseEvent(UpdateEventArgs eventArgs)
    {
        DatabaseEvent?.Invoke(this, eventArgs);
    }
}

public record UpdateEventArgs
{
    public enum LogLevel
    {
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Fatal = 5
    }

    public DateTime DateTime { get; } = DateTime.Now;
    public long EventId { get; }
    public Exception? Exception { get; }
    public LogLevel Level { get; }
    public string Message { get; }

    public UpdateEventArgs(LogLevel level, long eventId, string message, Exception? exception = null)
    {
        Level = level;
        EventId = eventId;
        Message = message;
        Exception = exception;
    }
}