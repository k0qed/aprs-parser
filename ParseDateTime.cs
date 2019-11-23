using System;
using System.Diagnostics;

namespace winlink.aprs
{
    public partial class AprsPacket
    {
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
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                TimeStamp = null;
            }
        }
    }
}
