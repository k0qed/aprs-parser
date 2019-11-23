using System;
using System.Text.RegularExpressions;

namespace winlink.aprs
{
    /// <summary>
    /// This class represents a raw APRS packet (TCN2 format) defining 
    /// only the basic elements found in all APRS packets. It uses several 
    /// helper classes to parse and store some of these elements.
    /// </summary>
    public partial class AprsPacket
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
            if (handler != null)
                handler(this, new ParseErrorEventArgs(packet, error));
        }

        /// <summary>
        /// Parses the raw packet into it's basic components
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>Returns true if sucessful, false otherwise</returns>
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


        /// <summary>
        /// Parses the raw packet into it's basic components
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>Returns true if sucessful, false otherwise</returns>
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

    }
}
