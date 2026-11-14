using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// A small JSON reader and writer (no package needed). Objects keep their key order and numbers keep the text they were
    /// written with, so a file read and written back (a server base.json) only changes what was set.
    /// Objects are JsonObject, arrays List&lt;object&gt;, numbers JsonNumber, plus string, bool and null.
    /// </summary>
    internal static class KitJson
    {
        internal sealed class JsonNumber
        {
            public readonly string Raw;
            public JsonNumber(string raw) { Raw = raw; }
            public double Value => double.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0d;
            public override string ToString() => Raw;
        }

        /// <summary>An object with its keys in order.</summary>
        internal sealed class JsonObject
        {
            public readonly List<string> Keys = new List<string>();
            private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

            public bool Has(string key) => _values.ContainsKey(key);

            public object this[string key]
            {
                get => _values.TryGetValue(key, out object v) ? v : null;
                set
                {
                    if (!_values.ContainsKey(key))
                        Keys.Add(key);
                    _values[key] = value;
                }
            }

            public void Remove(string key)
            {
                if (_values.Remove(key))
                    Keys.Remove(key);
            }

            public string Str(string key) => this[key] as string;

            public float Num(string key, float fallback = 0f) => this[key] is JsonNumber n ? (float)n.Value : fallback;

            public bool Bool(string key, bool fallback = false) => this[key] is bool b ? b : fallback;

            public JsonObject Obj(string key) => this[key] as JsonObject;

            public List<object> Arr(string key) => this[key] as List<object>;

            public float[] Floats(string key)
            {
                if (!(this[key] is List<object> list))
                    return null;
                var f = new float[list.Count];
                for (int i = 0; i < list.Count; i++)
                    f[i] = list[i] is JsonNumber n ? (float)n.Value : 0f;
                return f;
            }
        }

        // ---------- Reading ----------

        public static object Parse(string text)
        {
            int i = 0;
            // A byte order mark some editors leave
            if (text.Length > 0 && text[0] == '﻿')
                i = 1;
            object v = Value(text, ref i);
            Space(text, ref i);
            if (i < text.Length)
                throw new FormatException($"Unexpected text at {i}");
            return v;
        }

        private static void Space(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        private static object Value(string s, ref int i)
        {
            Space(s, ref i);
            if (i >= s.Length)
                throw new FormatException("Unexpected end");
            char c = s[i];
            if (c == '{')
                return Object(s, ref i);
            if (c == '[')
                return Array(s, ref i);
            if (c == '"')
                return String(s, ref i);
            if (Word(s, ref i, "true"))
                return true;
            if (Word(s, ref i, "false"))
                return false;
            if (Word(s, ref i, "null"))
                return null;
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                i++;
            if (i == start)
                throw new FormatException($"Unexpected '{c}' at {i}");
            return new JsonNumber(s.Substring(start, i - start));
        }

        private static bool Word(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                return false;
            i += word.Length;
            return true;
        }

        private static JsonObject Object(string s, ref int i)
        {
            var o = new JsonObject();
            i++;
            Space(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return o;
            }
            while (true)
            {
                Space(s, ref i);
                string key = String(s, ref i);
                Space(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException($"Expected ':' at {i}");
                i++;
                o[key] = Value(s, ref i);
                Space(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return o;
                }
                throw new FormatException($"Expected ',' or '}}' at {i}");
            }
        }

        private static List<object> Array(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            Space(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }
            while (true)
            {
                list.Add(Value(s, ref i));
                Space(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return list;
                }
                throw new FormatException($"Expected ',' or ']' at {i}");
            }
        }

        private static string String(string s, ref int i)
        {
            if (s[i] != '"')
                throw new FormatException($"Expected a string at {i}");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("Unclosed string");
        }

        // ---------- Writing ----------

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>A float as JSON (invariant, no needless digits).</summary>
        public static JsonNumber Num(float f) => new JsonNumber(Math.Round(f, 5).ToString("0.#####", CultureInfo.InvariantCulture));

        public static JsonNumber Num(int n) => new JsonNumber(n.ToString(CultureInfo.InvariantCulture));

        public static List<object> Nums(params float[] f)
        {
            var list = new List<object>(f.Length);
            foreach (float x in f)
                list.Add(Num(x));
            return list;
        }

        private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

        private static void WriteValue(StringBuilder sb, object v, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case JsonNumber n: sb.Append(n.Raw); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(Num(f).Raw); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case string s: WriteString(sb, s); break;
                case JsonObject o:
                    if (o.Keys.Count == 0)
                    {
                        sb.Append("{}");
                        break;
                    }
                    sb.Append("{\n");
                    for (int k = 0; k < o.Keys.Count; k++)
                    {
                        Indent(sb, depth + 1);
                        WriteString(sb, o.Keys[k]);
                        sb.Append(": ");
                        WriteValue(sb, o[o.Keys[k]], depth + 1);
                        sb.Append(k < o.Keys.Count - 1 ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append('}');
                    break;
                case List<object> list:
                    if (list.Count == 0)
                    {
                        sb.Append("[]");
                        break;
                    }
                    // Short lists of plain values on one line (positions, sizes)
                    bool flat = list.TrueForAll(x => !(x is JsonObject) && !(x is List<object>));
                    if (flat)
                    {
                        sb.Append('[');
                        for (int k = 0; k < list.Count; k++)
                        {
                            if (k > 0)
                                sb.Append(", ");
                            WriteValue(sb, list[k], depth + 1);
                        }
                        sb.Append(']');
                        break;
                    }
                    sb.Append("[\n");
                    for (int k = 0; k < list.Count; k++)
                    {
                        Indent(sb, depth + 1);
                        WriteValue(sb, list[k], depth + 1);
                        sb.Append(k < list.Count - 1 ? ",\n" : "\n");
                    }
                    Indent(sb, depth);
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, v.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
