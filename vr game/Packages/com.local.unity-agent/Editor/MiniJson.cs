using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Local.UnityAgent
{
    /// <summary>
    /// Minimal JSON parse/serialize (objects/arrays/string escapes/number/bool/null).
    /// Values: Dictionary&lt;string,object&gt;, List&lt;object&gt;, string, double, bool, null.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            var v = p.ParseValue();
            p.SkipWs();
            if (!p.Eof) throw new FormatException("Trailing junk at " + p.Pos);
            return v;
        }

        public static string Stringify(object value)
        {
            var sb = new StringBuilder(256);
            Write(sb, value);
            return sb.ToString();
        }

        public static Dictionary<string, object> AsObject(object v)
        {
            return v as Dictionary<string, object>;
        }

        public static List<object> AsArray(object v)
        {
            return v as List<object>;
        }

        public static string AsString(object v, string fallback = null)
        {
            if (v == null) return fallback;
            if (v is string s) return s;
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? fallback;
        }

        public static bool TryGet(Dictionary<string, object> obj, string key, out object value)
        {
            if (obj != null && obj.TryGetValue(key, out value)) return true;
            value = null;
            return false;
        }

        public static string GetString(Dictionary<string, object> obj, string key, string fallback = null)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            return AsString(v, fallback);
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback = false)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is bool b) return b;
            if (v is double d) return Math.Abs(d) > double.Epsilon;
            if (v is string s && bool.TryParse(s, out var pb)) return pb;
            return fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            // 进程内直接构造的字典常放 int/long，之前只认 double 会让这些值静默掉回 fallback。
            if (v is int i32) return i32;
            if (v is long i64) return (int)i64;
            if (v is double d) return (int)d;
            if (v is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
            return fallback;
        }

        public static double GetDouble(Dictionary<string, object> obj, string key, double fallback = 0)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is double d) return d;
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return x;
            return fallback;
        }

        static void Write(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d))
                        sb.Append("null");
                    else
                        sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case Dictionary<string, object> dict:
                    sb.Append('{');
                    var first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key ?? "");
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
                case IDictionary idict:
                    sb.Append('{');
                    first = true;
                    foreach (DictionaryEntry e in idict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? "");
                        sb.Append(':');
                        Write(sb, e.Value);
                    }
                    sb.Append('}');
                    return;
                case IList list:
                    sb.Append('[');
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Write(sb, list[i]);
                    }
                    sb.Append(']');
                    return;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                    return;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;

            public Parser(string s) { _s = s; _i = 0; }
            public int Pos => _i;
            public bool Eof => _i >= _s.Length;

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                    else break;
                }
            }

            public object ParseValue()
            {
                SkipWs();
                if (Eof) throw new FormatException("Unexpected end");
                var c = _s[_i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't' || c == 'f') return ParseBool();
                if (c == 'n') return ParseNull();
                if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                throw new FormatException("Unexpected '" + c + "' at " + _i);
            }

            Dictionary<string, object> ParseObject()
            {
                _i++; // {
                var obj = new Dictionary<string, object>();
                SkipWs();
                if (!Eof && _s[_i] == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWs();
                    if (Eof || _s[_i] != '"') throw new FormatException("Expected string key at " + _i);
                    var key = ParseString();
                    SkipWs();
                    if (Eof || _s[_i] != ':') throw new FormatException("Expected ':' at " + _i);
                    _i++;
                    var val = ParseValue();
                    obj[key] = val;
                    SkipWs();
                    if (Eof) throw new FormatException("Unterminated object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; break; }
                    throw new FormatException("Expected ',' or '}' at " + _i);
                }
                return obj;
            }

            List<object> ParseArray()
            {
                _i++; // [
                var list = new List<object>();
                SkipWs();
                if (!Eof && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWs();
                    if (Eof) throw new FormatException("Unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; break; }
                    throw new FormatException("Expected ',' or ']' at " + _i);
                }
                return list;
            }

            string ParseString()
            {
                _i++; // "
                var sb = new StringBuilder();
                while (!Eof)
                {
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (Eof) throw new FormatException("Bad escape");
                        var e = _s[_i++];
                        switch (e)
                        {
                            case '"':
                            case '\\':
                            case '/': sb.Append(e); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_i + 4 > _s.Length) throw new FormatException("Bad \\u");
                                var hex = _s.Substring(_i, 4);
                                _i += 4;
                                if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                    throw new FormatException("Bad \\u" + hex);
                                sb.Append((char)code);
                                break;
                            default:
                                throw new FormatException("Bad escape \\" + e);
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                throw new FormatException("Unterminated string");
            }

            object ParseBool()
            {
                if (_s.Substring(_i).StartsWith("true", StringComparison.Ordinal)) { _i += 4; return true; }
                if (_s.Substring(_i).StartsWith("false", StringComparison.Ordinal)) { _i += 5; return false; }
                throw new FormatException("Expected bool at " + _i);
            }

            object ParseNull()
            {
                if (_s.Substring(_i).StartsWith("null", StringComparison.Ordinal)) { _i += 4; return null; }
                throw new FormatException("Expected null at " + _i);
            }

            object ParseNumber()
            {
                var start = _i;
                if (_s[_i] == '-') _i++;
                if (Eof || _s[_i] < '0' || _s[_i] > '9') throw new FormatException("Bad number at " + start);
                if (_s[_i] == '0') _i++;
                else while (!Eof && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                if (!Eof && _s[_i] == '.')
                {
                    _i++;
                    if (Eof || _s[_i] < '0' || _s[_i] > '9') throw new FormatException("Bad fraction");
                    while (!Eof && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                }
                if (!Eof && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (!Eof && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    if (Eof || _s[_i] < '0' || _s[_i] > '9') throw new FormatException("Bad exponent");
                    while (!Eof && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                }
                var raw = _s.Substring(start, _i - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                    throw new FormatException("Bad number " + raw);
                return num;
            }
        }
    }
}
