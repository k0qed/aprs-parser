﻿using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace aprsparser
{
    /// <summary>
    /// This class represents a raw APRS packet (TNC2 format) defining 
    /// only the basic elements found in all APRS packets. It uses several 
    /// helper classes to parse and store some of these elements.
    /// </summary>
    public class AprsPacket
    {
        //properties
        public string RawPacket { get; private set; }
        public Callsign SourceCallsign { get; private set; }
        public Callsign DestCallsign { get; private set; }
        public string Digis { get; private set; }
        public Char DataTypeCh { get; private set; }
        public PacketDataType DataType { get; private set; }
        public string SourcePathHeader { get; private set; }
        public string InformationField { get; private set; }
        public string Comment { get; private set; }
        public Char SymbolTableIdentifier { get; private set; }
        public Char SymbolCode { get; private set; }
        public bool FromD7 { get; set; }
        public bool FromD700 { get; set; }

        public Position Position { get; private set; }
        public DateTime? TimeStamp { get; set; }

        public MessageData MessageData = new MessageData();

        //internal
        private readonly Regex _regexAprsPacket = new Regex(@"([\w-]{1,9})>([\w-]{1,9})(?:,(.*?))?:(.?)(.*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        //ctor
        public AprsPacket()
        {
            Position = new Position();
            Comment = string.Empty;
        }

        //events
        public event EventHandler<ParseErrorEventArgs> ParseErrorEvent;
        internal void RaiseParseErrorEvent(string packet, string error)
        {
            EventHandler<ParseErrorEventArgs> handler = ParseErrorEvent;
            handler?.Invoke(this, new ParseErrorEventArgs(packet, error));
        }

        /// <summary>
        /// Parses the raw packet into it's basic components
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>Returns true if successful, false otherwise</returns>
        public bool Parse(string packet)
        {
            try
            {
                //split packet into basic components of:
                // from callsign
                // destination callsign
                // digis
                // packet type
                // packet payload
                Position.Clear();
                RawPacket = packet;
                DataType = PacketDataType.Unknown;

                var q = packet.IndexOf(">", StringComparison.Ordinal); //separates source and dest callsigns
                var p = packet.IndexOf(":", StringComparison.Ordinal); //separates header from information field
                if (p == -1 || q == -1 || p <= q) return false;

                SourcePathHeader = packet.Substring(0, p);
                DataTypeCh = packet[p + 1];
                DataType = AprsDataType.GetDataType(DataTypeCh);
                if (DataType == PacketDataType.Unknown) DataTypeCh = (char)0x00;
                SourceCallsign = new Callsign(packet.Substring(0, q));
                Digis = "";

                var s = SourcePathHeader.Substring(q + 1); // remove source callsign and ">"
                var comma = s.IndexOf(",", StringComparison.Ordinal);
                if (comma > 0)
                {
                    //extract the destination CallSign
                    DestCallsign = new Callsign(s.Substring(0, comma));
                    Digis = s.Substring(comma + 1);
                }
                else
                {
                    DestCallsign = new Callsign(s);
                }

                if (DataType != PacketDataType.Unknown)
                {
                    InformationField = packet.Substring(p + 2);
                }
                else
                {
                    InformationField = packet.Substring(p + 1);
                }

                if (!string.IsNullOrEmpty(Digis))
                {
                    SourcePathHeader += "," + Digis;
                }

                //parse information field
                if (InformationField.Length > 0)
                    ParseInformationField();
                else
                    DataType = PacketDataType.Beacon;

                //compute gridsquare if not given
                if (Position.IsValid() && string.IsNullOrEmpty(Position.Gridsquare))
                {
                    Position.Gridsquare = AprsUtil.LatLonToGridSquare(Position.CoordinateSet);
                }

                //validate required elements
                //todo: 

                return true;
            }
            catch (Exception ex)
            {
                RaiseParseErrorEvent(packet, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Parses the raw packet into it's basic components
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>Returns true if successful, false otherwise</returns>
        [Obsolete("This method proved to be too slow when processing the live APRS feed.")]
        public bool ParseRegEx(string packet)
        {
            try
            {
                //split packet into basic components of:
                // from callsign
                // destination callsign
                // digis
                // packet type
                // packet payload
                Position.Clear();
                RawPacket = packet;
                DataType = PacketDataType.Unknown;
                if (_regexAprsPacket.IsMatch(packet))
                {
                    var m = _regexAprsPacket.Match(packet);
                    var g = m.Groups;
                    if (g.Count >= 5)
                    {
                        var idx = 0;
                        RawPacket = g[idx++].Value;
                        SourceCallsign = new Callsign(g[idx++].Value);
                        DestCallsign = new Callsign(g[idx++].Value);
                        //if less than 6 groups no digis
                        Digis = g.Count < 6 ? string.Empty : g[idx++].Value;
                        DataTypeCh = g[idx++].Value[0];
                        DataType = AprsDataType.GetDataType(DataTypeCh);
                        InformationField = g[idx].Value;
                        SourcePathHeader = SourceCallsign.StationCallsign + '>' + DestCallsign.StationCallsign;
                        if (!string.IsNullOrEmpty(Digis))
                        {
                            SourcePathHeader += "," + Digis;
                        }

                        //parse information field
                        if (InformationField.Length > 0)
                            ParseInformationField();
                        else
                            DataType = PacketDataType.Beacon;

                        //compute gridsquare if not given
                        if (Position.IsValid() && string.IsNullOrEmpty(Position.Gridsquare))
                        {
                            Position.Gridsquare = AprsUtil.LatLonToGridSquare(Position.CoordinateSet);
                        }

                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                RaiseParseErrorEvent(packet, ex.Message);
                return false;
            }
        }
        private void ParseDateTime(string str)
        {
            try
            {
                if (string.IsNullOrEmpty(str))
                    return;

                //assume current date/time 
                TimeStamp = DateTime.UtcNow;

                //we should see one of the following strings
                //  DDHHMM[z|/]                      (time is z=UTC /=local)
                //  HHMMSSh                          (time is UTC)
                //  NNDDHHMM      NN is month number (time is UTC)

                int day, hour, minute;

                int l = str.Length;
                if (str[l - 1] == 'z')
                {
                    try
                    {
                        day = int.Parse(str.Substring(0, 2));
                        hour = int.Parse(str.Substring(2, 2));
                        minute = int.Parse(str.Substring(4, 2));
                        TimeStamp = new DateTime(TimeStamp.GetValueOrDefault().Year,
                            TimeStamp.GetValueOrDefault().Month,
                            day, hour, minute, 0);
                    }
                    catch (Exception)
                    {
                        TimeStamp = DateTime.UtcNow;
                    }
                }
                else if (str[l - 1] == '/')
                {
                    //not going to try to deal with local times (impossible in this context anyhow)
                    TimeStamp = null;
                }
                else if (str[l - 1] == 'h')
                {
                    hour = int.Parse(str.Substring(0, 2));
                    minute = int.Parse(str.Substring(2, 2));
                    int second = int.Parse(str.Substring(4, 2));
                    TimeStamp = new DateTime(TimeStamp.GetValueOrDefault().Year,
                        TimeStamp.GetValueOrDefault().Month,
                        TimeStamp.GetValueOrDefault().Day, hour, minute, second);
                }
                else if (l == 8)
                {
                    int month = int.Parse(str.Substring(0, 2));
                    day = int.Parse(str.Substring(2, 2));
                    hour = int.Parse(str.Substring(4, 2));
                    minute = int.Parse(str.Substring(6, 2));
                    TimeStamp = new DateTime(TimeStamp.GetValueOrDefault().Year,
                        month, day, hour, minute, 0);
                }
                else
                {
                    TimeStamp = null;
                }
            }
            catch (Exception)
            {
                TimeStamp = null;
            }
        }

        public void ParseInformationField()
        {
            switch (DataType)
            {
                case PacketDataType.Unknown:
                    RaiseParseErrorEvent(RawPacket, "Unknown packet type");
                    break;
                case PacketDataType.Position:            // '!'  Position without timestamp (no APRS messaging)
                case PacketDataType.PositionMsg:         // '='  Position without timestamp (with APRS messaging)
                    ParsePosition();
                    break;
                case PacketDataType.PositionTime:        // '/'  Position with timestamp (no APRS messaging)
                case PacketDataType.PositionTimeMsg:     // '@'  Position with timestamp (with APRS messaging)
                    ParsePositionTime();
                    break;
                case PacketDataType.Message:             // ':'  Message
                    ParseMessage(InformationField);
                    break;
                case PacketDataType.MicECurrent:         // #$1C Current Mic-E Data (Rev 0 beta)
                case PacketDataType.MicEOld:             // #$1D Old Mic-E Data (Rev 0 beta)
                case PacketDataType.TMD700:              // '''  Old Mic-E Data (but current for TM-D700)
                case PacketDataType.MicE:                // '`'  Current Mic-E data (not used in TM-D700)
                    ParseMicE();
                    break;
                case PacketDataType.Beacon:
                case PacketDataType.Status:              // '>'  Status
                case PacketDataType.PeetBrosUII1:        // '#'  Peet Bros U-II Weather Station
                case PacketDataType.PeetBrosUII2:        // '*'  Peet Bros U-II Weather Station
                case PacketDataType.WeatherReport:       // '_'  Weather Report (without position)
                case PacketDataType.Object:              // ';'  Object
                case PacketDataType.Item:                // ')'  Item
                case PacketDataType.StationCapabilities: // '<'  Station Capabilities
                case PacketDataType.Query:               // '?'  Query
                case PacketDataType.UserDefined:         // '{'  User-Defined APRS packet format
                case PacketDataType.Telemetry:           // 'T'  Telemetry data
                case PacketDataType.InvalidOrTestData:   // ','  Invalid data or test data
                case PacketDataType.MaidenheadGridLoc:   // '['  Maidenhead grid locator beacon (obsolete)
                case PacketDataType.RawGPSorU2K:         // '$'  Raw GPS data 
                case PacketDataType.ThirdParty:          // '}'  Third-party traffic
                case PacketDataType.MicroFinder:         // '%'  Agrelo DFJr / MicroFinder
                case PacketDataType.MapFeature:          // '&'  [Reserved - Map Feature]
                case PacketDataType.ShelterData:         // '+'  [Reserved - Shelter data with time]
                case PacketDataType.SpaceWeather:        // '.'  [Reserved - Space Weather]
                    //do nothing - not implemented
                    break;
                default:
                    RaiseParseErrorEvent(RawPacket, "Unexpected packet data type in information field");
                    break;
            }
        }

        private void ParseMessage(string infoField)
        {
            var s = infoField;

            //addressee field must be 9 characters long
            if (s.Length < 9)
            {
                DataType = PacketDataType.InvalidOrTestData;
                return;
            }

            //get addressee
            MessageData.Addressee = s.Substring(0, 9).ToUpper().Trim();

            if (s.Length < 10) return; //no message

            s = s.Substring(10);
            //look for ack and reject messages
            if (s.Length > 3)
            {
                if (s.StartsWith("ACK", StringComparison.OrdinalIgnoreCase))
                {
                    MessageData.MsgType = MessageType.mtAck;
                    MessageData.SeqId = s.Substring(3);
                    MessageData.MsgText = string.Empty;
                    return;
                }
                if (s.StartsWith("REJ", StringComparison.OrdinalIgnoreCase))
                {
                    MessageData.MsgType = MessageType.mtRej;
                    MessageData.SeqId = s.Substring(3);
                    MessageData.MsgText = string.Empty;
                    return;
                }
            }

            //save sequence number - if any
            int idx = s.LastIndexOf("{", StringComparison.Ordinal);
            if (idx >= 0)
            {
                MessageData.SeqId = s.Substring(idx + 1);
                s = s.Substring(0, s.Length - MessageData.SeqId.Length - 1);
            }

            //assume standard message
            MessageData.MsgType = MessageType.mtGeneral;

            //further process message portion
            if (s.Length > 0)
            {
                //is this an NWS message
                if (s.StartsWith("NWS-", StringComparison.OrdinalIgnoreCase))
                {
                    MessageData.MsgType = MessageType.mtNWS;
                }
                else if (s.StartsWith("NWS_", StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Replace("NWS_", "NWS-"); //fix
                    MessageData.MsgType = MessageType.mtNWS;
                }
                else if (s.StartsWith("BLN", StringComparison.OrdinalIgnoreCase))
                {
                    //see if this is a bulletin or announcement
                    if (Regex.IsMatch(MessageData.Addressee, "^BLN[A-Z]", RegexOptions.IgnoreCase))
                    {
                        MessageData.MsgType = MessageType.mtAnnouncement;
                    }
                    else if (Regex.IsMatch(MessageData.Addressee, "^BLN[0-9]", RegexOptions.IgnoreCase))
                    {
                        MessageData.MsgType = MessageType.mtBulletin;
                    }
                }
                else if (Regex.IsMatch(s, @"^AA:|^\[AA\]", RegexOptions.IgnoreCase))
                {
                    MessageData.MsgType = MessageType.mtAutoAnswer;
                }
            }

            //save text of message
            MessageData.MsgText = s;
        }

        private void ParseMicE()
        {
            char ConvertDest(char ch)
            {
                int ci = ch - 0x30; //adjust all to be 0 based
                if (ci == 0x1C) ci = 0x0A; //change L to be a space digit
                if ((ci > 0x10) && (ci <= 0x1B)) ci = ci - 1; //A-K need to be decremented
                if ((ci & 0x0F) == 0x0A) ci = ci & 0xF0; //space is converted to 0 - we don't support ambiguity
                return (char)ci;
            }
            
            string dest = DestCallsign.StationCallsign;
            if (dest.Length < 6 || dest.Length == 7) return;

            //validate
            bool custom = (((dest[0] >= 'A') && (dest[0] <= 'K')) || ((dest[1] >= 'A') && (dest[1] <= 'K')) || ((dest[2] >= 'A') && (dest[2] <= 'K')));
            for (int j = 0; j < 3; j++)
            {
                var ch = dest[j];
                if (custom)
                {
                    if ((ch < '0') || (ch > 'L') || ((ch > '9') && (ch < 'A'))) return;
                }
                else
                {
                    if ((ch < '0') || (ch > 'Z') || ((ch > '9') && (ch < 'L')) || ((ch > 'L') && (ch < 'P'))) return;
                }
            }
            for (int j = 3; j < 6; j++)
            {
                var ch = dest[j];
                if ((ch < '0') || (ch > 'Z') || ((ch > '9') && (ch < 'L')) || ((ch > 'L') && (ch < 'P'))) return;
            }
            if (dest.Length > 6)
            {
                if ((dest[6] != '-') || (dest[7] < '0') || (dest[7] > '9')) return;
                if (dest.Length == 9)
                {
                    if ((dest[8] < '0') || (dest[8] > '9')) return;
                }
            }

            //parse the destination field
            int c = ConvertDest(dest[0]);
            int mes = 0; //message code
            if ((c & 0x10) != 0) mes = 0x08; //set the custom flag
            if (c >= 0x10) mes = mes + 0x04;
            int d = (c & 0x0F) * 10; //degrees
            c = ConvertDest(dest[1]);
            if (c >= 0x10) mes = mes + 0x02;
            d = d + (c & 0x0F);
            c = ConvertDest(dest[2]);
            if (c >= 0x10) mes += 1;
            //save message index
            MessageData.MsgIndex = mes;
            int m = (c & 0x0F) * 10; //minutes
            c = ConvertDest(dest[3]);
            bool north = c >= 0x20;
            m = m + (c & 0x0F);
            c = ConvertDest(dest[4]);
            bool hundred = c >= 0x20; //flag for adjustment
            int s = (c & 0x0F) * 10; //hundredths of minutes
            c = ConvertDest(dest[5]);
            bool west = c >= 0x20;
            s = s + (c & 0x0F);
            double lat = d + (m / 60.0) + (s / 6000.0);
            if (!north) lat = -lat;
            Position.CoordinateSet.Latitude = new Coordinate(lat, true);

            //parse the symbol
            if (InformationField.Length > 6) SymbolCode = InformationField[6];
            if (InformationField.Length > 7) SymbolTableIdentifier = InformationField[7];

            //set D7/D700 flags
            if (InformationField.Length > 8)
            {
                FromD7 = InformationField[8] == '>';
                FromD700 = InformationField[8] == ']';
            }

            //parse the longitude
            d = InformationField[0] - 28;
            m = InformationField[1] - 28;
            s = InformationField[2] - 28;

            //validate
            if ((d < 0) || (d > 99) || (m < 0) || (m > 99) || (s < 0) || (s > 99))
            {
                Position.Clear();
                return;
            }

            //adjust the degrees value
            if (hundred) d = d + 100;
            if (d >= 190) d = d - 190; else if (d >= 180) d = d - 80;
            //adjust minutes 0-9 to proper spot
            if (m >= 60) m = m - 60;
            double lon = d + (m / 60.0) + (s / 6000.0);
            if (west) lon = -lon;
            Position.CoordinateSet.Longitude = new Coordinate(lon, false);

            //record comment
            Comment = InformationField.Length > 8 ? InformationField.Substring(8) : string.Empty;

            if (InformationField.Length > 5)
            {
                //parse the Speed/Course (s/d)
                m = InformationField[4] - 28;
                if ((m < 0) || (m > 97)) return;
                s = InformationField[3] - 28;
                if ((s < 0) || (s > 99)) return;
                s = (int)Math.Round((double)((s * 10) + (m / 10))); //speed in knots
                d = InformationField[5] - 28;
                if ((d < 0) || (d > 99)) return;

                d = ((m % 10) * 100) + d; //course
                if (s >= 800) s = s - 800;
                if (d >= 400) d = d - 400;
                if (d > 0)
                {
                    Position.Course = d;
                    Position.Speed = s;
                }
            }
        }

        private void ParsePosition()
        {
            //after parsing position and symbol from the information field 
            //all that can be left is a comment
            Comment = ParsePositionAndSymbol(InformationField);
        }

        private string ParsePositionAndSymbol(string ps)
        {
            try
            {
                if (string.IsNullOrEmpty(ps))
                {
                    //not valid
                    Position.Clear();
                    return string.Empty;
                }

                //compressed format if the first character is not a digit
                if (!Char.IsDigit(ps[0]))
                {
                    //compressed position data (13)
                    if (ps.Length < 13)
                    {
                        //not valid
                        Position.Clear();
                        return string.Empty;
                    }
                    string pd = ps.Substring(0, 13);

                    SymbolTableIdentifier = pd[0];
                    //since compressed format never starts with a digit, to represent a
                    //digit as the overlay character a letter (a..j) is used instead
                    if ("abcdefghij".ToCharArray().Contains(SymbolTableIdentifier))
                    {
                        //convert to digit (0..9)
                        int sti = SymbolTableIdentifier - 'a' + '0';
                        SymbolTableIdentifier = (Char)sti;
                    }
                    SymbolCode = pd[9];

                    const int sqr91 = 91 * 91;
                    const int cube91 = 91 * 91 * 91;

                    //lat
                    string sLat = pd.Substring(1, 4);
                    double dLat = 90 - (
                        (sLat[0] - 33) * cube91 +
                        (sLat[1] - 33) * sqr91 +
                        (sLat[2] - 33) * 91 +
                        (sLat[3] - 33)
                        ) / 380926.0;
                    Position.CoordinateSet.Latitude = new Coordinate(dLat, true);

                    //lon
                    string sLon = pd.Substring(5, 4);
                    double dLon = -180 + (
                        (sLon[0] - 33) * cube91 +
                        (sLon[1] - 33) * sqr91 +
                        (sLon[2] - 33) * 91 +
                        (sLon[3] - 33)
                        ) / 190463.0;
                    Position.CoordinateSet.Longitude = new Coordinate(dLon, false);

                    //strip off position report and return remainder of string
                    ps = ps.Substring(13);
                }
                else
                {
                    if (ps.Length < 19)
                    {
                        //not valid
                        Position.Clear();
                        return string.Empty;
                    }

                    //normal (uncompressed)
                    string pd = ps.Substring(0, 19); //position data
                    string sLat = pd.Substring(0, 8); //latitude
                    SymbolTableIdentifier = pd[8];
                    string sLon = pd.Substring(9, 9); //longitude
                    SymbolCode = pd[18];

                    Position.CoordinateSet.Latitude = new Coordinate(sLat);
                    Position.CoordinateSet.Longitude = new Coordinate(sLon);

                    //check for valid lat/lon values
                    if (Position.CoordinateSet.Latitude.Value < -90 || Position.CoordinateSet.Latitude.Value > 90 ||
                       Position.CoordinateSet.Longitude.Value < -180 || Position.CoordinateSet.Longitude.Value > 180)
                    {
                        Position.Clear();
                    }

                    //strip off position report and return remainder of string
                    ps = ps.Substring(19);
                }
                return ps;
            }
            catch (Exception)
            {
                return InformationField;
            }
        }

        private void ParsePositionTime()
        {
            //InformationField
            ParseDateTime(InformationField.Substring(0, 7));
            string psr = InformationField.Substring(7);


            //after parsing position and symbol from the information field 
            //all that can be left is a comment
            Comment = ParsePositionAndSymbol(psr);

            //ignoring weather data "_" for now
        }
    }
}
