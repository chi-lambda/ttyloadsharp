using System;
using System.Collections.Generic;
using NDesk.Options;
using System.Threading;
using System.IO;
using System.Linq;

namespace ttyloadsharp
{
    class Program
    {
        [Flags]
        private enum Colors { None = 0, One = 1, Five = 2, Fifteen = 4 }

        /* storage for clock display along the bottom */
        private const int HEIGHTPAD = 7; /* 2 lines above; * 4 lines + cursor line below */
        private const int WIDTHPAD = 14;
        private const int CLOCKWIDTH = 7;
        private readonly TimeSpan OneMinute = new TimeSpan(0, 1, 0);

        private const int MINROWS = HEIGHTPAD + 6;
        private const int MINCOLS = WIDTHPAD + 6;
        private static readonly string usage =
        "Usage: {0} [<options>]\n" +
        "\n" +
        " Available options:\n" +
        "  -h -- show this help, then exit\n" +
        "  -v -- show version info, then exit\n" +
        "  -m -- monochrome mode (no ANSI escapes)\n" +
        "  -c cols -- how wide is the screen?\n" +
        "  -r rows -- and how high?\n" +
        "     (these override the default auto-detect)\n" +
        "  -i secs\n" +
        "     Alter the number of seconds in " +
        "the interval between refreshes.\n" +
        "     The default is 4, and the minimum " +
        "is 1, which is silently clamped.\n\n" +
        "  (Note: use ctrl-C to quit)\n\n" +
        "  For more info, see http://" +
        "www.daveltd.com/src/util/ttyload/";
        private static int clockpad;
        private static int clocks;
        private static string version = "1.0";

        private static decimal threshold = CoreCount();
        private static Action<Char> ActionForColor(ConsoleColor color)
        {
            return (ch) =>
            {
                var oldColor = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write(ch);
                Console.ForegroundColor = oldColor;
            };
        }

        private static readonly Dictionary<Colors, Action<Char>> colorLoadstrings = new Dictionary<Colors, Action<Char>> {
            {Colors.None, _ => Console.Write(" ")},
            {Colors.One, ActionForColor(ConsoleColor.Red)},
            {Colors.Five, ActionForColor(ConsoleColor.Green)},
            {Colors.One|Colors.Five, ActionForColor(ConsoleColor.Yellow)},
            {Colors.Fifteen, ActionForColor(ConsoleColor.Blue)},
            {Colors.One|Colors.Fifteen, ActionForColor(ConsoleColor.Magenta)},
            {Colors.Five|Colors.Fifteen, ActionForColor(ConsoleColor.Cyan)},
            {Colors.One|Colors.Five|Colors.Fifteen, ActionForColor(ConsoleColor.White)}
        };

        /* same stuff, same order: */
        private static readonly Dictionary<Colors, Action<Char>> monoLoadstrings = new Dictionary<Colors, Action<Char>> {
            {Colors.None, _ => Console.Write(" ")},
            {Colors.One, _ => Console.Write("1")},
            {Colors.Five, _ => Console.Write("2")},
            {Colors.One|Colors.Five, _ => Console.Write("3")},
            {Colors.Fifteen, _ => Console.Write("4")},
            {Colors.One|Colors.Fifteen, _ => Console.Write("5")},
            {Colors.Five|Colors.Fifteen, _ => Console.Write("6")},
            {Colors.One|Colors.Five|Colors.Fifteen, _ => Console.Write("7")}
        };

        /* by default, use color: */
        private static Dictionary<Colors, Action<Char>> loadstrings = colorLoadstrings;

        /* The following two variables should probably be assigned
        using some sort of real logic, rather than these hard-coded
        defaults, but the defaults work for now... */
        private static int intSecs = 4;

        /* other globals (ugh, I know, but it's a simple program, that needs re-writing) */
        private static String hostname;
        private static LinkedList<Load> loadAverages = new LinkedList<Load>();

        private static (int, int) GetTermSize()
        {
            return (Console.WindowWidth, Console.WindowHeight);
        }

