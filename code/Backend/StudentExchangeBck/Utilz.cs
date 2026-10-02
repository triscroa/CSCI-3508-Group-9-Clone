namespace StudentExchangeBck
{
    public static class Utilz
    {
        public static IEnumerable<List<T>> Transpose<T>(IEnumerable<IEnumerable<T>> source) =>
            source.SelectMany(inner => inner.Select((item, index) => new { item, index }))
            .GroupBy(i => i.index, i => i.item).Select(g => g.ToList());

        public static string FirstUpper(string txt)
        {
            var split = txt.Split(' ');
            for (int i = 0; i < split.Length; i++)
            {
                var w = split[i];
                if (w.Length >= 2)
                    split[i] = w.ToUpper().Substring(0, 1) + w.ToLower().Substring(1);
                else if (w.Length == 1)
                    split[i] = w.ToUpper();
            }
            return string.Join(' ', split);
        }

        static readonly TimeZoneInfo _mountainZone = TimeZoneInfo.FindSystemTimeZoneById("Mountain Standard Time");
        public static DateTime ToMtn(DateTime? utc = null)
        {
            if (utc == null) utc = DateTime.UtcNow;
            return TimeZoneInfo.ConvertTimeFromUtc(utc.Value, _mountainZone);
        }
    }
}
