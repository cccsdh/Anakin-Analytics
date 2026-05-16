using System.Linq;
using System.Text;

namespace AnakinAnalytics
{
    internal static class StringExtensions
    {
        public static bool IsUpper(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;

            var letters = s.Where(char.IsLetter).ToArray();
            if (letters.Length == 0)
                return false;

            return letters.All(char.IsUpper);
        }

        public static string RemovePunctuation(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                // keep apostrophes to preserve contractions; remove other punctuation
                if (char.IsPunctuation(c) && c != '\'')
                    continue;

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