        private static void PrintHeader()
        {
            Console.WriteLine("{0}   {1:N2}, {2:N2}, {3:N2}   {4:HH:mm:ss}       ttyload, v{5}\n",
                hostname,
                (loadAverages.Last.Value.OneMinute),
                (loadAverages.Last.Value.FiveMinutes),
                (loadAverages.Last.Value.FifteenMinutes),
                loadAverages.Last.Value.Time,
                version);
        }


        static void Main(string[] args)
        {
            bool errflag = false, versflag = false;
            String basename = System.AppDomain.CurrentDomain.FriendlyName;

            var (cols, rows) = GetTermSize();

            var options = new OptionSet(){
                {"i|interval=", "timing interval in seconds", (int s) => intSecs = s},
                {"m", "monochrome mode", m => loadstrings = m != null ? monoLoadstrings : colorLoadstrings },
                {"r|rows=", "rows", (int r) => rows = r},
                {"c|cols=", "columns", (int c) => cols = c},
                {"v", "show version", v => versflag = v != null },
                {"t=", "threshold", (decimal m) => threshold = m },
                {"h|help", "show help", h => errflag = h != null }
            };

            try
            {
                options.Parse(args);
            }
            catch (OptionException)
            {
                errflag = true;
            }

            /* version info requested, show it: */
            if (versflag)
            {
                Console.Error.WriteLine("{0} version {1}", basename, version);
                return;
            }
            /* error, show usage: */
            if (errflag)
            {
                Console.Error.WriteLine(usage, basename);
                return;
            }

            hostname = System.Environment.MachineName;

            //Console.Clear();

            if (rows < MINROWS)
            {
                Console.Error.WriteLine($"Sorry, {basename} requires at least {MINROWS} rows to run.");
                return;
            }
            if (cols < MINCOLS)
            {
                Console.Error.WriteLine($"Sorry, {basename} requires at least {MINCOLS} cols to run.");
                return;
            }

            intSecs = Math.Max(1, intSecs); /* must be positive */
            var height = rows - HEIGHTPAD - 1;
            var width = cols - WIDTHPAD;
            clocks = Math.Max(width / intSecs, width / CLOCKWIDTH);
            clockpad = (width / clocks) - CLOCKWIDTH;

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Clear();

            DateTime nextRun = DateTime.Now.AddSeconds(intSecs);

            while (!Console.KeyAvailable || Console.ReadKey().Key != ConsoleKey.Escape)
            {
                MoveCursorHome();
                CycleLoadList(new Load(), width);
                PrintHeader();
                ShowLoads(height);
                Thread.Sleep(Math.Max(0, (int)(nextRun - DateTime.Now).TotalMilliseconds));
                nextRun = nextRun.AddSeconds(intSecs);
            }
        }

        private static int CoreCount()
        {
            var lines = File.ReadAllLines("/proc/cpuinfo");

            return lines.Where(x => x.StartsWith("core id")).GroupBy(x => x).Count();
        }

        private class IndexedValue<T> : IComparable<IndexedValue<T>> where T : IComparable<T>
        {
            public IndexedValue(int index, T value)
            {
                this.Index = index;
                this.Value = value;

            }
            public int Index { get; private set; }
            public T Value { get; private set; }

            public int CompareTo(IndexedValue<T> other)
            {
                return Value.CompareTo(other.Value);
            }
        }

