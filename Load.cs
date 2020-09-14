using System;
using System.IO;
using System.Linq;

namespace ttyloadsharp
{
    public class Load
        {
            public decimal OneMinute { get; set; }
            public decimal FiveMinutes { get; set; }
            public decimal FifteenMinutes { get; set; }
            public int Height1 { get; set; }
            public int Height5 { get; set; }
            public int Height15 { get; set; }
            public DateTime Time { get; set; }

            public Load()
            {
                String fileContent = File.ReadAllText("/proc/loadavg");

                var loads = fileContent.Split(' ').Take(3).Select(x => Decimal.Parse(x)).ToArray();

                OneMinute = loads[0];
                FiveMinutes = loads[1];
                FifteenMinutes = loads[2];
                Time = DateTime.Now;
            }

            public void ComputeHeights(decimal maxLoad, int height)
            {
                Height1 = (int)(height * ((maxLoad - OneMinute) / maxLoad));
                Height5 = (int)(height * ((maxLoad - FiveMinutes) / maxLoad));
                Height15 = (int)(height * ((maxLoad - FifteenMinutes) / maxLoad));
            }
        }
}