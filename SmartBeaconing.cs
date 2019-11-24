using System;
using NLog;

namespace aprsparser
{
    public class SmartBeaconing
    {
        private readonly Logger _log = LogManager.GetCurrentClassLogger();

        public double FastSpeed { get; set; }
        public int FastRate { get; set; }
        public double SlowSpeed { get; set; }
        public int SlowRate { get; set; }

        //settings for corner pegging
        private const int TurnTime = 15;
        private const int TurnMin = 10;
        private const double TurnSlope = 240;

        private Location _prevLocation;

        public SmartBeaconing()
        {
            FastSpeed = 100.0 / 3.6; //km/hr ->  m/s
            FastRate = 60; //seconds
            SlowSpeed = 5.0 / 3.6; //km/hr ->  m/s
            SlowRate = 1200; //seconds

            _prevLocation = null;
        }

        public bool SmartBeaconCheck(double latitude, double longitude, DateTime time, double speed, double bearing)
        {
            bool beacon;
            var location = new Location(latitude, longitude, time, speed, bearing);

            if (_prevLocation == null)
            {
                _log.Trace("No previous location - beacon");
                beacon = true;
            }
            else
            {
                if (SmartBeaconCornerPeg(location, _prevLocation))
                {
                    _log.Trace("Corner pegging");
                    beacon = true;
                }
                else
                {
                    var timeDiff = location.Timestamp - _prevLocation.Timestamp;
                    var beaconRate = BeaconRate(location.Speed);
                    _log.Trace($"Beacon if itme diff: {timeDiff.Seconds} >= speed rate: {beaconRate}");
                    beacon = timeDiff.Seconds >= beaconRate;
                }
            }

            if (beacon)
            {
                //save as last location
                _prevLocation = location;
            }
            return beacon;
        }

        private int BeaconRate(double speed)
        {
            //returns the beacon rate given speed
            int rate; //seconds
            if (speed <= SlowSpeed)
                rate = SlowRate;
            else if (speed >= FastSpeed)
                rate = FastRate;
            else
                rate = Convert.ToInt32(FastRate + (SlowRate - FastRate) * (FastSpeed - speed) / (FastSpeed - SlowSpeed));
            return rate;
        }

        private double GetBearingAngle(double alpha, double beta)
        {
            // returns the angle between two bearings
            var delta = Math.Abs(alpha - beta) % 360;
            if (delta <= 180) return delta;
            return 360 - delta;
        }

        private bool SmartBeaconCornerPeg(Location location, Location prevLocation)
        {
            var speed = location.Speed;
            var timeDiff = location.Timestamp - prevLocation.Timestamp;
            var bearingAngle = GetBearingAngle(location.Bearing, prevLocation.Bearing);

            // standing still / hardly moving -> no corner pegging
            if (Math.Abs(speed) < 0.01)
            {
                _log.Trace("Standing still or hardly moving - no beacon");
                return false;
            }

            // if last bearing unknown, deploy turn_time
            if (!prevLocation.HasBearing())
            {
                _log.Trace($"Last bearing unknown - beacon only if {timeDiff.Seconds} >= {TurnTime}");
                return timeDiff.Seconds >= TurnTime;
            }

            // threshold depends on slope/speed [mph]
            var threshold = Math.Floor(TurnMin + (TurnSlope / (speed * 2.23693629)));

            // need to corner peg if turn time reached and turn > threshold
            _log.Trace($"Corner peg if time diff: {timeDiff.Seconds} > turn time: {TurnTime} and bearing angle: {bearingAngle} > threshold: {threshold}");
            return timeDiff.Seconds >= TurnTime && bearingAngle > threshold;
        }
    }

    public class Location
    {
        public DateTime Timestamp { get; set; }
        public double Speed { get; set; }
        public double Bearing { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public Location()
        {
            Bearing = double.NaN;
        }

        public Location(double latitude, double longitude, DateTime time, double speed, double bearing)
        {
            Latitude = latitude;
            Longitude = longitude;
            Timestamp = time;
            Speed = speed;
            Bearing = bearing;
        }

        public bool HasBearing()
        {
            return !double.IsNaN(Bearing);
        }
    }

}