        private static void ShowLoads(int maxHeight)
        {
            decimal maxLoad = loadAverages.Max(x => Max(x.OneMinute, x.FiveMinutes, x.FifteenMinutes));
            //decimal minLoad = LoadAverages.Min(x => Min(x.OneMinute, x.FiveMinutes, x.FifteenMinutes));
            //var loadSpread = maxLoad - minLoad;

            foreach (var load in loadAverages)
            {
                load.ComputerHeights(maxLoad, maxHeight);
            }

            var thresholdHeight = maxLoad < 2m ? -1 : Enumerable.Range(0, maxHeight)
            .Select(height => new IndexedValue<decimal>(height, Math.Abs(maxLoad * (maxHeight - height) / maxHeight - threshold)))
            .Min()
            ?.Index ?? -1;
            for (int height = 0; height <= maxHeight; height++)
            {
                Console.Write("{0,6:F2}   ", maxLoad * (maxHeight - height) / maxHeight);
                var loadNode = loadAverages.First;
                while (loadNode != null)
                {
                    var ch = GraphCharacter(loadNode, height, n => n.Value.Height1);
                    var color = Colors.One;
                    if (ch == ' ')
                    {
                        ch = GraphCharacter(loadNode, height, n => n.Value.Height5);
                        color = Colors.Five;
                    }
                    if (ch == ' ')
                    {
                        ch = GraphCharacter(loadNode, height, n => n.Value.Height15);
                        color = Colors.Fifteen;
                    }
                    if (ch == ' ' && height == thresholdHeight)
                    {
                        ch = '╍';
                        color = Colors.One | Colors.Fifteen | Colors.Five;
                    }
                    if (ch == ' ')
                    {
                        color = Colors.None;
                    }
                    loadstrings[color](ch);
                    loadNode = loadNode.Next;
                }
                Console.WriteLine();
            }

            var lastTime = loadAverages.First.Value.Time;

            // TODO clocks

            Console.WriteLine("  Legend:\n");
            Console.Write("     1 min: ");
            loadstrings[Colors.One]('*');
            Console.Write(", 5 min: ");
            loadstrings[Colors.Five]('*');
            Console.Write(", 15 min: ");
            loadstrings[Colors.Fifteen]('*');
            Console.WriteLine();
        }

        private static void MoveCursorHome()
        {
            Console.SetCursorPosition(0, 0);
        }

        private static void CycleLoadList(Load newload, int width)
        {
            while (loadAverages.Count >= width)
            {
                loadAverages.RemoveFirst();
            }
            loadAverages.AddLast(newload);
        }

        private static decimal Max(decimal x, decimal y, decimal z)
        {
            return Math.Max(x, Math.Max(y, z));
        }
        private static decimal Min(decimal x, decimal y, decimal z)
        {
            return Math.Min(x, Math.Min(y, z));
        }

        private static char GraphCharacter(LinkedListNode<Load> loadNode, int height, Func<LinkedListNode<Load>, int> selector)
        {
            var previousNode = loadNode.Previous;
            var nextNode = loadNode.Next;
            if (height == selector(loadNode))
            {
                if (IsHorizontal(loadNode, selector))
                {
                    return '─';
                }
                else if (IsRising(loadNode, selector))
                {
                    return '╭';
                }
                else if (IsFalling(loadNode, selector))
                {
                    return '╰';
                }
            }
            else if (height < selector(loadNode) && previousNode != null)
            {
                if (height == selector(previousNode))
                {
                    return '╮';
                }
                else if (height > selector(previousNode))
                {
                    return '│';
                }
            }
            else if (previousNode != null)
            { // height > loadNode.Value.height1
                if (height == selector(previousNode))
                {
                    return '╯';
                }
                else if (height < selector(previousNode))
                {
                    return '│';
                }
            }
            return ' ';
        }

        private static bool IsHorizontal(LinkedListNode<Load> loadNode, Func<LinkedListNode<Load>, int> selector)
        {
            var previousNode = loadNode.Previous;
            var nextNode = loadNode.Next;
            return previousNode == null || selector(loadNode) == selector(previousNode);
        }
        private static bool IsRising(LinkedListNode<Load> loadNode, Func<LinkedListNode<Load>, int> selector)
        {
            var previousNode = loadNode.Previous;
            var nextNode = loadNode.Next;
            return previousNode != null && selector(loadNode) < selector(previousNode);
        }
        private static bool IsFalling(LinkedListNode<Load> loadNode, Func<LinkedListNode<Load>, int> selector)
        {
            var previousNode = loadNode.Previous;
            var nextNode = loadNode.Next;
            return previousNode != null && selector(loadNode) > selector(previousNode);
        }
    }
}