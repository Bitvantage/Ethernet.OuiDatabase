# Bitvantage.Ethernet.OuiDatabase
Lookup manufacture information for MAC addresses

## Installing via NuGet Package Manager
```sh
PM> NuGet\Install-Package Bitvantage.Ethernet.OuiDatabase
```

## OUI Database
The first three octets of a MAC address are assigned by the IEEE. The registration data is public and can be looked up for a given MAC address. The manufacturer can often give clues as to what the device is. 
```csharp
// Create a new OuiDatabase using the defaults
// The default options will use the last downloaded database if it exists, and fallback to an internal database if it does not exist. 
// An asynchronous background update check is triggered upon object creation that will update the database every 30 days.
var ouiDatabase = new OuiDatabase();

var ouiRecord = ouiDatabase["64:D1:A3:01:02:03"];
var organization = ouiRecord.Organization;
var address = ouiRecord.Address;
var prefix = ouiRecord.Prefix;
```