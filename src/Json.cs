using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Amc
{
    // Minimal JSON reader - enough for the lyric APIs we call.
    /// <summary>
    /// Tiny hand-written JSON reader used for the lyric APIs (the project has no NuGet
    /// dependencies, so this replaces Newtonsoft). Supports objects, arrays, strings, numbers,
    /// booleans and null - enough for the QQ / NetEase responses.
    /// </summary>
    public class J
    {
        public enum K { Null, Bool, Num, Str, Arr, Obj }

        public K Kind = K.Null;
        public bool B;
        public double N;
        public string S;
        public List<J> Items;
        public Dictionary<string, J> Map;

        public J this[string key]
        {
            get
            {
                J v;
                if (Map != null && Map.TryGetValue(key, out v)) return v;
                return null;
            }
        }

        public J this[int i]
        {
            get
            {
                if (Items != null && i >= 0 && i < Items.Count) return Items[i];
                return null;
            }
        }

        public string Str(string key)
        {
            J v = this[key];
            return v == null ? null : v.AsString();
        }

        public double Num(string key, double def)
        {
            J v = this[key];
            if (v == null || v.Kind != K.Num) return def;
            return v.N;
        }

        public List<J> Arr(string key)
        {
            J v = this[key];
            if (v == null || v.Kind != K.Arr) return null;
            return v.Items;
        }

        public string AsString()
        {
            if (Kind == K.Str) return S;
            if (Kind == K.Num) return N.ToString(CultureInfo.InvariantCulture);
            if (Kind == K.Bool) return B ? "true" : "false";
            return null;
        }

        public static J Parse(string text)
        {
            int i = 0;
            J v = ParseValue(text, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static J ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return new J();
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return new J { Kind = K.Str, S = ParseString(s, ref i) };
            if (c == 't') { i += 4; return new J { Kind = K.Bool, B = true }; }
            if (c == 'f') { i += 5; return new J { Kind = K.Bool, B = false }; }
            if (c == 'n') { i += 4; return new J(); }
            return ParseNumber(s, ref i);
        }

        static J ParseObject(string s, ref int i)
        {
            var o = new J { Kind = K.Obj, Map = new Dictionary<string, J>() };
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                J val = ParseValue(s, ref i);
                o.Map[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return o;
        }

        static J ParseArray(string s, ref int i)
        {
            var a = new J { Kind = K.Arr, Items = new List<J>() };
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (i < s.Length)
            {
                J val = ParseValue(s, ref i);
                a.Items.Add(val);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return a;
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        static J ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            double d = 0;
            double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            return new J { Kind = K.Num, N = d };
        }
    }
}
