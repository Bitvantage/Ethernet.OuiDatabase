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

namespace Bitvantage.Ethernet
{
    [Serializable]
    public record OuiRecord
    {
        public string Organization { get; }
        public string Address { get; }
        public MacAddress Prefix { get; }

        internal OuiRecord(ulong prefixInt, string organization, string address)
        {
            Prefix = new MacAddress(prefixInt);
            Organization = organization;
            Address = address;
        }
    }
}
