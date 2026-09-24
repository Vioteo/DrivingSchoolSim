using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DrivingSchool.Simulation.RoadGraph
{
    /// <summary>
    /// SHA256 over a canonical text dump of a DTO graph (public fields sorted by name, invariant round-trip numbers).
    /// Independent of JSON formatting, so two compilations can be compared without a serializer.
    /// </summary>
    public static class GraphFingerprint
    {
        public static string Compute(object root)
        {
            var sb = new StringBuilder();
            Write(sb, root, 0);
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())).Select(b => b.ToString("x2")));
        }

        static void Write(StringBuilder sb, object value, int depth)
        {
            if (depth > 32) throw new InvalidOperationException("Graph too deep for fingerprint");
            switch (value)
            {
                case null: sb.Append('~'); return;
                case string s: sb.Append('"').Append(s.Replace("\"", "\\\"")).Append('"'); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case bool b: sb.Append(b ? 'T' : 'F'); return;
                case Enum e: sb.Append(Convert.ToInt64(e, CultureInfo.InvariantCulture)); return;
                case IConvertible c when value.GetType().IsPrimitive: sb.Append(c.ToString(CultureInfo.InvariantCulture)); return;
                case IEnumerable list:
                    sb.Append('[');
                    foreach (var item in list) { Write(sb, item, depth + 1); sb.Append(','); }
                    sb.Append(']');
                    return;
            }
            sb.Append('{');
            foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                sb.Append(field.Name).Append(':');
                Write(sb, field.GetValue(value), depth + 1);
                sb.Append(';');
            }
            sb.Append('}');
        }
    }
}
